using DesktopOps.Server;
using DesktopOps.Server.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .Enrich.FromLogContext());

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

var connectionString = builder.Configuration.GetConnectionString("DesktopOps")
    ?? "Data Source=desktopops.db";

var storageRoot = builder.Configuration["Storage:RootPath"];
var storageOptions = new StorageOptions
{
    RootPath = Path.GetFullPath(storageRoot ?? Path.Combine(AppContext.BaseDirectory, "storage"))
};

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";

builder.Services.AddSingleton(storageOptions);
builder.Services.AddSingleton<PackageStorageService>();
builder.Services.Configure<PackageSigningOptions>(
    builder.Configuration.GetSection(PackageSigningOptions.SectionName));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<PackageSigningOptions>>().Value);

var apiKeyOptions = builder.Configuration.GetSection(ApiKeyOptions.SectionName).Get<ApiKeyOptions>()
    ?? new ApiKeyOptions();
if (string.IsNullOrWhiteSpace(apiKeyOptions.ApiKeyHeader))
{
    apiKeyOptions.ApiKeyHeader = ApiKeyOptions.DefaultHeaderName;
}

builder.Services.AddSingleton(apiKeyOptions);
builder.Services.AddDbContext<DesktopOpsDbContext>(options =>
{
    if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<DesktopOpsDbContext>("database")
    .AddCheck("storage", () =>
    {
        try
        {
            Directory.CreateDirectory(storageOptions.RootPath);
            var probe = Path.Combine(storageOptions.RootPath, ".health");
            File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("O"));
            File.Delete(probe);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("writable");
        }
        catch (Exception)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Package storage not writable.");
        }
    });

var app = builder.Build();

ProductionGuards.EnsureServerReady(
    app.Environment,
    apiKeyOptions,
    provider,
    connectionString,
    app.Logger);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DesktopOpsDbContext>();
    await DatabaseInitializer.InitializeAsync(dbContext);
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = static check => check.Name is "database" or "storage"
});

app.MapGet("/", (ApiKeyOptions security) => Results.Ok(new
{
    name = "DesktopOps Server",
    version = "0.3.0",
    product = "DesktopOps",
    apiKeyRequired = security.IsEnabled,
    adminApiKeyRequired = security.HasSeparateAdminKey
}));

app.MapGet("/api/programs", async (DesktopOpsDbContext dbContext) =>
{
    var programs = await dbContext.Programs
        .OrderBy(static item => item.Name)
        .ToListAsync();

    return Results.Ok(programs);
});

app.MapPost("/api/programs", async (CreateProgramRequest request, DesktopOpsDbContext dbContext) =>
{
    var program = new ManagedProgram
    {
        Name = request.Name.Trim(),
        Slug = request.Slug.Trim().ToLowerInvariant(),
        ShortName = string.IsNullOrWhiteSpace(request.ShortName) ? null : request.ShortName.Trim(),
        Description = request.Description?.Trim()
    };

    dbContext.Programs.Add(program);
    await dbContext.SaveChangesAsync();
    return Results.Ok(program);
});

app.MapGet("/api/groups", async (DesktopOpsDbContext dbContext) =>
{
    var groups = await dbContext.UserGroups
        .Include(static item => item.Members)
        .OrderBy(static item => item.Name)
        .Select(group => new
        {
            group.Id,
            group.Name,
            group.Description,
            Members = group.Members.Select(member => new
            {
                member.Id,
                member.UserName,
                member.WindowsSid
            }).ToList()
        })
        .ToListAsync();

    return Results.Ok(groups);
});

app.MapPost("/api/groups", async (CreateGroupRequest request, DesktopOpsDbContext dbContext) =>
{
    var group = new UserGroup
    {
        Name = request.Name.Trim(),
        Description = request.Description?.Trim(),
        Members = request.UserNames
            .Where(static userName => !string.IsNullOrWhiteSpace(userName))
            .Select(userName => new UserGroupMember { UserName = userName.Trim() })
            .ToList()
    };

    dbContext.UserGroups.Add(group);
    await dbContext.SaveChangesAsync();
    return Results.Ok(new
    {
        group.Id,
        group.Name,
        group.Description,
        Members = group.Members.Select(member => new
        {
            member.Id,
            member.UserName,
            member.WindowsSid
        }).ToList()
    });
});

