namespace DesktopOps.Updates;

public sealed class UpdateCheckResult
{
    public static UpdateCheckResult NoUpdate(Version currentVersion) =>
        new()
        {
            CurrentVersion = currentVersion,
            AvailableReleases = []
        };

    public Version CurrentVersion { get; init; } = new(0, 0, 0);

    public bool IsUpdateAvailable => AvailableRelease is not null;

    public UpdateRelease? AvailableRelease => AvailableReleases.FirstOrDefault();

    public IReadOnlyList<UpdateRelease> AvailableReleases { get; init; } = [];
}
