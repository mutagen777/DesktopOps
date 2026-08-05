using DesktopOps.Admin.Components;
using DesktopOps.Server.Data;
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

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

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

app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
