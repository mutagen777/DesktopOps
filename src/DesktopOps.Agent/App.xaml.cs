using System.Windows;
using DesktopOps.Agent.Services;

namespace DesktopOps.Agent;

public partial class App : System.Windows.Application
{
    private AgentHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
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