app.MapPost("/api/groups/{groupId:guid}/members", async (Guid groupId, AddGroupMemberRequest request, DesktopOpsDbContext dbContext) =>
{
    var group = await dbContext.UserGroups
        .Include(static item => item.Members)
        .FirstOrDefaultAsync(item => item.Id == groupId);

    if (group is null)
    {
        return Results.NotFound();
    }

    group.Members.Add(new UserGroupMember
    {
        UserGroupId = groupId,
        UserName = request.UserName.Trim(),
        WindowsSid = string.IsNullOrWhiteSpace(request.WindowsSid) ? null : request.WindowsSid.Trim()
    });

    await dbContext.SaveChangesAsync();
    return Results.Ok(group);
});

app.MapPost("/api/assignments", async (CreateAssignmentRequest request, DesktopOpsDbContext dbContext) =>
{
    var license = await LicenseService.EvaluateAsync(dbContext);
    if (license.BlocksMutations)
    {
        return Results.Json(
            new { error = "LicenseRequired", message = license.ErrorMessage ?? "License invalid or expired." },
            statusCode: StatusCodes.Status402PaymentRequired);
    }

    var existing = await dbContext.ProgramAssignments.FirstOrDefaultAsync(item =>
        item.ProgramId == request.ProgramId && item.UserGroupId == request.UserGroupId);
    if (existing is not null)
    {
        return Results.Ok(existing);
    }

    var assignment = new ProgramAssignment
    {
        ProgramId = request.ProgramId,
        UserGroupId = request.UserGroupId
    };

    dbContext.ProgramAssignments.Add(assignment);
    await dbContext.SaveChangesAsync();
    return Results.Ok(assignment);
});

app.MapGet("/api/releases", async (DesktopOpsDbContext dbContext) =>
{
    var releases = await dbContext.ReleasePackages
        .AsNoTracking()
        .Include(static item => item.Program)
        .ToListAsync();

    return Results.Ok(releases
        .OrderByDescending(static item => item.CreatedAtUtc)
        .Select(release => new
        {
            release.Id,
            release.ProgramId,
            ProgramName = release.Program?.Name,
            ProgramSlug = release.Program?.Slug,
            release.Version,
            release.OriginalFileName,
            release.PackageHash,
            release.PackageSize,
            release.DeltaHash,
            release.DeltaSize,
            release.DeltaBaseVersion,
            release.ReleaseNotes,
            release.IsMandatory,
            release.RolloutPercent,
            release.CreatedAtUtc,
            release.PublishedAtUtc
        }));
});

