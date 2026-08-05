using System.Reflection;

namespace DesktopOps.Diagnostics;

public sealed class DiagnosticsOptions
{
    public string AppName { get; set; } = Assembly.GetEntryAssembly()?.GetName().Name ?? "DesktopOpsApp";

    public string AppVersion { get; set; } = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";

    public string DiagnosticsDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopOps",
        "Diagnostics");

    public string LogFileName { get; set; } = "desktopops.log";

    public bool IncludeEnvironmentVariables { get; set; }

    public int RetainedExportCount { get; set; } = 10;
}
