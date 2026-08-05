using System.Windows;
using DesktopOps.Diagnostics;
using DesktopOps.Updates;
using Microsoft.Extensions.Logging;

namespace DesktopOps.Wpf;

public static class DesktopOpsWpfBootstrapper
{
    public static DesktopOpsRuntime Initialize(
        Application application,
        Action<DesktopOpsWpfOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(application);

        var options = new DesktopOpsWpfOptions();
        configure?.Invoke(options);

        var diagnosticsService = new DiagnosticsService(options.Diagnostics);
        var loggerProvider = diagnosticsService.CreateLoggerProvider();
        var logger = loggerProvider.CreateLogger("DesktopOps.Wpf");
        var updateService = new UpdateService(options.Updates);

        application.DispatcherUnhandledException += (_, args) =>
        {
            diagnosticsService.CaptureException(args.Exception, "DispatcherUnhandledException");
            logger.LogError(args.Exception, "Unhandled dispatcher exception.");
            options.OnExceptionCaptured?.Invoke(args.Exception, "DispatcherUnhandledException");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                diagnosticsService.CaptureException(exception, "CurrentDomainUnhandledException");
                logger.LogError(exception, "Unhandled AppDomain exception.");
                options.OnExceptionCaptured?.Invoke(exception, "CurrentDomainUnhandledException");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            diagnosticsService.CaptureException(args.Exception, "UnobservedTaskException");
            logger.LogError(args.Exception, "Unobserved task exception.");
            options.OnExceptionCaptured?.Invoke(args.Exception, "UnobservedTaskException");
        };

        logger.LogInformation(
            "DesktopOps started for {AppName} {AppVersion}.",
            options.Diagnostics.AppName,
            options.Diagnostics.AppVersion);

        return new DesktopOpsRuntime(diagnosticsService, updateService);
    }
}