app.MapPost("/api/releases", async (
    HttpRequest request,
    DesktopOpsDbContext dbContext,
    PackageStorageService storage,
    PackageSigningOptions signing,
    CancellationToken cancellationToken) =>
{
    var form = await request.ReadFormAsync(cancellationToken);

    var packageFile = form.Files["package"];
    if (packageFile is null)
    {
        return Results.BadRequest("Missing package file.");
    }

    if (!Guid.TryParse(form["programId"], out var programId))
    {
        return Results.BadRequest("Invalid program id.");
    }

    var program = await dbContext.Programs.FirstOrDefaultAsync(item => item.Id == programId, cancellationToken);
    if (program is null)
    {
        return Results.NotFound("Program not found.");
    }

    var version = form["version"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(version))
    {
        return Results.BadRequest("Version is required.");
    }

    await using var packageStream = packageFile.OpenReadStream();
    var stored = await storage.SavePackageAsync(
        program.Slug,
        version,
        packageFile.FileName,
        packageStream,
        cancellationToken);

    string? signaturePath = null;
    try
    {
        var signatureFile = form.Files["signature"];
        if (signatureFile is not null)
        {
            await using var signatureStream = signatureFile.OpenReadStream();
            signaturePath = await storage.SaveAndVerifyDetachedSignatureAsync(
                stored.RelativePath,
                signatureStream,
                signing.IsConfigured ? signing.CertificateThumbprint : null,
                cancellationToken);
        }
        else if (signing.IsConfigured)
        {
            signaturePath = storage.SignPackage(stored.RelativePath, signing.CertificateThumbprint!);
        }
        else if (signing.RequireSignature)
        {
            storage.TryDeletePackageArtifacts(stored.RelativePath);
            return Results.BadRequest("Package signature is required (upload signature or configure Signing:CertificateThumbprint).");
        }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        storage.TryDeletePackageArtifacts(stored.RelativePath);
        return Results.BadRequest($"Package signature failed: {ex.Message}");
    }

    var rolloutPercent = 100;
    if (int.TryParse(form["rolloutPercent"], out var parsedRollout))
    {
        rolloutPercent = Math.Clamp(parsedRollout, 0, 100);
    }

    var release = new ReleasePackage
    {
        ProgramId = programId,
        Version = version,
        OriginalFileName = packageFile.FileName,
        PackagePath = stored.RelativePath,
        PackageHash = stored.Sha256Hash,
        PackageSize = stored.SizeBytes,
        SignaturePath = signaturePath,
        ReleaseNotes = form["releaseNotes"].ToString(),
        IsMandatory = bool.TryParse(form["isMandatory"], out var isMandatory) && isMandatory,
        RolloutPercent = rolloutPercent
    };

    var previousPackages = await dbContext.ReleasePackages
        .AsNoTracking()
        .Where(item => item.ProgramId == programId)
        .ToListAsync(cancellationToken);
    var previous = ReleaseDeltaHelper.FindPreviousRelease(previousPackages, version);
    if (previous is not null)
    {
        ReleaseDeltaHelper.TryAttachDelta(storage, release, previous);
    }

    dbContext.ReleasePackages.Add(release);
    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new
    {
        release.Id,
        release.ProgramId,
        release.Version,
        release.OriginalFileName,
        release.PackageHash,
        release.PackageSize,
        release.SignaturePath,
        release.DeltaPath,
        release.DeltaHash,
        release.DeltaSize,
        release.DeltaBaseVersion,
        release.ReleaseNotes,
        release.IsMandatory,
        release.RolloutPercent,
        release.CreatedAtUtc,
        release.PublishedAtUtc
    });
});

app.MapPost("/api/releases/{releaseId:guid}/publish", async (
    Guid releaseId,
    PublishReleaseRequest? request,
    DesktopOpsDbContext dbContext,
    PackageSigningOptions signing) =>
{
    var license = await LicenseService.EvaluateAsync(dbContext);
    if (license.BlocksMutations)
    {
        return Results.Json(
            new { error = "LicenseRequired", message = license.ErrorMessage ?? "License invalid or expired." },
            statusCode: StatusCodes.Status402PaymentRequired);
    }

    var release = await dbContext.ReleasePackages.FirstOrDefaultAsync(item => item.Id == releaseId);
    if (release is null)
    {
        return Results.NotFound();
    }

    if (signing.RequireSignature && string.IsNullOrWhiteSpace(release.SignaturePath))
    {
        return Results.BadRequest("Package signature is required before publish.");
    }

    if (request?.RolloutPercent is int percent)
    {
        release.RolloutPercent = Math.Clamp(percent, 0, 100);
    }

    release.PublishedAtUtc = DateTimeOffset.UtcNow;
    await dbContext.SaveChangesAsync();
    return Results.Ok(new
    {
        release.Id,
        release.ProgramId,
        release.Version,
        release.PackageHash,
        release.RolloutPercent,
        release.PublishedAtUtc
    });
});

app.MapPatch("/api/releases/{releaseId:guid}/rollout", async (Guid releaseId, UpdateRolloutRequest request, DesktopOpsDbContext dbContext) =>
{
    var release = await dbContext.ReleasePackages.FirstOrDefaultAsync(item => item.Id == releaseId);
    if (release is null)
    {
        return Results.NotFound();
    }

    release.RolloutPercent = Math.Clamp(request.RolloutPercent, 0, 100);
    await dbContext.SaveChangesAsync();
    return Results.Ok(new { release.Id, release.Version, release.RolloutPercent, release.PublishedAtUtc });
});

