using Microsoft.EntityFrameworkCore;

namespace DesktopOps.Server.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(DesktopOpsDbContext dbContext, CancellationToken cancellationToken = default)
    {
        // EnsureCreated is not concurrency-safe across processes; tolerate already-created schemas.
        try
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Another process created the schema first.
        }
    }
}
