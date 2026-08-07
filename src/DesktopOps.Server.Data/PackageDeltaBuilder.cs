using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopOps.Server.Data;

/// <summary>Manifest embedded in a DesktopOps package delta ZIP.</summary>
public sealed class PackageDeltaManifest
{
    public const string FileName = "_desktopops-delta.json";

    public string BaseVersion { get; set; } = string.Empty;

    public string TargetVersion { get; set; } = string.Empty;

    public string TargetPackageHash { get; set; } = string.Empty;

    public List<string> Deletes { get; set; } = [];

    /// <summary>SHA-256 of every file entry in the target package (path → hex).</summary>
    public Dictionary<string, string> TargetEntryHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Builds zip-of-changed-files deltas between two full package ZIPs.</summary>
public static class PackageDeltaBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Creates a delta ZIP at <paramref name="deltaAbsolutePath"/> from <paramref name="basePackageAbsolutePath"/>
    /// to <paramref name="targetPackageAbsolutePath"/>. Returns hash and size of the delta.
    /// </summary>
    public static (string Sha256Hash, long SizeBytes) CreateDeltaZip(
        string basePackageAbsolutePath,
        string targetPackageAbsolutePath,
        string baseVersion,
        string targetVersion,
        string targetPackageHash,
        string deltaAbsolutePath)
    {
        if (!File.Exists(basePackageAbsolutePath))
        {
            throw new FileNotFoundException("Base package not found.", basePackageAbsolutePath);
        }

        if (!File.Exists(targetPackageAbsolutePath))
        {
            throw new FileNotFoundException("Target package not found.", targetPackageAbsolutePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(deltaAbsolutePath)!);
        if (File.Exists(deltaAbsolutePath))
        {
            File.Delete(deltaAbsolutePath);
        }

        var baseEntries = ReadEntryHashes(basePackageAbsolutePath);
        var targetEntries = ReadEntryHashes(targetPackageAbsolutePath);

        var deletes = baseEntries.Keys
            .Where(path => !targetEntries.ContainsKey(path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var changed = targetEntries
            .Where(pair =>
                !baseEntries.TryGetValue(pair.Key, out var baseHash)
                || !string.Equals(baseHash, pair.Value, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Key)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (deletes.Count == 0 && changed.Count == 0)
        {
            throw new InvalidOperationException("Packages are identical; no delta to create.");
        }

        var manifest = new PackageDeltaManifest
        {
            BaseVersion = baseVersion.Trim(),
            TargetVersion = targetVersion.Trim(),
            TargetPackageHash = targetPackageHash.Trim().ToLowerInvariant(),
            Deletes = deletes,
            TargetEntryHashes = targetEntries.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase)
        };

        using (var deltaStream = File.Create(deltaAbsolutePath))
        using (var deltaArchive = new ZipArchive(deltaStream, ZipArchiveMode.Create))
        using (var targetArchive = ZipFile.OpenRead(targetPackageAbsolutePath))
        {
            var targetByName = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in targetArchive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                targetByName[NormalizeEntryName(entry.FullName)] = entry;
            }

            var manifestEntry = deltaArchive.CreateEntry(PackageDeltaManifest.FileName, CompressionLevel.Optimal);
            using (var manifestStream = manifestEntry.Open())
            {
                JsonSerializer.Serialize(manifestStream, manifest, JsonOptions);
            }

            foreach (var relativePath in changed)
            {
                if (!targetByName.TryGetValue(relativePath, out var source))
                {
                    throw new InvalidOperationException($"Missing target entry: {relativePath}");
                }

                var dest = deltaArchive.CreateEntry(NormalizeEntryName(relativePath), CompressionLevel.Optimal);
                using var input = source.Open();
                using var output = dest.Open();
                input.CopyTo(output);
            }
        }

        var hash = PackageStorageService.ComputeFileHash(deltaAbsolutePath);
        var size = new FileInfo(deltaAbsolutePath).Length;
        return (hash, size);
    }

    private static Dictionary<string, string> ReadEntryHashes(string zipPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var name = NormalizeEntryName(entry.FullName);
            if (string.Equals(name, PackageDeltaManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = entry.Open();
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.AppendData(buffer.AsSpan(0, read));
            }

            map[name] = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        }

        return map;
    }

    private static string NormalizeEntryName(string name)
    {
        var normalized = name.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(name)
            || normalized.StartsWith('/'))
        {
            throw new InvalidOperationException($"Unsafe zip entry path: {name}");
        }

        return normalized;
    }
}
