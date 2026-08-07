namespace DesktopOps.Updates;

public sealed class UpdateOptions
{
    public Uri? ServerUri { get; set; }

    public Version CurrentVersion { get; set; } = new(0, 0, 0);

    /// <summary>Program slug for embedded clients. Leave empty for the tray agent.</summary>
    public string ProgramSlug { get; set; } = string.Empty;

    public string UserName { get; set; } = Environment.UserName;

    public string MachineName { get; set; } = Environment.MachineName;

    public string? WindowsSid { get; set; }

    /// <summary>
    /// Shared API key for <c>X-DesktopOps-Key</c> (must match server <c>Security:ApiKey</c> when set).
    /// </summary>
    public string? ApiKey { get; set; }

    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopOps",
        "Updates");

    public string InstallationDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopOps",
        "Programs");

    /// <summary>
    /// Comma-separated certificate thumbprints trusted for detached CMS package signatures.
    /// When empty, a present signature is still cryptographically verified but any valid signer is accepted.
    /// </summary>
    public string? TrustedCmsThumbprints { get; set; }

    /// <summary>When true, releases without a signature URL are rejected after download.</summary>
    public bool RequirePackageCmsSignature { get; set; }
}
