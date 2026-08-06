using System.Windows;
using System.Windows.Threading;
using DesktopOps.Agent.Resources;
using DesktopOps.Agent.Services;
using MessageBox = System.Windows.MessageBox;

namespace DesktopOps.Agent;

public partial class MainWindow : Window
{
    private readonly AgentUiSettings _uiSettings = AgentUiSettings.Load();
    private bool _startupHandled;
    private DispatcherTimer? _remindTimer;

    public MainWindow()
    {
        InitializeComponent();

        TrayIcon.Icon = TrayIconFactory.Create();
        TrayIcon.ToolTipText = Loc.Get("TrayTooltip");
        TrayIcon.Visibility = Visibility.Visible;

        ConfigurationMenuItem.Header = Loc.Get("MenuConfiguration");
        SearchUpdatesMenuItem.Header = Loc.Get("MenuSearchUpdates");
        LastSearchMenuItem.Header = Loc.Get("LastSearchNone");
        ReinstallMenuItem.Header = Loc.Get("MenuReinstall");
        DiagnosticsMenuItem.Header = Loc.Get("MenuDiagnostics");
        VersionMenuItem.Header = Loc.Format("Version", "–");
        ExitMenuItem.Header = Loc.Get("MenuExit");

        Hide();
        Loaded += OnLoaded;
    }

    private AgentOrchestrator? Orchestrator =>
        ((App)System.Windows.Application.Current).GetOrchestrator();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        TrayIcon.ShowBalloonTip(
            Loc.Get("TrayTooltip"),
            Loc.Get("BalloonRunning"),
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);

        if (Orchestrator is not null)
        {
            Orchestrator.UpdatesDiscovered += OnUpdatesDiscovered;
            VersionMenuItem.Header = Loc.Format("Version", Orchestrator.AgentVersion);
        }

