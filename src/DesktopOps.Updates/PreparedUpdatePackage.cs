namespace DesktopOps.Updates;

public sealed class PreparedUpdatePackage
{
    public required UpdateRelease Release { get; init; }

    public required string PackagePath { get; init; }

    public Guid ClientId { get; init; }

    public DateTimeOffset DownloadedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// True when the package was rebuilt from a delta and entry hashes were verified
    /// (full ZIP SHA-256 / CMS may not match the published artifact).
    /// </summary>
    public bool VerifiedViaDeltaEntries { get; init; }
}
