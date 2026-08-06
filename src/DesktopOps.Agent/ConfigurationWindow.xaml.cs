using System.Windows;
using DesktopOps.Agent.Services;

namespace DesktopOps.Agent;

public partial class ConfigurationWindow : Window
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly AgentUiSettings _settings;

    public ConfigurationWindow(AgentOrchestrator orchestrator, AgentUiSettings settings)
    {
        _orchestrator = orchestrator;
        _settings = settings;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        InstallDirText.Text = _orchestrator.InstallationDirectory;
        VersionText.Text = _orchestrator.AgentVersion.ToString();
        LastSearchText.Text = _orchestrator.LastSearchUtc?.ToString("g")
            ?? _settings.LastSearchUtc?.ToString("g")
            ?? "Noch keine Suche";
        AutoInstallCheck.IsChecked = _settings.AutoInstallOnStartup;
        NotifyCheck.IsChecked = _settings.NotifyWhenUpdatesAvailable;
        ProgramsGrid.ItemsSource = _orchestrator.GetProgramInventory();
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        var search = new SearchWindow(_orchestrator) { Owner = this };
        search.ShowDialog();
        LastSearchText.Text = _orchestrator.LastSearchUtc?.ToString("g") ?? LastSearchText.Text;
        ProgramsGrid.ItemsSource = _orchestrator.GetProgramInventory();

        if (search.ResultCount > 0)
        {
            var update = new UpdateWindow(_orchestrator) { Owner = this };
            update.ShowDialog();
            ProgramsGrid.ItemsSource = _orchestrator.GetProgramInventory();
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        _settings.AutoInstallOnStartup = AutoInstallCheck.IsChecked == true;
        _settings.NotifyWhenUpdatesAvailable = NotifyCheck.IsChecked == true;
        _settings.LastSearchUtc = _orchestrator.LastSearchUtc ?? _settings.LastSearchUtc;
        _settings.Save();
        DialogResult = true;
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
