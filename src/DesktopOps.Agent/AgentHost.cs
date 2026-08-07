using System.IO;
using DesktopOps.Agent.Services;
using DesktopOps.Diagnostics;
using DesktopOps.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DesktopOps.Agent;

internal sealed class AgentHost : IDisposable
{
    private readonly IHost _host;

    public AgentHost()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                var agentSection = context.Configuration.GetSection("Agent");
                var serverUri = agentSection["ServerUri"] ?? "https://localhost:7022";
                var installDir = string.IsNullOrWhiteSpace(agentSection["InstallationDirectory"])
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopOps", "Programs")
                    : agentSection["InstallationDirectory"]!;
                var userName = string.IsNullOrWhiteSpace(agentSection["UserName"])
                    ? Environment.UserName
                    : agentSection["UserName"]!;
                var pollMinutes = int.TryParse(agentSection["PollIntervalMinutes"], out var minutes) ? minutes : 30;
                var allowSelfUpdate = !bool.TryParse(agentSection["AllowSelfUpdate"], out var selfUpdate)
                    || selfUpdate;

                var updateOptions = new UpdateOptions
                {
                    ServerUri = new Uri(serverUri),
                    ProgramSlug = string.Empty,
                    UserName = userName,
                    MachineName = Environment.MachineName,
                    InstallationDirectory = installDir,
                    CurrentVersion = typeof(AgentHost).Assembly.GetName().Version ?? new Version(0, 3, 0),
                    ApiKey = agentSection["ApiKey"],
                    TrustedCmsThumbprints = agentSection["TrustedCmsThumbprints"],
                    RequirePackageCmsSignature = bool.TryParse(agentSection["RequirePackageCmsSignature"], out var requireCms)
                        && requireCms,
                    EnableDeltaUpdates = !bool.TryParse(agentSection["EnableDeltaUpdates"], out var enableDelta)
                        || enableDelta,
                    DeltaMaxSizeRatio = double.TryParse(
                        agentSection["DeltaMaxSizeRatio"],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var deltaRatio)
                        ? Math.Clamp(deltaRatio, 0.05, 1.0)
                        : 0.8
                };

                var diagnosticsOptions = new DiagnosticsOptions
                {
                    AppName = "DesktopOps.Agent",
                    AppVersion = updateOptions.CurrentVersion.ToString()
                };

                services.AddSingleton(updateOptions);
                services.AddSingleton(diagnosticsOptions);
                services.AddSingleton<IDiagnosticsService>(sp => new DiagnosticsService(sp.GetRequiredService<DiagnosticsOptions>()));
                services.AddSingleton<UpdateService>();
                services.AddSingleton<IUpdateService>(sp => sp.GetRequiredService<UpdateService>());
                services.AddSingleton(sp => new ProgramInstallService(sp.GetRequiredService<UpdateOptions>().InstallationDirectory));
                services.AddSingleton(sp =>
                {
                    var diagnostics = sp.GetRequiredService<IDiagnosticsService>();
                    var loggerFactory = LoggerFactory.Create(builder =>
                    {
                        builder.AddProvider(diagnostics.CreateLoggerProvider());
                        builder.AddDebug();
                        builder.SetMinimumLevel(LogLevel.Information);
                    });

                    return new AgentSelfUpdateService(
                        sp.GetRequiredService<UpdateService>(),
                        loggerFactory.CreateLogger<AgentSelfUpdateService>());
                });
                services.AddSingleton(sp =>
                {
                    var diagnostics = sp.GetRequiredService<IDiagnosticsService>();
                    var loggerFactory = LoggerFactory.Create(builder =>
                    {
                        builder.AddProvider(diagnostics.CreateLoggerProvider());
                        builder.AddDebug();
                        builder.SetMinimumLevel(LogLevel.Information);
                    });

                    return new AgentOrchestrator(
                        sp.GetRequiredService<IUpdateService>(),
                        sp.GetRequiredService<ProgramInstallService>(),
                        sp.GetRequiredService<AgentSelfUpdateService>(),
                        diagnostics,
                        loggerFactory.CreateLogger<AgentOrchestrator>(),
                        TimeSpan.FromMinutes(pollMinutes),
                        allowSelfUpdate);
                });
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
            })
            .Build();

        Orchestrator = _host.Services.GetRequiredService<AgentOrchestrator>();
    }

    public AgentOrchestrator Orchestrator { get; }

    public Task StartAsync() => Orchestrator.StartAsync();

    public Task StopAsync() => Orchestrator.StopAsync();

    public void Dispose() => _host.Dispose();
}
