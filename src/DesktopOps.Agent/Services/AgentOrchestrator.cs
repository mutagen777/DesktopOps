using System.IO;
using DesktopOps.Agent.Resources;
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
    private DateTimeOffset? _lastSearchUtc;
    private string? _lastSearchError;

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

    public event Action<int>? UpdatesDiscovered;

    public bool RestartScheduled => _restartScheduled;

    public DateTimeOffset? LastSearchUtc => _lastSearchUtc;

    public string? LastSearchError => _lastSearchError;

    public string InstallationDirectory => _installService.InstallationDirectory;

    public Version AgentVersion => _updateService.Options.CurrentVersion;

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

            _lastSearchUtc = DateTimeOffset.Now;
            _lastSearchError = null;
            _logger.LogInformation("Found {Count} pending program changes", candidates.Count);
            UpdatesDiscovered?.Invoke(candidates.Count);
            return candidates.Count;
        }
        catch (Exception ex)
        {
            _lastSearchError = ex.Message;
            _diagnostics.CaptureException(ex, "SearchUpdates");
            _logger.LogError(ex, "Update search failed");
            return null;
        }
    }

    public async Task<int> InstallPendingAsync(
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        List<ProgramUpdateCandidate> pending;
        lock (_sync)
        {
            pending = _pending.ToList();
        }

        var installedCount = 0;
        var total = pending.Count;
        for (var index = 0; index < pending.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = pending[index];
            var label = FormatAction(candidate);
            progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressPreparing"), (double)index / Math.Max(total, 1)));

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

                    progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressAgentPreparing"), (index + 0.5) / Math.Max(total, 1)));
                    var scheduled = await _selfUpdate.TryScheduleSelfUpdateAsync(candidate.Program, cancellationToken);
                    if (scheduled)
                    {
                        installedCount++;
                        _restartScheduled = true;
                        progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressRestart"), 1));
                        break;
                    }

                    continue;
                }

                if (candidate.Action == ProgramUpdateAction.Delete)
                {
                    progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressRemoving"), (index + 0.5) / Math.Max(total, 1)));
                    _installService.RemoveProgram(candidate.Program.Slug);
                    installedCount++;
                    continue;
                }

                if (candidate.Program.LatestRelease is null)
                {
                    continue;
                }

                string? basePackagePath = null;
                var release = candidate.Program.LatestRelease;

                if (candidate.Action == ProgramUpdateAction.Update)
                {
                    progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressBackup"), (index + 0.2) / Math.Max(total, 1)));
                    _installService.BackupProgram(candidate.Program.Slug);

                    if (!string.IsNullOrWhiteSpace(candidate.InstalledVersion)
                        && !string.IsNullOrWhiteSpace(release.DeltaUrl)
                        && !_updateService.Options.RequirePackageCmsSignature
                        && string.Equals(
                            candidate.InstalledVersion,
                            release.DeltaBaseVersion,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        basePackagePath = Path.Combine(
                            _updateService.Options.CacheDirectory,
                            $"{SanitizePathSegment(candidate.Program.Slug)}-{SanitizePathSegment(candidate.InstalledVersion)}-base.zip");
                        if (!_installService.CreateInstallSnapshotZip(candidate.Program.Slug, basePackagePath))
                        {
                            basePackagePath = null;
                        }
                    }
                }

                progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressDownload"), (index + 0.4) / Math.Max(total, 1)));
                await _updateService.ReportStatusAsync(
                    release.Id,
                    DeploymentStatus.Downloading,
                    "Download started.",
                    cancellationToken: cancellationToken);

                var prepared = await _updateService.PrepareUpdateAsync(
                    release,
                    basePackagePath,
                    candidate.InstalledVersion,
                    cancellationToken);

                if (candidate.Action == ProgramUpdateAction.Update)
                {
                    _installService.RemoveProgram(candidate.Program.Slug);
                }

                progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressInstall"), (index + 0.7) / Math.Max(total, 1)));
                var ok = await _installService.InstallFromZipAsync(
                    candidate.Program,
                    prepared.PackagePath,
                    release.PackageHash,
                    cancellationToken,
                    skipPackageHashVerification: prepared.VerifiedViaDeltaEntries);

                if (!ok)
                {
                    await _updateService.ReportStatusAsync(
                        release.Id,
                        DeploymentStatus.Failed,
                        "Install or hash verification failed.",
                        cancellationToken: cancellationToken);
                    progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressFailed"), (index + 1.0) / Math.Max(total, 1)));
                    continue;
                }

                await _updateService.ReportStatusAsync(
                    release.Id,
                    DeploymentStatus.Installed,
                    "Installed by DesktopOps Agent.",
                    release.Version,
                    cancellationToken);
                installedCount++;
                progress?.Report(new UpdateProgress(index + 1, total, label, Loc.Get("ProgressDone"), (index + 1.0) / Math.Max(total, 1)));
            }
            catch (OperationCanceledException)
            {
                throw;
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

                progress?.Report(new UpdateProgress(index + 1, total, label, ex.Message, (index + 1.0) / Math.Max(total, 1)));
            }
        }

        if (!_restartScheduled)
        {
            await SearchUpdatesAsync(cancellationToken);
        }

        return installedCount;
    }

    public async Task ReinstallProgramsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var manifest in _installService.GetInstalledPrograms().ToList())
        {
            if (AgentSelfUpdateService.IsAgentProgram(manifest.Slug))
            {
                continue;
            }

            _installService.RemoveProgram(manifest.Slug);
        }

        await SearchUpdatesAsync(cancellationToken);
    }

    public IReadOnlyList<ProgramInventoryItem> GetProgramInventory()
    {
        var installed = _installService.GetInstalledPrograms();
        var pendingBySlug = PendingUpdates.ToDictionary(
            static item => item.Program.Slug,
            StringComparer.OrdinalIgnoreCase);

        var rows = new List<ProgramInventoryItem>();
        foreach (var local in installed)
        {
            pendingBySlug.TryGetValue(local.Slug, out var pending);
            rows.Add(new ProgramInventoryItem(
                local.Name,
                local.Slug,
                pending?.Program.LatestRelease?.Version ?? local.Version,
                local.Version));
        }

        foreach (var pending in PendingUpdates)
        {
            if (rows.Any(item => string.Equals(item.Slug, pending.Program.Slug, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            rows.Add(new ProgramInventoryItem(
                pending.Program.Name,
                pending.Program.Slug,
                pending.Program.LatestRelease?.Version,
                pending.InstalledVersion));
        }

        return rows.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        return _diagnostics.ExportDiagnosticsAsync(cancellationToken);
    }

    public static string FormatAction(ProgramUpdateCandidate candidate)
    {
        var name = string.IsNullOrWhiteSpace(candidate.Program.Name)
            ? candidate.Program.Slug
            : candidate.Program.Name;
        var version = candidate.Program.LatestRelease?.Version;

        return candidate.Action switch
        {
            ProgramUpdateAction.Add => string.IsNullOrWhiteSpace(version)
                ? Loc.Format("ActionInstall", name)
                : Loc.Format("ActionInstallVer", name, version),
            ProgramUpdateAction.Update => string.IsNullOrWhiteSpace(version)
                ? Loc.Format("ActionUpdate", name)
                : Loc.Format("ActionUpdateVer", name, version),
            ProgramUpdateAction.Delete => Loc.Format("ActionUninstall", name),
            _ => name
        };
    }

    private ProgramUpdateCandidate? NormalizeCandidate(ProgramUpdateCandidate candidate)
    {
        if (!AgentSelfUpdateService.IsAgentProgram(candidate.Program.Slug))
        {
            return candidate;
        }

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

    private static string SanitizePathSegment(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '-');
        }

        return value;
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        // Startup force-update is handled by the UI; background poll only discovers.
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
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

public sealed record UpdateProgress(
    int Current,
    int Total,
    string Title,
    string Detail,
    double Fraction);

public sealed record ProgramInventoryItem(
    string Name,
    string Slug,
    string? LatestVersion,
    string? InstalledVersion);
