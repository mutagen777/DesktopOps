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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DesktopOpsDbContext>();
    await DatabaseInitializer.InitializeAsync(dbContext);
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new
{
    name = "DesktopOps Server",
    version = "0.3.0",
    product = "DesktopOps"
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
        .OrderByDescending(static item => item.CreatedAtUtc)
        .ToListAsync();

    return Results.Ok(releases.Select(release => new
    {
        release.Id,
        release.ProgramId,
        ProgramName = release.Program?.Name,
        ProgramSlug = release.Program?.Slug,
        release.Version,
        release.OriginalFileName,
        release.PackageHash,
        release.PackageSize,
        release.ReleaseNotes,
        release.IsMandatory,
        release.CreatedAtUtc,
        release.PublishedAtUtc
    }));
});

app.MapPost("/api/releases", async (HttpRequest request, DesktopOpsDbContext dbContext, PackageStorageService storage, CancellationToken cancellationToken) =>
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

    var release = new ReleasePackage
    {
        ProgramId = programId,
        Version = version,
        OriginalFileName = packageFile.FileName,
        PackagePath = stored.RelativePath,
        PackageHash = stored.Sha256Hash,
        PackageSize = stored.SizeBytes,
        ReleaseNotes = form["releaseNotes"].ToString(),
        IsMandatory = bool.TryParse(form["isMandatory"], out var isMandatory) && isMandatory
    };

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
        release.ReleaseNotes,
        release.IsMandatory,
        release.CreatedAtUtc,
        release.PublishedAtUtc
    });
});

app.MapPost("/api/releases/{releaseId:guid}/publish", async (Guid releaseId, DesktopOpsDbContext dbContext) =>
{
    var release = await dbContext.ReleasePackages.FirstOrDefaultAsync(item => item.Id == releaseId);
    if (release is null)
    {
        return Results.NotFound();
    }

    release.PublishedAtUtc = DateTimeOffset.UtcNow;
    await dbContext.SaveChangesAsync();
    return Results.Ok(release);
});

app.MapGet("/api/rollouts", async (DesktopOpsDbContext dbContext) =>
{
    var events = await dbContext.DeploymentEvents
        .Include(static item => item.ClientRegistration)
        .Include(static item => item.ReleasePackage!)
            .ThenInclude(static item => item.Program)
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
                    ProgramName = item.ReleasePackage.Program!.Name,
                    ProgramSlug = item.ReleasePackage.Program.Slug
                }
        })
        .ToListAsync();

    return Results.Ok(events);
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

    var programs = await GetAssignedProgramsQuery(dbContext, client.UserName)
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

    var programs = await GetAssignedProgramsQuery(dbContext, client.UserName)
        .Include(static item => item.Releases)
        .ToListAsync();

    var assignments = programs.Select(program =>
    {
        var latest = program.Releases
            .Where(static release => release.PublishedAtUtc.HasValue)
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
                    $"/api/packages/{latest.Id}"));
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
        assignment.UserGroup!.Members.Any(member =>
            string.Equals(member.UserName, client.UserName, StringComparison.OrdinalIgnoreCase)));

    if (!isAssigned)
    {
        return Results.Ok(Array.Empty<object>());
    }

    var updates = program.Releases
        .Where(static release => release.PublishedAtUtc.HasValue)
        .Select(release => new
        {
            release.Id,
            release.Version,
            release.ReleaseNotes,
            release.IsMandatory,
            release.PackageHash,
            release.PackageSize,
            packageUrl = $"/api/packages/{release.Id}"
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

app.MapGet("/api/packages/{releaseId:guid}", async (Guid releaseId, DesktopOpsDbContext dbContext, PackageStorageService storage) =>
{
    var release = await dbContext.ReleasePackages.FirstOrDefaultAsync(item => item.Id == releaseId);
    if (release is null)
    {
        return Results.NotFound();
    }

    var absolutePath = storage.GetAbsolutePath(release.PackagePath);
    if (!File.Exists(absolutePath))
    {
        return Results.NotFound();
    }

    return Results.File(absolutePath, "application/octet-stream", release.OriginalFileName);
});

app.Run();

static IQueryable<ManagedProgram> GetAssignedProgramsQuery(DesktopOpsDbContext dbContext, string userName)
{
    var normalizedUser = userName.Trim().ToLowerInvariant();
    return dbContext.Programs
        .Where(program => program.IsActive && program.Assignments.Any(assignment =>
            assignment.UserGroup!.Members.Any(member => member.UserName.ToLower() == normalizedUser)))
        .OrderBy(static program => program.Name);
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
    string PackageUrl);
