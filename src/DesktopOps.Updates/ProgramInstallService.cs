using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopOps.Updates;

public sealed class ProgramInstallService
{
    public const string ManifestExtension = ".dops";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _installationDirectory;

    public ProgramInstallService(string installationDirectory)
    {
        _installationDirectory = installationDirectory;
        Directory.CreateDirectory(_installationDirectory);
    }

    public string InstallationDirectory => _installationDirectory;

    public IReadOnlyList<ProgramInstallationManifest> GetInstalledPrograms()
    {
        var results = new List<ProgramInstallationManifest>();
        foreach (var file in Directory.GetFiles(_installationDirectory, $"*{ManifestExtension}"))
        {
            var manifest = ReadManifest(file);
            if (manifest is not null)
            {
                results.Add(manifest);
            }
        }

        return results;
    }

    public List<ProgramUpdateCandidate> GetUpdateCandidates(IEnumerable<AssignedProgram> assignedPrograms)
    {
        var assigned = assignedPrograms.ToList();
        var installed = GetInstalledPrograms();
        var candidates = new List<ProgramUpdateCandidate>();

        foreach (var program in assigned)
        {
            if (program.LatestRelease is null)
            {
                continue;
            }

            var local = installed.FirstOrDefault(item =>
                string.Equals(item.Slug, program.Slug, StringComparison.OrdinalIgnoreCase));

            if (local is null)
            {
                candidates.Add(new ProgramUpdateCandidate
                {
                    Program = program,
                    Action = ProgramUpdateAction.Add
                });
            }
            else if (!string.Equals(local.Version, program.LatestRelease.Version, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(new ProgramUpdateCandidate
                {
                    Program = program,
                    Action = ProgramUpdateAction.Update,
                    InstalledVersion = local.Version
                });
            }
        }

        foreach (var local in installed)
        {
            if (assigned.Any(item => string.Equals(item.Slug, local.Slug, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            candidates.Add(new ProgramUpdateCandidate
            {
                Program = new AssignedProgram
                {
                    Name = local.Name,
                    Slug = local.Slug
                },
                Action = ProgramUpdateAction.Delete,
                InstalledVersion = local.Version
            });
        }

        return candidates;
    }

    public bool BackupProgram(string slug)
    {
        var manifest = GetInstalledPrograms()
            .FirstOrDefault(item => string.Equals(item.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (manifest is null)
        {
            return false;
        }

        var zipPath = Path.Combine(_installationDirectory, $"{slug}-{manifest.Version}-backup.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        using var stream = File.Create(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var relativeFile in manifest.Files)
        {
            var absolutePath = Path.Combine(_installationDirectory, relativeFile);
            if (!File.Exists(absolutePath))
            {
                continue;
            }

            archive.CreateEntryFromFile(absolutePath, relativeFile.Replace('\\', '/'));
        }

        foreach (var oldBackup in Directory.EnumerateFiles(_installationDirectory, $"{slug}-*-backup.zip"))
        {
            if (!string.Equals(oldBackup, zipPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(oldBackup);
            }
        }

        return true;
    }

    public bool RemoveProgram(string slug)
    {
        var manifestPath = Path.Combine(_installationDirectory, slug + ManifestExtension);
        var manifest = ReadManifest(manifestPath);
        if (manifest is null)
        {
            return false;
        }

        foreach (var relativeFile in manifest.Files)
        {
            var absolutePath = Path.Combine(_installationDirectory, relativeFile);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }

        File.Delete(manifestPath);

        foreach (var backup in Directory.EnumerateFiles(_installationDirectory, $"{slug}-*-backup.zip"))
        {
            File.Delete(backup);
        }

        return true;
    }

    public async Task<bool> InstallFromZipAsync(
        AssignedProgram program,
        string zipPath,
        string expectedHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program.LatestRelease);

        var actualHash = ComputeSha256(zipPath);
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var extractRoot = Path.Combine(_installationDirectory, "_extract", program.Slug);
        if (Directory.Exists(extractRoot))
        {
            Directory.Delete(extractRoot, recursive: true);
        }

        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

        var installedFiles = new List<string>();
        foreach (var file in Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(extractRoot, file);
            var target = Path.Combine(_installationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
            installedFiles.Add(relative);
        }

        Directory.Delete(extractRoot, recursive: true);
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var manifest = new ProgramInstallationManifest
        {
            Name = program.Name,
            Slug = program.Slug,
            Version = program.LatestRelease.Version,
            Files = installedFiles,
            PackageHash = expectedHash
        };

        var manifestPath = Path.Combine(_installationDirectory, program.Slug + ManifestExtension);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, SerializerOptions),
            cancellationToken);

        return true;
    }

    private static ProgramInstallationManifest? ReadManifest(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProgramInstallationManifest>(json, SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
