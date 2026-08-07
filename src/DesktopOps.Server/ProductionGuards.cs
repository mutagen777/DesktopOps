namespace DesktopOps.Server;

/// <summary>Fails fast when Production is missing required hardening settings.</summary>
internal static class ProductionGuards
{
    public static void EnsureServerReady(
        IHostEnvironment environment,
        ApiKeyOptions apiKeyOptions,
        string databaseProvider,
        string connectionString)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        if (!apiKeyOptions.IsEnabled)
        {
            throw new InvalidOperationException(
                "Production requires Security:ApiKey (or env Security__ApiKey). Empty API key is only allowed for local demo.");
        }

        if (string.IsNullOrWhiteSpace(connectionString)
            || connectionString.Contains("desktopops-demo.db", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains(@"\source\repos\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production requires ConnectionStrings:DesktopOps pointing at a non-demo database.");
        }

        if (string.Equals(databaseProvider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            // Allowed but discouraged — log via exception message guidance is in docs.
            // Soft warning: do not throw; SQL Server is recommended.
        }
    }
}
