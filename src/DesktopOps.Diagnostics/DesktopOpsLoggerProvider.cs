using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DesktopOps.Diagnostics;

internal sealed class DesktopOpsLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly object _syncRoot = new();
    private bool _disposed;

    public DesktopOpsLoggerProvider(string logFilePath)
    {
        _logFilePath = logFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
    }

    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new DesktopOpsLogger(categoryName, WriteRecord);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void WriteRecord(DiagnosticRecord record)
    {
        var json = JsonSerializer.Serialize(record);

        lock (_syncRoot)
        {
            File.AppendAllText(_logFilePath, json + Environment.NewLine);
        }
    }

    private sealed class DesktopOpsLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly Action<DiagnosticRecord> _writeRecord;

        public DesktopOpsLogger(string categoryName, Action<DiagnosticRecord> writeRecord)
        {
            _categoryName = categoryName;
            _writeRecord = writeRecord;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var record = new DiagnosticRecord(
                DateTimeOffset.UtcNow,
                logLevel.ToString(),
                _categoryName,
                eventId.Id == 0 ? null : eventId.Id,
                formatter(state, exception),
                exception?.ToString());

            _writeRecord(record);
        }
    }
}
