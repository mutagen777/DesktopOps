namespace DesktopOps.Updates;

public interface IUpdateService
{
    UpdateOptions Options { get; }

    Task<RegisteredClient> RegisterClientAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssignedProgram>> GetAssignmentsAsync(CancellationToken cancellationToken = default);

    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<PreparedUpdatePackage> PrepareUpdateAsync(
        UpdateRelease release,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads and verifies a release package. When <paramref name="basePackagePath"/> and
    /// <paramref name="installedVersion"/> match an available delta, reconstructs the full ZIP
    /// from the delta and falls back to a full download on any failure.
    /// </summary>
    Task<PreparedUpdatePackage> PrepareUpdateAsync(
        UpdateRelease release,
        string? basePackagePath,
        string? installedVersion,
        CancellationToken cancellationToken = default);

    Task ReportStatusAsync(
        Guid releaseId,
        DeploymentStatus status,
        string? message = null,
        string? installedVersion = null,
        CancellationToken cancellationToken = default);
}
