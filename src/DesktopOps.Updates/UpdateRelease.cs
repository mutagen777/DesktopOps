namespace DesktopOps.Updates;

public sealed class UpdateRelease
{
    public Guid Id { get; set; }

    public string Version { get; set; } = string.Empty;

    public string PackageUrl { get; set; } = string.Empty;

    public string? ReleaseNotes { get; set; }

    public bool IsMandatory { get; set; }

    public string PackageHash { get; set; } = string.Empty;

    public long PackageSize { get; set; }

    /// <summary>Optional URL for the detached CMS signature (.p7s).</summary>
    public string? SignatureUrl { get; set; }

    /// <summary>Optional URL for a delta ZIP (zip of changed files).</summary>
    public string? DeltaUrl { get; set; }

    public string? DeltaHash { get; set; }

    public long? DeltaSize { get; set; }

    /// <summary>Local version that must be installed for the delta to apply.</summary>
    public string? DeltaBaseVersion { get; set; }
}