        Dispatcher.BeginInvoke(async () => await RunStartupAsync(), DispatcherPriority.ApplicationIdle);
    }

    private async Task RunStartupAsync()
    {
        if (_startupHandled)
        {
            return;
        }

        _startupHandled = true;
        var orchestrator = Orchestrator;
        if (orchestrator is null)
        {
            return;
        }

        var count = await orchestrator.SearchUpdatesAsync();
        PersistLastSearch(orchestrator);

        if (count is null)
        {
            TrayIcon.ShowBalloonTip(
                Loc.Get("TrayTooltip"),
                Loc.Get("ServerUnreachableRetry"),
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
            return;
        }

        if (count == 0)
        {
            return;
        }

        if (_uiSettings.AutoInstallOnStartup)
        {
            var window = new UpdateWindow(orchestrator, autoExecute: true);
            window.ShowDialog();
            HandleRestart(orchestrator);
            return;
        }

        NotifyUpdatesAvailable(count.Value);
    }

    private void OnUpdatesDiscovered(int count)
    {
        Dispatcher.Invoke(() =>
        {
            PersistLastSearch(Orchestrator);
            if (!_uiSettings.NotifyWhenUpdatesAvailable || count <= 0 || IsActiveDialogOpen())
            {
                return;
            }

            // Background poll: toast only (like Autoupdater non-force path).
            if (_startupHandled)
            {
                NotifyUpdatesAvailable(count);
            }
        });
    }

    private void NotifyUpdatesAvailable(int count)
    {
        var text = count == 1
            ? Loc.Get("UpdateAvailableOne")
            : Loc.Format("UpdateAvailableMany", count);
        TrayIcon.ShowBalloonTip(Loc.Get("TrayTooltip"), text, Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        ScheduleRemind();
    }

    private void ScheduleRemind()
    {
        _remindTimer?.Stop();
        _remindTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        _remindTimer.Tick += async (_, _) =>
        {
            _remindTimer.Stop();
            if (Orchestrator is null || IsActiveDialogOpen())
            {
                return;
            }

            var count = await Orchestrator.SearchUpdatesAsync();
            PersistLastSearch(Orchestrator);
            if (count > 0)
            {
                NotifyUpdatesAvailable(count.Value);
            }
        };
        _remindTimer.Start();
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var last = Orchestrator?.LastSearchUtc ?? _uiSettings.LastSearchUtc;
        LastSearchMenuItem.Header = last is null
            ? Loc.Get("LastSearchNone")
            : Loc.Format("LastSearch", last.Value.ToString("g"));
        if (Orchestrator is not null)
        {
            VersionMenuItem.Header = Loc.Format("Version", Orchestrator.AgentVersion);
        }
    }

    private void OnConfigurationClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is null)
        {
            return;
        }

        var window = new ConfigurationWindow(Orchestrator, _uiSettings);
        window.ShowDialog();
    }

    private async void OnSearchUpdatesClick(object sender, RoutedEventArgs e) => await SearchAndOfferUpdatesAsync();

    private async void OnTrayDoubleClick(object sender, RoutedEventArgs e) => await SearchAndOfferUpdatesAsync();

    private async Task SearchAndOfferUpdatesAsync()
    {
        if (Orchestrator is null)
        {
            TrayIcon.ShowBalloonTip(
                Loc.Get("TrayTooltip"),
                Loc.Get("AgentStillStarting"),
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
            return;
        }

        var search = new SearchWindow(Orchestrator);
        search.ShowDialog();
        PersistLastSearch(Orchestrator);

        if (search.ResultCount is null)
        {
            TrayIcon.ShowBalloonTip(
                Loc.Get("TrayTooltip"),
                Loc.Get("ServerUnreachable"),
                Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Warning);
            return;
        }

        if (search.ResultCount == 0)
        {
            MessageBox.Show(
                Loc.Get("NoUpdatesAvailable"),
                Loc.Get("TrayTooltip"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var update = new UpdateWindow(Orchestrator);
        update.ShowDialog();
        HandleRestart(Orchestrator);
    }

    private async void OnReinstallClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            Loc.Get("ReinstallConfirm"),
            Loc.Get("ReinstallTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await Orchestrator.ReinstallProgramsAsync();
        PersistLastSearch(Orchestrator);
        if (Orchestrator.PendingUpdates.Count == 0)
        {
            MessageBox.Show(
                Loc.Get("NothingToReinstall"),
                Loc.Get("TrayTooltip"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var update = new UpdateWindow(Orchestrator);
        update.ShowDialog();
        HandleRestart(Orchestrator);
    }

    private async void OnExportDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is null)
        {
            return;
        }

        var path = await Orchestrator.ExportDiagnosticsAsync();
        TrayIcon.ShowBalloonTip(
            Loc.Get("TrayTooltip"),
            Loc.Format("DiagnosticsExported", path),
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        if (Orchestrator is not null)
        {
            Orchestrator.UpdatesDiscovered -= OnUpdatesDiscovered;
        }

        _remindTimer?.Stop();
        TrayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void PersistLastSearch(AgentOrchestrator? orchestrator)
    {
        if (orchestrator?.LastSearchUtc is null)
        {
            return;
        }

        _uiSettings.LastSearchUtc = orchestrator.LastSearchUtc;
        _uiSettings.Save();
    }

    private void HandleRestart(AgentOrchestrator orchestrator)
    {
        if (!orchestrator.RestartScheduled)
        {
            return;
        }

        TrayIcon.ShowBalloonTip(
            Loc.Get("TrayTooltip"),
            Loc.Get("AgentUpdateRestart"),
            Hardcodet.Wpf.TaskbarNotification.BalloonIcon.Info);
        TrayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private static bool IsActiveDialogOpen()
    {
        foreach (Window window in System.Windows.Application.Current.Windows)
        {
            if (window is SearchWindow or UpdateWindow or ConfigurationWindow && window.IsVisible)
            {
                return true;
            }
        }

        return false;
    }
}
