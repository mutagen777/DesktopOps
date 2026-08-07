using System.Security.Cryptography;

namespace DesktopOps.Server.Data;

public sealed class PackageStorageService
{
    private readonly StorageOptions _options;

    public PackageStorageService(StorageOptions options)
    {
        _options = options;
        Directory.CreateDirectory(_options.RootPath);
    }

    public string GetAbsolutePath(string relativePath)
    {
        return Path.Combine(_options.RootPath, relativePath);
    }

    public async Task<StoredPackage> SavePackageAsync(
        string programSlug,
        string version,
        string originalFileName,
        Stream packageStream,
        CancellationToken cancellationToken = default)
    {
        var safeProgramSlug = Sanitize(programSlug);
        var safeVersion = Sanitize(version);
        var extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".zip";
        }

        var folder = Path.Combine(_options.RootPath, safeProgramSlug);
        Directory.CreateDirectory(folder);

        var fileName = $"{safeVersion}{extension}";
        var absolutePath = Path.Combine(folder, fileName);

        await using var output = File.Create(absolutePath);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int read;
        while ((read = await packageStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hasher.AppendData(buffer.AsSpan(0, read));
        }

        await output.FlushAsync(cancellationToken);
        var hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        var size = output.Length;

        return new StoredPackage(
            Path.Combine(safeProgramSlug, fileName),
            hash,
            size);
    }

    /// <summary>Relative detached signature path for a package (<c>{packagePath}.p7s</c>).</summary>
    public static string GetDetachedSignatureRelativePath(string packageRelativePath)
    {
        return packageRelativePath + ".p7s";
    }

    /// <summary>Writes an uploaded detached CMS signature next to the package.</summary>
    public async Task<string> SaveDetachedSignatureAsync(
        string packageRelativePath,
        Stream signatureStream,
        CancellationToken cancellationToken = default)
    {
        var relativeSignaturePath = GetDetachedSignatureRelativePath(packageRelativePath);
        var absolutePath = GetAbsolutePath(relativeSignaturePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using var output = File.Create(absolutePath);
        await signatureStream.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        return relativeSignaturePath;
    }

    /// <summary>
    /// Saves and verifies a detached CMS signature over the package.
    /// When <paramref name="requiredSignerThumbprint"/> is set, the signer must match it.
    /// </summary>
    public async Task<string> SaveAndVerifyDetachedSignatureAsync(
        string packageRelativePath,
        Stream signatureStream,
        string? requiredSignerThumbprint = null,
        CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await signatureStream.CopyToAsync(buffer, cancellationToken);
        var signatureBytes = buffer.ToArray();
        if (signatureBytes.Length == 0)
        {
            throw new InvalidOperationException("Package signature is empty.");
        }

        var packageAbsolutePath = GetAbsolutePath(packageRelativePath);
        IReadOnlyCollection<string>? trusted = null;
        if (!string.IsNullOrWhiteSpace(requiredSignerThumbprint))
        {
            trusted = [PackageCmsSigner.NormalizeThumbprint(requiredSignerThumbprint)];
        }

        PackageCmsVerifier.VerifyDetachedSignature(packageAbsolutePath, signatureBytes, trusted);

        var relativeSignaturePath = GetDetachedSignatureRelativePath(packageRelativePath);
        var absoluteSignaturePath = GetAbsolutePath(relativeSignaturePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteSignaturePath)!);
        await File.WriteAllBytesAsync(absoluteSignaturePath, signatureBytes, cancellationToken);
        return relativeSignaturePath;
    }

    /// <summary>Deletes the package and its detached signature if present (best-effort).</summary>
    public void TryDeletePackageArtifacts(string packageRelativePath, string? deltaRelativePath = null)
    {
        TryDeleteFile(GetAbsolutePath(packageRelativePath));
        TryDeleteFile(GetAbsolutePath(GetDetachedSignatureRelativePath(packageRelativePath)));
        if (!string.IsNullOrWhiteSpace(deltaRelativePath))
        {
            TryDeleteFile(GetAbsolutePath(deltaRelativePath));
        }
    }

    /// <summary>Relative delta path: <c>{slug}/{version}.from-{baseVersion}.delta.zip</c>.</summary>
    public static string GetDeltaRelativePath(string packageRelativePath, string baseVersion)
    {
        var directory = Path.GetDirectoryName(packageRelativePath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(packageRelativePath);
        var safeBase = Sanitize(baseVersion);
        var deltaName = $"{fileName}.from-{safeBase}.delta.zip";
        return string.IsNullOrEmpty(directory)
            ? deltaName
            : Path.Combine(directory, deltaName);
    }

    /// <summary>Builds a delta ZIP from an existing base package to this target package.</summary>
    public (string RelativePath, string Sha256Hash, long SizeBytes) CreateDeltaFromPackages(
        string basePackageRelativePath,
        string targetPackageRelativePath,
        string baseVersion,
        string targetVersion,
        string targetPackageHash)
    {
        var relativeDelta = GetDeltaRelativePath(targetPackageRelativePath, baseVersion);
        var absoluteDelta = GetAbsolutePath(relativeDelta);
        var (hash, size) = PackageDeltaBuilder.CreateDeltaZip(
            GetAbsolutePath(basePackageRelativePath),
            GetAbsolutePath(targetPackageRelativePath),
            baseVersion,
            targetVersion,
            targetPackageHash,
            absoluteDelta);
        return (relativeDelta, hash, size);
    }

    /// <summary>Signs the package on disk and returns the relative <c>.p7s</c> path.</summary>
    public string SignPackage(string packageRelativePath, string certificateThumbprint)
    {
        var absolutePackagePath = GetAbsolutePath(packageRelativePath);
        PackageCmsSigner.SignPackageFile(absolutePackagePath, certificateThumbprint);
        return GetDetachedSignatureRelativePath(packageRelativePath);
    }

    public static string ComputeFileHash(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string absolutePath)
    {
        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch
        {
            // Best-effort cleanup of orphaned upload artifacts.
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '-');
        }

        return value;
    }
}

public sealed record StoredPackage(string RelativePath, string Sha256Hash, long SizeBytes);
