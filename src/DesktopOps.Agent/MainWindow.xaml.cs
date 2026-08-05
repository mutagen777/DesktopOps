using System.Drawing;
using System.Windows;
using DesktopOps.Agent.Services;

namespace DesktopOps.Agent;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TrayIcon.Icon = SystemIcons.Application;
        Hide();
    }

    private AgentOrchestrator? Orchestrator =>
        ((App)System.Windows.Application.Current).GetOrchestrator();

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is null)
        {
            return;
        }

        var count = await Orchestrator.SearchUpdatesAsync();
        TrayIcon.ShowBalloonTip(
            "DesktopOps",
            count is null ? "Unable to reach the DesktopOps server." :
            count == 0 ? "All assigned programs are up to date." :
            $"{count} update(s) available.",
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is null)
        {
            return;
        }

        var installed = await Orchestrator.InstallPendingAsync();
        TrayIcon.ShowBalloonTip(
            "DesktopOps",
            installed == 0 ? "No pending updates." : $"Installed {installed} update(s).",
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    private async void OnExportDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is null)
        {
            return;
        }

        var path = await Orchestrator.ExportDiagnosticsAsync();
        TrayIcon.ShowBalloonTip(
            "DesktopOps",
            $"Diagnostics exported to {path}",
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e)
    {
        OnCheckUpdatesClick(sender, e);
    }
}
