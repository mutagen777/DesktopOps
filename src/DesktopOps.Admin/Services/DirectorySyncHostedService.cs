using Microsoft.Extensions.Options;

namespace DesktopOps.Admin.Services;

/// <summary>Periodically syncs DesktopOps groups linked to AD/Windows groups.</summary>
public sealed class DirectorySyncHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<DirectorySyncOptions> _options;
    private readonly ILogger<DirectorySyncHostedService> _logger;

    public DirectorySyncHostedService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<DirectorySyncOptions> options,
        ILogger<DirectorySyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            var delay = TimeSpan.FromMinutes(Math.Max(5, options.IntervalMinutes <= 0 ? 60 : options.IntervalMinutes));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (!_options.CurrentValue.Enabled)
            {
                continue;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<DirectoryGroupSyncService>();
                var lookup = scope.ServiceProvider.GetRequiredService<IDirectoryAccountLookup>();
                if (!lookup.IsAvailable)
                {
                    _logger.LogDebug("Scheduled directory sync skipped (directory lookup unavailable).");
                    continue;
                }

                var results = await sync.SyncAllLinkedGroupsAsync(stoppingToken);
                var errors = results.Count(static item => item.Error is not null);
                _logger.LogInformation(
                    "Scheduled directory sync finished: {Groups} group(s), {Errors} error(s)",
                    results.Count,
                    errors);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled directory sync failed");
            }
        }
    }
}
