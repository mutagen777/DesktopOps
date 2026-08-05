namespace DesktopOps.Updates;

public interface IUpdateService
{
    UpdateOptions Options { get; }

    Task<RegisteredClient> RegisterClientAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AssignedProgram>> GetAssignmentsAsync(CancellationToken cancellationToken = default);

    Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<PreparedUpdatePackage> PrepareUpdateAsync(UpdateRelease release, CancellationToken cancellationToken = default);

    Task ReportStatusAsync(
        Guid releaseId,
        DeploymentStatus status,
        string? message = null,
        string? installedVersion = null,
        CancellationToken cancellationToken = default);
}
