using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DesktopOps.Diagnostics;

public sealed class DiagnosticsService : IDiagnosticsService
{
    private readonly ConcurrentDictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentBag<DiagnosticsAttachment> _attachments = [];
    private readonly ConcurrentQueue<object> _capturedExceptions = new();

    public DiagnosticsService(DiagnosticsOptions? options = null)
    {
        Options = options ?? new DiagnosticsOptions();
        Directory.CreateDirectory(Options.DiagnosticsDirectory);
        CurrentLogFilePath = Path.Combine(Options.DiagnosticsDirectory, Options.LogFileName);

        AddMetadata("appName", Options.AppName);
        AddMetadata("appVersion", Options.AppVersion);
        AddMetadata("osVersion", Environment.OSVersion.VersionString);
        AddMetadata("machineName", Environment.MachineName);
        AddMetadata("userName", Environment.UserName);
        AddMetadata("startedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
    }

    public DiagnosticsOptions Options { get; }

    public string CurrentLogFilePath { get; }

    public ILoggerProvider CreateLoggerProvider() => new DesktopOpsLoggerProvider(CurrentLogFilePath);

    public void AddMetadata(string key, string value)
    {
        _metadata[key] = value;
    }

    public void CaptureException(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        _capturedExceptions.Enqueue(new
        {
            timestamp = DateTimeOffset.UtcNow,
            context,
            type = exception.GetType().FullName,
            message = exception.Message,
            stackTrace = exception.StackTrace,
            fullException = exception.ToString()
        });
    }

    public void RegisterAttachment(string sourcePath, string relativePath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        _attachments.Add(new DiagnosticsAttachment(sourcePath, relativePath));
    }

    public async Task<string> ExportDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Options.DiagnosticsDirectory);
        PruneOldExports();

        var fileName = $"{SanitizeFileName(Options.AppName)}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-diagnostics.zip";
        var exportPath = Path.Combine(Options.DiagnosticsDirectory, fileName);

        await using var stream = File.Create(exportPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);

        await WriteJsonEntryAsync(archive, "app-info.json", new
        {
            Options.AppName,
            Options.AppVersion,
            currentLogFile = CurrentLogFilePath
        }, cancellationToken);

        await WriteJsonEntryAsync(archive, "environment.json", BuildEnvironmentPayload(), cancellationToken);
        await WriteJsonEntryAsync(archive, "exceptions.json", _capturedExceptions.ToArray(), cancellationToken);

        if (File.Exists(CurrentLogFilePath))
        {
            archive.CreateEntryFromFile(CurrentLogFilePath, $"logs/{Path.GetFileName(CurrentLogFilePath)}");
        }

        foreach (var attachment in _attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            archive.CreateEntryFromFile(attachment.SourcePath, attachment.RelativePath);
        }

        return exportPath;
    }

    private object BuildEnvironmentPayload()
    {
        return new
        {
            metadata = _metadata.OrderBy(pair => pair.Key).ToDictionary(),
            environmentVariables = Options.IncludeEnvironmentVariables
                ? Environment.GetEnvironmentVariables()
                    .Keys
                    .Cast<string>()
                    .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(static key => key, static key => Environment.GetEnvironmentVariable(key))
                : null
        };
    }

    private void PruneOldExports()
    {
        var exports = new DirectoryInfo(Options.DiagnosticsDirectory)
            .EnumerateFiles("*-diagnostics.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(static file => file.CreationTimeUtc)
            .Skip(Options.RetainedExportCount - 1)
            .ToList();

        foreach (var file in exports)
        {
            file.Delete();
        }
    }

    private static async Task WriteJsonEntryAsync(
        ZipArchive archive,
        string entryName,
        object payload,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, payload, cancellationToken: cancellationToken, options: new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '-');
        }

        return value;
    }
}
