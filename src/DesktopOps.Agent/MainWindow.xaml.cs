using System.Windows;
using DesktopOps.Agent.Services;

namespace DesktopOps.Agent;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        TrayIcon.Icon = TrayIconFactory.Create();
        TrayIcon.ToolTipText = "DesktopOps Agent";
        TrayIcon.Visibility = Visibility.Visible;

        Hide();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TrayIcon.ShowBalloonTip(
            "DesktopOps Agent",
            "Running in the notification area. Right-click the blue D icon for updates.",
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    private AgentOrchestrator? Orchestrator =>
        ((App)System.Windows.Application.Current).GetOrchestrator();

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is null)
        {
            TrayIcon.ShowBalloonTip(
                "DesktopOps",
                "Agent is still starting. Try again in a moment.",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
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
        if (Orchestrator.RestartScheduled)
        {
            TrayIcon.ShowBalloonTip(
                "DesktopOps",
                "Agent update staged. Restarting…",
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
            TrayIcon.Dispose();
            System.Windows.Application.Current.Shutdown();
            return;
        }

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
        TrayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnTrayDoubleClick(object sender, RoutedEventArgs e)
    {
        OnCheckUpdatesClick(sender, e);
    }
}
