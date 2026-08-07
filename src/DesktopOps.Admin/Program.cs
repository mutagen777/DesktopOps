using DesktopOps.Admin;
using DesktopOps.Admin.Components;
using DesktopOps.Admin.Services;
using DesktopOps.Server.Data;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DesktopOps")
    ?? "Data Source=desktopops.db";

var storageRoot = builder.Configuration["Storage:RootPath"];
builder.Services.AddSingleton(new StorageOptions
{
    RootPath = Path.GetFullPath(storageRoot ?? Path.Combine(AppContext.BaseDirectory, "storage"))
});
builder.Services.AddSingleton<PackageStorageService>();

var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
builder.Services.AddDbContextFactory<DesktopOpsDbContext>(options =>
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

var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>()
    ?? new SecurityOptions();
builder.Services.AddSingleton(security);
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.Configure<DirectorySyncOptions>(
    builder.Configuration.GetSection(DirectorySyncOptions.SectionName));
builder.Services.AddSingleton<DirectoryGroupSyncService>();
builder.Services.AddHostedService<DesktopOps.Admin.Services.DirectorySyncHostedService>();
builder.Services.AddSingleton<EntraGraphDirectoryLookup>();
if (OperatingSystem.IsWindows())
{
    builder.Services.AddSingleton<WindowsDirectoryAccountLookup>();
}

builder.Services.AddSingleton<IDirectoryAccountLookup>(static sp =>
{
    WindowsDirectoryAccountLookup? windows = null;
    if (OperatingSystem.IsWindows())
    {
        windows = sp.GetRequiredService<WindowsDirectoryAccountLookup>();
    }

    var entra = sp.GetRequiredService<EntraGraphDirectoryLookup>();
    if (windows is null && !entra.IsAvailable)
    {
        return new NullDirectoryAccountLookup();
    }

    return new CompositeDirectoryAccountLookup(windows, entra);
});

builder.Services.AddLocalization();

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supported = new[] { "en", "de", "fr", "es", "it", "ru" };
    options.SetDefaultCulture("en")
        .AddSupportedCultures(supported)
        .AddSupportedUICultures(supported);
    options.ApplyCurrentCultureToResponseHeaders = true;
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

if (security.Enabled)
{
    if (string.IsNullOrWhiteSpace(security.DeveloperADGroup))
    {
        throw new InvalidOperationException(
            "Security.DeveloperADGroup must be set when Security.Enabled is true.");
    }

    if (string.IsNullOrWhiteSpace(security.ADGroup))
    {
        throw new InvalidOperationException(
            "Security.ADGroup must be set when Security.Enabled is true.");
    }

    builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
        .AddNegotiate();

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        options.AddPolicy("Manager", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireRole(security.ADGroup, security.DeveloperADGroup);
        });

        options.AddPolicy("Developer", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireRole(security.DeveloperADGroup);
        });
    });
}
else
{
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Manager", policy => policy.RequireAssertion(static _ => true));
        options.AddPolicy("Developer", policy => policy.RequireAssertion(static _ => true));
    });
}

var app = builder.Build();

ProductionGuards.EnsureAdminReady(app.Environment, security, connectionString);

using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DesktopOpsDbContext>>();
    await using var dbContext = await dbContextFactory.CreateDbContextAsync();
    await DatabaseInitializer.InitializeAsync(dbContext);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found");
app.UseHttpsRedirection();
app.UseRequestLocalization();

if (security.Enabled)
{
    app.UseAuthentication();
}

app.UseAuthorization();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
