using Microsoft.Extensions.Logging;

namespace DesktopOps.Server;

/// <summary>Fails fast when Production is missing required hardening settings.</summary>
internal static class ProductionGuards
{
    private static readonly string[] PlaceholderFragments =
    [
        "REPLACE-",
        "CHANGE-ME",
        "CHANGEME",
        "YOUR-",
        "EXAMPLE",
        "TODO"
    ];

    public static void EnsureServerReady(
        IHostEnvironment environment,
        ApiKeyOptions apiKeyOptions,
        string databaseProvider,
        string connectionString,
        ILogger? logger = null)
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

        ValidateSecret("Security:ApiKey", apiKeyOptions.ApiKey);

        if (!apiKeyOptions.HasSeparateAdminKey)
        {
            throw new InvalidOperationException(
                "Production requires Security:AdminApiKey distinct from Security:ApiKey so agents cannot call management APIs.");
        }

        ValidateSecret("Security:AdminApiKey", apiKeyOptions.AdminApiKey);

        if (string.Equals(apiKeyOptions.ApiKey.Trim(), apiKeyOptions.AdminApiKey.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Production requires Security:AdminApiKey to differ from Security:ApiKey.");
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
            logger?.LogWarning(
                "Production is using Sqlite. Prefer SQL Server for customer deployments (see docs/production-hardening.md).");
        }
    }

    private static void ValidateSecret(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Production requires {name}.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length < 32)
        {
            throw new InvalidOperationException($"Production requires {name} with at least 32 characters.");
        }

        foreach (var fragment in PlaceholderFragments)
        {
            if (trimmed.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Production rejects placeholder {name} values. Set a real secret via env ({name.Replace(':', '_')}).");
            }
        }
    }
}
