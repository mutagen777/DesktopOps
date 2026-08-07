using System.Globalization;
using System.Windows;
using DesktopOps.Agent.Services;
using Velopack;

namespace DesktopOps.Agent;

public partial class App : System.Windows.Application
{
    private AgentHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Must run before other startup work (handles install/update hooks).
        VelopackApp.Build().Run();

        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentUICulture;

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _host = new AgentHost();
        await _host.StartAsync();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    internal AgentOrchestrator? GetOrchestrator() => _host?.Orchestrator;
}