app.MapPost("/api/releases/{releaseId:guid}/delta", async (
    Guid releaseId,
    DesktopOpsDbContext dbContext,
    PackageStorageService storage,
    CancellationToken cancellationToken) =>
{
    var release = await dbContext.ReleasePackages.FirstOrDefaultAsync(item => item.Id == releaseId, cancellationToken);
    if (release is null)
    {
        return Results.NotFound();
    }

    var previousPackages = await dbContext.ReleasePackages
        .AsNoTracking()
        .Where(item => item.ProgramId == release.ProgramId && item.Id != release.Id)
        .ToListAsync(cancellationToken);
    var previous = ReleaseDeltaHelper.FindPreviousRelease(previousPackages, release.Version);
    if (previous is null)
    {
        return Results.BadRequest("No older published package version found to build a delta from.");
    }

    var previousDeltaPath = release.DeltaPath;
    if (!ReleaseDeltaHelper.TryAttachDelta(storage, release, previous))
    {
        return Results.BadRequest("Delta was not created (packages identical or delta not smaller enough).");
    }

    if (!string.IsNullOrWhiteSpace(previousDeltaPath)
        && !string.Equals(previousDeltaPath, release.DeltaPath, StringComparison.OrdinalIgnoreCase))
    {
        ReleaseDeltaHelper.TryDeleteDeltaOnly(storage, previousDeltaPath);
    }

    await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new
    {
        release.Id,
        release.DeltaPath,
        release.DeltaHash,
        release.DeltaSize,
        release.DeltaBaseVersion
    });
});

app.MapGet("/api/rollouts", async (DesktopOpsDbContext dbContext) =>
{
    var events = await dbContext.DeploymentEvents
        .AsNoTracking()
        .Include(static item => item.ClientRegistration)
        .Include(static item => item.ReleasePackage!)
            .ThenInclude(static item => item.Program)
        .ToListAsync();

    return Results.Ok(events
        .OrderByDescending(static item => item.TimestampUtc)
        .Take(200)
        .Select(item => new
        {
            item.Id,
            item.Status,
            item.Message,
            item.TimestampUtc,
            Client = item.ClientRegistration == null
                ? null
                : new
                {
                    item.ClientRegistration.UserName,
                    item.ClientRegistration.MachineName,
                    item.ClientRegistration.ProgramSlug,
                    item.ClientRegistration.CurrentVersion
                },
            Release = item.ReleasePackage == null
                ? null
                : new
                {
                    item.ReleasePackage.Id,
                    item.ReleasePackage.Version,
                    ProgramName = item.ReleasePackage.Program?.Name,
                    ProgramSlug = item.ReleasePackage.Program?.Slug
                }
        }));
});

app.MapPost("/api/clients/register", async (ClientRegistrationRequest request, DesktopOpsDbContext dbContext) =>
{
    var programSlug = string.IsNullOrWhiteSpace(request.ProgramSlug)
        ? ClientConstants.AgentProgramSlug
        : request.ProgramSlug.Trim().ToLowerInvariant();

    var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item =>
        item.ProgramSlug == programSlug &&
        item.UserName == request.UserName &&
        item.MachineName == request.MachineName);

    if (client is null)
    {
        client = new ClientRegistration
        {
            ProgramSlug = programSlug,
            UserName = request.UserName.Trim(),
            MachineName = request.MachineName.Trim(),
            WindowsSid = string.IsNullOrWhiteSpace(request.WindowsSid) ? null : request.WindowsSid.Trim(),
            CurrentVersion = request.CurrentVersion.Trim()
        };

        dbContext.ClientRegistrations.Add(client);
    }
    else
    {
        client.CurrentVersion = request.CurrentVersion.Trim();
        client.WindowsSid = string.IsNullOrWhiteSpace(request.WindowsSid) ? client.WindowsSid : request.WindowsSid.Trim();
        client.LastSeenAtUtc = DateTimeOffset.UtcNow;
    }

    await dbContext.SaveChangesAsync();
    return Results.Ok(client);
});

