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

    public static string ComputeFileHash(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
