using System.Windows;
using DesktopOps.Agent.Services;

namespace DesktopOps.Agent;

public partial class SearchWindow : Window
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly CancellationTokenSource _cts = new();
    private bool _started;

    public int? ResultCount { get; private set; }

    public SearchWindow(AgentOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) => _cts.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            ResultCount = await _orchestrator.SearchUpdatesAsync(_cts.Token);
            if (ResultCount is null)
            {
                StatusText.Text = "Server nicht erreichbar.";
                await Task.Delay(900);
            }
            else if (ResultCount == 0)
            {
                StatusText.Text = "Keine Updates gefunden.";
                await Task.Delay(700);
            }

            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            DialogResult = false;
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            await Task.Delay(1200);
            DialogResult = false;
        }
        finally
        {
            Close();
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        DialogResult = false;
        Close();
    }
}
