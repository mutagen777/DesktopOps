using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using DesktopOps.Updates;
using Microsoft.Extensions.Logging;

namespace DesktopOps.Agent.Services;

/// <summary>Applies updates to the running agent process via a staged copy-and-restart script.</summary>
public sealed class AgentSelfUpdateService
{
    private readonly UpdateService _updateService;
    private readonly ILogger<AgentSelfUpdateService> _logger;

    public AgentSelfUpdateService(UpdateService updateService, ILogger<AgentSelfUpdateService> logger)
    {
        _updateService = updateService;
        _logger = logger;
    }

    public static bool IsAgentProgram(string? slug) =>
        string.Equals(slug, UpdateService.AgentProgramSlug, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns true when a restart was scheduled and the process should shut down.
    /// </summary>
    public async Task<bool> TryScheduleSelfUpdateAsync(
        AssignedProgram program,
        CancellationToken cancellationToken = default)
    {
        if (program.LatestRelease is null)
        {
            return false;
        }

        var targetDirectory = ResolveAgentInstallDirectory();
        if (targetDirectory is null)
        {
            _logger.LogWarning(
                "Agent self-update skipped: process is not a published DesktopOps.Agent.exe (e.g. dotnet run).");
            return false;
        }

        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopOps",
            "AgentUpdate");

        if (Directory.Exists(stagingRoot))
        {
            Directory.Delete(stagingRoot, recursive: true);
        }

        Directory.CreateDirectory(stagingRoot);

        var prepared = await _updateService.PrepareUpdateAsync(program.LatestRelease, cancellationToken);
        var actualHash = ProgramInstallService.ComputeSha256(prepared.PackagePath);
        if (!string.Equals(actualHash, program.LatestRelease.PackageHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Agent package hash mismatch.");
            await _updateService.ReportStatusAsync(
                program.LatestRelease.Id,
                DeploymentStatus.Failed,
                "Agent self-update hash verification failed.",
                cancellationToken: cancellationToken);
            return false;
        }

        var extractRoot = Path.Combine(stagingRoot, "extract");
        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(prepared.PackagePath, extractRoot, overwriteFiles: true);

        var exeName = Path.GetFileName(Environment.ProcessPath!)!;
        var applyScript = Path.Combine(stagingRoot, "apply-update.cmd");
        var script = $"""
            @echo off
            setlocal
            rem Wait for the current agent process to exit
            timeout /t 2 /nobreak >nul
            xcopy /E /Y /I "{extractRoot}\*" "{targetDirectory}\" >nul
            if errorlevel 1 (
              echo DesktopOps agent self-update copy failed.
              exit /b 1
            )
            start "" "{Path.Combine(targetDirectory, exeName)}"
            endlocal
            """;

        await File.WriteAllTextAsync(applyScript, script, cancellationToken);

        await _updateService.ReportStatusAsync(
            program.LatestRelease.Id,
            DeploymentStatus.Installed,
            $"Agent self-update {program.LatestRelease.Version} staged; restarting.",
            program.LatestRelease.Version,
            cancellationToken);

        _logger.LogInformation(
            "Scheduling agent self-update to {Version} into {Target}",
            program.LatestRelease.Version,
            targetDirectory);

        Process.Start(new ProcessStartInfo
        {
            FileName = applyScript,
            UseShellExecute = true,
            WorkingDirectory = stagingRoot,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        return true;
    }

    private static string? ResolveAgentInstallDirectory()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var fileName = Path.GetFileName(processPath);
        if (fileName.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!fileName.Contains("DesktopOps.Agent", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains("Agent", StringComparison.OrdinalIgnoreCase))
        {
            // Still allow replacement of whatever published host EXE we are
            // as long as it is not the SDK host.
        }

        return Path.GetDirectoryName(processPath);
    }
}