app.MapGet("/api/clients/{clientId:guid}/programs", async (Guid clientId, DesktopOpsDbContext dbContext) =>
{
    var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item => item.Id == clientId);
    if (client is null)
    {
        return Results.NotFound();
    }

    var programs = await GetAssignedProgramsQuery(dbContext, client.UserName, client.WindowsSid)
        .Select(program => new
        {
            program.Id,
            program.Name,
            program.Slug,
            program.ShortName
        })
        .ToListAsync();

    return Results.Ok(programs);
});

app.MapGet("/api/clients/{clientId:guid}/assignments", async (Guid clientId, DesktopOpsDbContext dbContext) =>
{
    var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item => item.Id == clientId);
    if (client is null)
    {
        return Results.NotFound();
    }

    var programs = await GetAssignedProgramsQuery(dbContext, client.UserName, client.WindowsSid)
        .Include(static item => item.Releases)
        .ToListAsync();

    var assignments = programs.Select(program =>
    {
        var latest = program.Releases
            .Where(static release => release.PublishedAtUtc.HasValue)
            .Where(release => RolloutEligibility.IsIncluded(client.UserName, release.Id, release.RolloutPercent))
            .OrderByDescending(static release => ParseVersion(release.Version))
            .FirstOrDefault();

        return new AssignedProgramDto(
            program.Id,
            program.Name,
            program.Slug,
            program.ShortName,
            latest is null
                ? null
                : new AssignedReleaseDto(
                    latest.Id,
                    latest.Version,
                    latest.ReleaseNotes,
                    latest.IsMandatory,
                    latest.PackageHash,
                    latest.PackageSize,
                    $"/api/packages/{latest.Id}?clientId={client.Id}",
                    string.IsNullOrWhiteSpace(latest.SignaturePath)
                        ? null
                        : $"/api/packages/{latest.Id}/signature?clientId={client.Id}",
                    string.IsNullOrWhiteSpace(latest.DeltaPath)
                        ? null
                        : $"/api/packages/{latest.Id}/delta?clientId={client.Id}",
                    latest.DeltaHash,
                    latest.DeltaSize,
                    latest.DeltaBaseVersion));
    }).ToList();

    return Results.Ok(assignments);
});

app.MapGet("/api/clients/{clientId:guid}/updates", async (Guid clientId, string programSlug, DesktopOpsDbContext dbContext) =>
{
    var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item => item.Id == clientId);
    if (client is null)
    {
        return Results.NotFound();
    }

    var normalizedSlug = programSlug.Trim().ToLowerInvariant();
    var program = await dbContext.Programs
        .Include(static item => item.Releases)
        .Include(static item => item.Assignments)
            .ThenInclude(static assignment => assignment.UserGroup!)
            .ThenInclude(static group => group.Members)
        .FirstOrDefaultAsync(item => item.Slug == normalizedSlug);

    if (program is null)
    {
        return Results.NotFound();
    }

    var isAssigned = program.Assignments.Any(assignment =>
        assignment.UserGroup!.Members.Any(member => MemberMatchesClient(member, client.UserName, client.WindowsSid)));

    if (!isAssigned)
    {
        return Results.Ok(Array.Empty<object>());
    }

    var updates = program.Releases
        .Where(static release => release.PublishedAtUtc.HasValue)
        .Where(release => RolloutEligibility.IsIncluded(client.UserName, release.Id, release.RolloutPercent))
        .Select(release => new
        {
            release.Id,
            release.Version,
            release.ReleaseNotes,
            release.IsMandatory,
            release.PackageHash,
            release.PackageSize,
            release.RolloutPercent,
            packageUrl = $"/api/packages/{release.Id}?clientId={clientId}",
            signatureUrl = string.IsNullOrWhiteSpace(release.SignaturePath)
                ? null
                : $"/api/packages/{release.Id}/signature?clientId={clientId}",
            deltaUrl = string.IsNullOrWhiteSpace(release.DeltaPath)
                ? null
                : $"/api/packages/{release.Id}/delta?clientId={clientId}",
            release.DeltaHash,
            release.DeltaSize,
            release.DeltaBaseVersion
        })
        .OrderByDescending(static release => ParseVersion(release.Version))
        .ToList();

    return Results.Ok(updates);
});

