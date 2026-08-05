using DesktopOps.Diagnostics;
using DesktopOps.Updates;
using Microsoft.Extensions.Logging;

namespace DesktopOps.Agent.Services;

public sealed class AgentOrchestrator
{
    private readonly IUpdateService _updateService;
    private readonly ProgramInstallService _installService;
    private readonly AgentSelfUpdateService _selfUpdate;
    private readonly IDiagnosticsService _diagnostics;
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly bool _allowSelfUpdate;
    private readonly object _sync = new();
    private List<ProgramUpdateCandidate> _pending = [];
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;
    private Task? _pollLoop;
    private bool _restartScheduled;

    public AgentOrchestrator(
        IUpdateService updateService,
        ProgramInstallService installService,
        AgentSelfUpdateService selfUpdate,
        IDiagnosticsService diagnostics,
        ILogger<AgentOrchestrator> logger,
        TimeSpan pollInterval,
        bool allowSelfUpdate = true)
    {
        _updateService = updateService;
        _installService = installService;
        _selfUpdate = selfUpdate;
        _diagnostics = diagnostics;
        _logger = logger;
        _pollInterval = pollInterval;
        _allowSelfUpdate = allowSelfUpdate;
    }

    public bool RestartScheduled => _restartScheduled;

    public IReadOnlyList<ProgramUpdateCandidate> PendingUpdates
    {
        get
        {
            lock (_sync)
            {
                return _pending.ToList();
            }
        }
    }

    public Task StartAsync()
    {
        _cts = new CancellationTokenSource();
        _pollLoop = PollLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        _timer?.Dispose();
        if (_pollLoop is not null)
        {
            try
            {
                await _pollLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    public async Task<int?> SearchUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _updateService.RegisterClientAsync(cancellationToken);
            var assignments = await _updateService.GetAssignmentsAsync(cancellationToken);
            var candidates = _installService.GetUpdateCandidates(assignments)
                .Select(NormalizeCandidate)
                .Where(static item => item is not null)
                .Cast<ProgramUpdateCandidate>()
                .Where(item => item.Action is ProgramUpdateAction.Add or ProgramUpdateAction.Update or ProgramUpdateAction.Delete)
                .ToList();

            lock (_sync)
            {
                _pending = candidates;
            }

            _logger.LogInformation("Found {Count} pending program changes", candidates.Count);
            return candidates.Count;
        }
        catch (Exception ex)
        {
            _diagnostics.CaptureException(ex, "SearchUpdates");
            _logger.LogError(ex, "Update search failed");
            return null;
        }
    }

    public async Task<int> InstallPendingAsync(CancellationToken cancellationToken = default)
    {
        List<ProgramUpdateCandidate> pending;
        lock (_sync)
        {
            pending = _pending.ToList();
        }

        var installedCount = 0;
        foreach (var candidate in pending)
        {
            try
            {
                if (AgentSelfUpdateService.IsAgentProgram(candidate.Program.Slug))
                {
                    if (!_allowSelfUpdate)
                    {
                        _logger.LogInformation("Agent self-update disabled by configuration.");
                        continue;
                    }

                    if (candidate.Program.LatestRelease is null)
                    {
                        continue;
                    }

                    var scheduled = await _selfUpdate.TryScheduleSelfUpdateAsync(candidate.Program, cancellationToken);
                    if (scheduled)
                    {
                        installedCount++;
                        _restartScheduled = true;
                        break;
                    }

                    continue;
                }

                if (candidate.Action == ProgramUpdateAction.Delete)
                {
                    _installService.RemoveProgram(candidate.Program.Slug);
                    installedCount++;
                    continue;
                }

                if (candidate.Program.LatestRelease is null)
                {
                    continue;
                }

                if (candidate.Action == ProgramUpdateAction.Update)
                {
                    _installService.BackupProgram(candidate.Program.Slug);
                    _installService.RemoveProgram(candidate.Program.Slug);
                }

                var prepared = await _updateService.PrepareUpdateAsync(candidate.Program.LatestRelease, cancellationToken);
                var ok = await _installService.InstallFromZipAsync(
                    candidate.Program,
                    prepared.PackagePath,
                    candidate.Program.LatestRelease.PackageHash,
                    cancellationToken);

                if (!ok)
                {
                    await _updateService.ReportStatusAsync(
                        candidate.Program.LatestRelease.Id,
                        DeploymentStatus.Failed,
                        "Install or hash verification failed.",
                        cancellationToken: cancellationToken);
                    continue;
                }

                await _updateService.ReportStatusAsync(
                    candidate.Program.LatestRelease.Id,
                    DeploymentStatus.Installed,
                    "Installed by DesktopOps Agent.",
                    candidate.Program.LatestRelease.Version,
                    cancellationToken);
                installedCount++;
            }
            catch (Exception ex)
            {
                _diagnostics.CaptureException(ex, $"Install:{candidate.Program.Slug}");
                _logger.LogError(ex, "Failed to apply update for {Slug}", candidate.Program.Slug);
                if (candidate.Program.LatestRelease is not null)
                {
                    await _updateService.ReportStatusAsync(
                        candidate.Program.LatestRelease.Id,
                        DeploymentStatus.Failed,
                        ex.Message,
                        cancellationToken: cancellationToken);
                }
            }
        }

        if (!_restartScheduled)
        {
            await SearchUpdatesAsync(cancellationToken);
        }

        return installedCount;
    }

    public Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return _diagnostics.ExportDiagnosticsAsync(cancellationToken);
    }

    private ProgramUpdateCandidate? NormalizeCandidate(ProgramUpdateCandidate candidate)
    {
        if (!AgentSelfUpdateService.IsAgentProgram(candidate.Program.Slug))
        {
            return candidate;
        }

        // Never remove the agent via the normal uninstall path.
        if (candidate.Action == ProgramUpdateAction.Delete)
        {
            return null;
        }

        if (candidate.Program.LatestRelease is null)
        {
            return null;
        }

        if (!Version.TryParse(candidate.Program.LatestRelease.Version, out var remoteVersion))
        {
            return null;
        }

        var localVersion = _updateService.Options.CurrentVersion;
        if (remoteVersion <= localVersion)
        {
            return null;
        }

        return new ProgramUpdateCandidate
        {
            Program = candidate.Program,
            Action = ProgramUpdateAction.Update,
            InstalledVersion = localVersion.ToString()
        };
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        await SearchUpdatesAsync(cancellationToken);
        _timer = new PeriodicTimer(_pollInterval);
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                await SearchUpdatesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
