using System.Windows;
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
            ? "Keine ausstehenden Aktionen."
            : $"{ActionList.Items.Count} Aktion(en) bereit.";
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
        CloseButton.Content = "Abbrechen";
        _cts = new CancellationTokenSource();

        var progress = new Progress<UpdateProgress>(report =>
        {
            StepText.Text = $"{report.Current} / {report.Total} – {report.Title}: {report.Detail}";
            Progress.Value = Math.Clamp(report.Fraction, 0, 1);
        });

        try
        {
            var count = await _orchestrator.InstallPendingAsync(progress, _cts.Token);
            if (_orchestrator.RestartScheduled)
            {
                StepText.Text = "Agent-Update vorbereitet. Neustart…";
                DialogResult = true;
                Close();
                System.Windows.Application.Current.Shutdown();
                return;
            }

            StepText.Text = count == 0 ? "Nichts installiert." : $"{count} Aktion(en) ausgeführt.";
            Progress.Value = 1;
            RefreshList();
            CloseButton.Content = "Schließen";
        }
        catch (OperationCanceledException)
        {
            StepText.Text = "Abgebrochen.";
            CloseButton.Content = "Schließen";
        }
        catch (Exception ex)
        {
            StepText.Text = ex.Message;
            CloseButton.Content = "Schließen";
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
