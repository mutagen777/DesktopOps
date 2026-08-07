namespace DesktopOps.Admin;

/// <summary>Fails fast when Production is missing required hardening settings.</summary>
internal static class ProductionGuards
{
    public static void EnsureAdminReady(
        IHostEnvironment environment,
        SecurityOptions security,
        string connectionString)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        if (!security.Enabled)
        {
            throw new InvalidOperationException(
                "Production requires Security:Enabled=true with AD groups configured.");
        }

        if (string.IsNullOrWhiteSpace(security.ADGroup)
            || string.IsNullOrWhiteSpace(security.DeveloperADGroup))
        {
            throw new InvalidOperationException(
                "Production requires Security:ADGroup and Security:DeveloperADGroup.");
        }

        if (string.IsNullOrWhiteSpace(connectionString)
            || connectionString.Contains("desktopops-demo.db", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains(@"\source\repos\", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production requires ConnectionStrings:DesktopOps pointing at a non-demo database.");
        }
    }
}
