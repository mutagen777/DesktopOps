namespace DesktopOps.Updates;

public sealed class PreparedUpdatePackage
{
    public required UpdateRelease Release { get; init; }

    public required string PackagePath { get; init; }

    public Guid ClientId { get; init; }

    public DateTimeOffset DownloadedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
