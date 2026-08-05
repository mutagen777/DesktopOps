using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace DesktopOps.Sample.Wpf;

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly StringBuilder _activityLog = new();
    private string _diagnosticsDirectoryLabel = string.Empty;
    private string _currentVersionLabel = string.Empty;
    private string _updateStatusLabel = "No update check has been run yet.";
    private string _exportPathLabel = "No diagnostics export created yet.";
    private string _packagePathLabel = "No update package downloaded yet.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DiagnosticsDirectoryLabel
    {
        get => _diagnosticsDirectoryLabel;
        set => SetField(ref _diagnosticsDirectoryLabel, value);
    }

    public string CurrentVersionLabel
    {
        get => _currentVersionLabel;
        set => SetField(ref _currentVersionLabel, value);
    }

    public string UpdateStatusLabel
    {
        get => _updateStatusLabel;
        set => SetField(ref _updateStatusLabel, value);
    }

    public string ExportPathLabel
    {
        get => _exportPathLabel;
        set => SetField(ref _exportPathLabel, value);
    }

    public string PackagePathLabel
    {
        get => _packagePathLabel;
        set => SetField(ref _packagePathLabel, value);
    }

    public string ActivityLog => _activityLog.ToString();

    public void AppendLog(string message)
    {
        _activityLog.AppendLine($"[{DateTimeOffset.Now:T}] {message}");
        OnPropertyChanged(nameof(ActivityLog));
    }

    private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
