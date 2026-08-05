using System.IO;
using System.Windows;
using DesktopOps.Updates;

namespace DesktopOps.Sample.Wpf;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private UpdateRelease? _availableRelease;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;

        _viewModel.DiagnosticsDirectoryLabel = $"Diagnostics directory: {Path.GetDirectoryName(App.Runtime.Diagnostics.CurrentLogFilePath)}";
        _viewModel.CurrentVersionLabel = $"Current version: {App.Runtime.Updates.Options.CurrentVersion}";
        _viewModel.AppendLog($"DesktopOps runtime initialized for server {App.Runtime.Updates.Options.ServerUri}.");
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var exportPath = await App.Runtime.Diagnostics.ExportDiagnosticsAsync();
        _viewModel.ExportPathLabel = $"Last export: {exportPath}";
        _viewModel.AppendLog($"Diagnostics bundle created at '{exportPath}'.");
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        var result = await App.Runtime.Updates.CheckForUpdatesAsync();
        _availableRelease = result.AvailableRelease;

        _viewModel.UpdateStatusLabel = result.IsUpdateAvailable
            ? $"Update available: {result.AvailableRelease!.Version}"
            : "No published server update available for this user and program.";

        _viewModel.AppendLog(_viewModel.UpdateStatusLabel);
    }

    private async void DownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableRelease is null)
        {
            _viewModel.AppendLog("Run 'Check for updates' before downloading a package.");
            return;
        }

        var preparedPackage = await App.Runtime.Updates.PrepareUpdateAsync(_availableRelease);
        _viewModel.PackagePathLabel = $"Prepared package: {preparedPackage.PackagePath}";
        _viewModel.AppendLog($"Downloaded update package for {preparedPackage.Release.Version}.");
        await App.Runtime.Updates.ReportStatusAsync(_availableRelease.Id, DeploymentStatus.Installed, "Sample download completed.");
    }

    private void ThrowHandledException_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            throw new InvalidOperationException("Sample handled exception.");
        }
        catch (Exception exception)
        {
            App.Runtime.Diagnostics.CaptureException(exception, "HandledSampleException");
            _viewModel.AppendLog("Handled exception captured for diagnostics export.");
        }
    }

    private void ThrowBackgroundException_Click(object sender, RoutedEventArgs e)
    {
        Task.Run(() => throw new InvalidOperationException("Sample background exception."));
        _viewModel.AppendLog("Background exception queued. It will be captured by the runtime hooks.");
    }
}