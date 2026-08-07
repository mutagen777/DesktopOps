using System.IO.Compression;
using System.Text.Json;

namespace DesktopOps.Updates;

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

/// <summary>Applies a zip-of-changed-files delta onto a base full ZIP to produce a target full ZIP.</summary>
public static class PackageDeltaApplier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Reconstructs the target package at <paramref name="outputPackageAbsolutePath"/> from
    /// <paramref name="basePackageAbsolutePath"/> and <paramref name="deltaAbsolutePath"/>.
    /// </summary>
    public static PackageDeltaManifest ApplyDelta(
        string basePackageAbsolutePath,
        string deltaAbsolutePath,
        string outputPackageAbsolutePath)
    {
        if (!File.Exists(basePackageAbsolutePath))
        {
            throw new FileNotFoundException("Base package not found.", basePackageAbsolutePath);
        }

        if (!File.Exists(deltaAbsolutePath))
        {
            throw new FileNotFoundException("Delta package not found.", deltaAbsolutePath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPackageAbsolutePath)!);
        if (File.Exists(outputPackageAbsolutePath))
        {
            File.Delete(outputPackageAbsolutePath);
        }

        using var deltaArchive = ZipFile.OpenRead(deltaAbsolutePath);
        var manifestEntry = deltaArchive.GetEntry(PackageDeltaManifest.FileName)
            ?? throw new InvalidOperationException("Delta package is missing _desktopops-delta.json.");

        PackageDeltaManifest manifest;
        using (var manifestStream = manifestEntry.Open())
        {
            manifest = JsonSerializer.Deserialize<PackageDeltaManifest>(manifestStream, JsonOptions)
                ?? throw new InvalidOperationException("Delta manifest is invalid.");
        }

        var deletes = new HashSet<string>(
            manifest.Deletes.Select(NormalizeEntryName),
            StringComparer.OrdinalIgnoreCase);

        var replacements = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in deltaArchive.Entries)
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

            replacements[name] = entry;
        }

        using (var baseArchive = ZipFile.OpenRead(basePackageAbsolutePath))
        using (var outputStream = File.Create(outputPackageAbsolutePath))
        using (var outputArchive = new ZipArchive(outputStream, ZipArchiveMode.Create))
        {
            foreach (var entry in baseArchive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                var name = NormalizeEntryName(entry.FullName);
                if (deletes.Contains(name) || replacements.ContainsKey(name))
                {
                    continue;
                }

                CopyEntry(entry, outputArchive, name);
            }

            foreach (var (name, entry) in replacements.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                CopyEntry(entry, outputArchive, name);
            }
        }

        VerifyReconstructedEntries(outputPackageAbsolutePath, manifest);
        return manifest;
    }

    private static void VerifyReconstructedEntries(string packagePath, PackageDeltaManifest manifest)
    {
        if (manifest.TargetEntryHashes.Count == 0)
        {
            throw new InvalidOperationException("Delta manifest is missing target entry hashes.");
        }

        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                var name = NormalizeEntryName(entry.FullName);
                using var stream = entry.Open();
                using var hasher = System.Security.Cryptography.IncrementalHash.CreateHash(
                    System.Security.Cryptography.HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.AppendData(buffer.AsSpan(0, read));
                }

                actual[name] = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            }
        }

        if (actual.Count != manifest.TargetEntryHashes.Count)
        {
            throw new InvalidOperationException(
                $"Reconstructed package entry count mismatch ({actual.Count} vs {manifest.TargetEntryHashes.Count}).");
        }

        foreach (var (path, expectedHash) in manifest.TargetEntryHashes)
        {
            if (!actual.TryGetValue(path, out var actualHash)
                || !string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Reconstructed package entry hash mismatch: {path}");
            }
        }
    }

    private static void CopyEntry(ZipArchiveEntry source, ZipArchive destination, string entryName)
    {
        var dest = destination.CreateEntry(entryName, CompressionLevel.Optimal);
        using var input = source.Open();
        using var output = dest.Open();
        input.CopyTo(output);
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
