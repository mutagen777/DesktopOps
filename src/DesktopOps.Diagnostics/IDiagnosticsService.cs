using Microsoft.Extensions.Logging;

namespace DesktopOps.Diagnostics;

public interface IDiagnosticsService
{
    DiagnosticsOptions Options { get; }

    string CurrentLogFilePath { get; }

    ILoggerProvider CreateLoggerProvider();

    void AddMetadata(string key, string value);

    void CaptureException(Exception exception, string context);

    void RegisterAttachment(string sourcePath, string relativePath);

    Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default);
}
