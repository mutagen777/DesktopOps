using System.Windows;
using DesktopOps.Agent.Resources;
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
        Title = Loc.Get("ConfigTitle");
        SaveButton.Content = Loc.Get("Save");
        CancelButton.Content = Loc.Get("Cancel");
        GeneralTab.Header = Loc.Get("TabGeneral");
        ProgramsTab.Header = Loc.Get("TabPrograms");
        InstallFolderLabel.Text = Loc.Get("InstallFolder");
        LastSearchLabel.Text = Loc.Get("LastSearchLabel");
        SearchButton.Content = Loc.Get("SearchEllipsis");
        AgentVersionLabel.Text = Loc.Get("AgentVersion");
        AutoInstallCheck.Content = Loc.Get("AutoInstall");
        NotifyCheck.Content = Loc.Get("NotifyUpdates");
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ColName.Header = Loc.Get("ColName");
        ColLatest.Header = Loc.Get("ColLatest");
        ColInstalled.Header = Loc.Get("ColInstalled");

        InstallDirText.Text = _orchestrator.InstallationDirectory;
        VersionText.Text = _orchestrator.AgentVersion.ToString();
        LastSearchText.Text = _orchestrator.LastSearchUtc?.ToString("g")
            ?? _settings.LastSearchUtc?.ToString("g")
            ?? Loc.Get("NoSearchYet");
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
