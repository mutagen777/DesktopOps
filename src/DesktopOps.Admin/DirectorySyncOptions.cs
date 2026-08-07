namespace DesktopOps.Admin;

/// <summary>Background directory sync and optional Entra ID (Graph) settings.</summary>
public sealed class DirectorySyncOptions
{
    public const string SectionName = "DirectorySync";

    /// <summary>When true, Admin periodically syncs groups that have ActiveDirectoryGroup set.</summary>
    public bool Enabled { get; set; }

    /// <summary>Minutes between full sync passes (minimum 5).</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>Microsoft Entra ID app registration used for Graph group member sync.</summary>
    public EntraDirectoryOptions Entra { get; set; } = new();
}

/// <summary>Client-credentials settings for Microsoft Graph.</summary>
public sealed class EntraDirectoryOptions
{
    /// <summary>Directory (tenant) ID.</summary>
    public string? TenantId { get; set; }

    /// <summary>Application (client) ID.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret value.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>True when TenantId, ClientId, and ClientSecret are all set.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}