app.MapPost("/api/clients/{clientId:guid}/events", async (Guid clientId, ClientDeploymentEventRequest request, DesktopOpsDbContext dbContext) =>
{
    var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item => item.Id == clientId);
    if (client is null)
    {
        return Results.NotFound();
    }

    if (request.Status == DeploymentStatus.Installed && !string.IsNullOrWhiteSpace(request.InstalledVersion))
    {
        client.CurrentVersion = request.InstalledVersion.Trim();
        client.LastSeenAtUtc = DateTimeOffset.UtcNow;
    }

    var deploymentEvent = new DeploymentEvent
    {
        ClientRegistrationId = clientId,
        ReleasePackageId = request.ReleaseId,
        Status = request.Status,
        Message = request.Message
    };

    dbContext.DeploymentEvents.Add(deploymentEvent);
    await dbContext.SaveChangesAsync();
    return Results.Ok(deploymentEvent);
});

app.MapGet("/api/packages/{releaseId:guid}", async (
    Guid releaseId,
    Guid? clientId,
    HttpContext httpContext,
    DesktopOpsDbContext dbContext,
    PackageStorageService storage) =>
{
    var release = await dbContext.ReleasePackages
        .Include(static item => item.Program)
        .FirstOrDefaultAsync(item => item.Id == releaseId);
    if (release is null)
    {
        return Results.NotFound();
    }

    var role = httpContext.Items[ApiKeyOptions.RoleItemKey] as string;
    if (string.Equals(role, ApiKeyOptions.RoleAgent, StringComparison.Ordinal))
    {
        if (clientId is null)
        {
            return Results.BadRequest(new { error = "clientId query parameter is required for agent downloads." });
        }

        var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item => item.Id == clientId.Value);
        if (client is null)
        {
            return Results.NotFound();
        }

        var assigned = await GetAssignedProgramsQuery(dbContext, client.UserName, client.WindowsSid)
            .AnyAsync(program => program.Id == release.ProgramId);
        if (!assigned)
        {
            return Results.Json(
                new { error = "Forbidden", message = "Release is not assigned to this client." },
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var absolutePath = storage.GetAbsolutePath(release.PackagePath);
    if (!File.Exists(absolutePath))
    {
        return Results.NotFound();
    }

    return Results.File(absolutePath, "application/octet-stream", release.OriginalFileName);
});

app.MapGet("/api/packages/{releaseId:guid}/signature", async (
    Guid releaseId,
    Guid? clientId,
    HttpContext httpContext,
    DesktopOpsDbContext dbContext,
    PackageStorageService storage) =>
{
    var release = await dbContext.ReleasePackages
        .FirstOrDefaultAsync(item => item.Id == releaseId);
    if (release is null || string.IsNullOrWhiteSpace(release.SignaturePath))
    {
        return Results.NotFound();
    }

    var role = httpContext.Items[ApiKeyOptions.RoleItemKey] as string;
    if (string.Equals(role, ApiKeyOptions.RoleAgent, StringComparison.Ordinal))
    {
        if (clientId is null)
        {
            return Results.BadRequest(new { error = "clientId query parameter is required for agent downloads." });
        }

        var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item => item.Id == clientId.Value);
        if (client is null)
        {
            return Results.NotFound();
        }

        var assigned = await GetAssignedProgramsQuery(dbContext, client.UserName, client.WindowsSid)
            .AnyAsync(program => program.Id == release.ProgramId);
        if (!assigned)
        {
            return Results.Json(
                new { error = "Forbidden", message = "Release is not assigned to this client." },
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var absolutePath = storage.GetAbsolutePath(release.SignaturePath);
    if (!File.Exists(absolutePath))
    {
        return Results.NotFound();
    }

    var downloadName = Path.GetFileName(release.SignaturePath);
    return Results.File(absolutePath, "application/pkcs7-signature", downloadName);
});

app.MapGet("/api/packages/{releaseId:guid}/delta", async (
    Guid releaseId,
    Guid? clientId,
    HttpContext httpContext,
    DesktopOpsDbContext dbContext,
    PackageStorageService storage) =>
{
    var release = await dbContext.ReleasePackages
        .FirstOrDefaultAsync(item => item.Id == releaseId);
    if (release is null || string.IsNullOrWhiteSpace(release.DeltaPath))
    {
        return Results.NotFound();
    }

    var role = httpContext.Items[ApiKeyOptions.RoleItemKey] as string;
    if (string.Equals(role, ApiKeyOptions.RoleAgent, StringComparison.Ordinal))
    {
        if (clientId is null)
        {
            return Results.BadRequest(new { error = "clientId query parameter is required for agent downloads." });
        }

        var client = await dbContext.ClientRegistrations.FirstOrDefaultAsync(item => item.Id == clientId.Value);
        if (client is null)
        {
            return Results.NotFound();
        }

        var assigned = await GetAssignedProgramsQuery(dbContext, client.UserName, client.WindowsSid)
            .AnyAsync(program => program.Id == release.ProgramId);
        if (!assigned)
        {
            return Results.Json(
                new { error = "Forbidden", message = "Release is not assigned to this client." },
                statusCode: StatusCodes.Status403Forbidden);
        }
    }

    var absolutePath = storage.GetAbsolutePath(release.DeltaPath);
    if (!File.Exists(absolutePath))
    {
        return Results.NotFound();
    }

    var downloadName = Path.GetFileName(release.DeltaPath);
    return Results.File(absolutePath, "application/octet-stream", downloadName);
});

app.Run();

static IQueryable<ManagedProgram> GetAssignedProgramsQuery(
    DesktopOpsDbContext dbContext,
    string userName,
    string? windowsSid)
{
    var normalizedUser = userName.Trim().ToLowerInvariant();
    var normalizedSid = string.IsNullOrWhiteSpace(windowsSid) ? null : windowsSid.Trim();

    return dbContext.Programs
        .Where(program => program.IsActive && program.Assignments.Any(assignment =>
            assignment.UserGroup!.Members.Any(member =>
                member.UserName.ToLower() == normalizedUser
                || (normalizedSid != null
                    && member.WindowsSid != null
                    && member.WindowsSid == normalizedSid))))
        .OrderBy(static program => program.Name);
}

static bool MemberMatchesClient(UserGroupMember member, string userName, string? windowsSid)
{
    if (string.Equals(member.UserName, userName, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return !string.IsNullOrWhiteSpace(member.WindowsSid)
        && !string.IsNullOrWhiteSpace(windowsSid)
        && string.Equals(member.WindowsSid, windowsSid, StringComparison.OrdinalIgnoreCase);
}

static Version ParseVersion(string value)
{
    return Version.TryParse(value, out var version) ? version : new Version(0, 0);
}

internal static class ClientConstants
{
    public const string AgentProgramSlug = "_agent_";
}

internal sealed record CreateProgramRequest(string Name, string Slug, string? ShortName, string? Description);

internal sealed record CreateGroupRequest(string Name, string? Description, List<string> UserNames);

internal sealed record AddGroupMemberRequest(string UserName, string? WindowsSid);

internal sealed record CreateAssignmentRequest(Guid ProgramId, Guid UserGroupId);

internal sealed record ClientRegistrationRequest(
    string ProgramSlug,
    string UserName,
    string MachineName,
    string CurrentVersion,
    string? WindowsSid);

internal sealed record ClientDeploymentEventRequest(
    Guid ReleaseId,
    DeploymentStatus Status,
    string? Message,
    string? InstalledVersion);

internal sealed record PublishReleaseRequest(int? RolloutPercent);

internal sealed record UpdateRolloutRequest(int RolloutPercent);

internal sealed record AssignedProgramDto(
    Guid Id,
    string Name,
    string Slug,
    string? ShortName,
    AssignedReleaseDto? LatestRelease);

internal sealed record AssignedReleaseDto(
    Guid Id,
    string Version,
    string? ReleaseNotes,
    bool IsMandatory,
    string PackageHash,
    long PackageSize,
    string PackageUrl,
    string? SignatureUrl,
    string? DeltaUrl,
    string? DeltaHash,
    long? DeltaSize,
    string? DeltaBaseVersion);
