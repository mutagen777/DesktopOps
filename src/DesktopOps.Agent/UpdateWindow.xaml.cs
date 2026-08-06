using System.Windows;
using DesktopOps.Agent.Resources;
using DesktopOps.Agent.Services;

namespace DesktopOps.Agent;

public partial class UpdateWindow : Window
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly bool _autoExecute;
    private CancellationTokenSource? _cts;
    private bool _running;

    public UpdateWindow(AgentOrchestrator orchestrator, bool autoExecute = false)
    {
        _orchestrator = orchestrator;
        _autoExecute = autoExecute;
        InitializeComponent();
        Title = Loc.Get("UpdateTitle");
        PendingHeader.Text = Loc.Get("PendingActions");
        ExecuteButton.Content = Loc.Get("Execute");
        CloseButton.Content = Loc.Get("Cancel");
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshList();
        if (_autoExecute && ActionList.Items.Count > 0)
        {
            await RunAsync();
        }
    }

    private void RefreshList()
    {
        ActionList.Items.Clear();
        foreach (var item in _orchestrator.PendingUpdates)
        {
            ActionList.Items.Add(AgentOrchestrator.FormatAction(item));
        }

        ExecuteButton.IsEnabled = ActionList.Items.Count > 0 && !_running;
        StepText.Text = ActionList.Items.Count == 0
            ? Loc.Get("NoPendingActions")
            : Loc.Format("ActionsReady", ActionList.Items.Count);
    }

    private async void OnExecuteClick(object sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        ExecuteButton.IsEnabled = false;
        CloseButton.Content = Loc.Get("Cancel");
        _cts = new CancellationTokenSource();

        var progress = new Progress<UpdateProgress>(report =>
        {
            StepText.Text = Loc.Format("ProgressStep", report.Current, report.Total, report.Title, report.Detail);
            Progress.Value = Math.Clamp(report.Fraction, 0, 1);
        });

        try
        {
            var count = await _orchestrator.InstallPendingAsync(progress, _cts.Token);
            if (_orchestrator.RestartScheduled)
            {
                StepText.Text = Loc.Get("AgentUpdateRestart");
                DialogResult = true;
                Close();
                System.Windows.Application.Current.Shutdown();
                return;
            }

            StepText.Text = count == 0
                ? Loc.Get("NothingInstalled")
                : Loc.Format("ActionsDone", count);
            Progress.Value = 1;
            RefreshList();
            CloseButton.Content = Loc.Get("Close");
        }
        catch (OperationCanceledException)
        {
            StepText.Text = Loc.Get("Cancelled");
            CloseButton.Content = Loc.Get("Close");
        }
        catch (Exception ex)
        {
            StepText.Text = ex.Message;
            CloseButton.Content = Loc.Get("Close");
        }
        finally
        {
            _running = false;
            ExecuteButton.IsEnabled = ActionList.Items.Count > 0;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_running && _cts is not null)
        {
            _cts.Cancel();
            return;
        }

        DialogResult = true;
        Close();
    }
}
