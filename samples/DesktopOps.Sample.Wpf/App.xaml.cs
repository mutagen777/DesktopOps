using System.Windows;
using DesktopOps.Wpf;

namespace DesktopOps.Sample.Wpf;

public partial class App : Application
{
    public static DesktopOpsRuntime Runtime { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Runtime = DesktopOpsWpfBootstrapper.Initialize(this, options =>
        {
            options.Diagnostics.AppName = "DesktopOps Sample";
            options.Diagnostics.AppVersion = "0.1.0";
            options.Diagnostics.IncludeEnvironmentVariables = false;
            options.Updates.CurrentVersion = new Version(0, 1, 0);
            options.Updates.ProgramSlug = "desktopops-sample";
            options.Updates.ServerUri = new Uri("https://localhost:7022");
        });

        var window = new MainWindow();
        window.Show();
    }
}

