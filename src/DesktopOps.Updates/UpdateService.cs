using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json;

namespace DesktopOps.Updates;

public sealed class UpdateService : IUpdateService
{
    public const string AgentProgramSlug = "_agent_";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private RegisteredClient? _registeredClient;

    public UpdateService(UpdateOptions options, HttpClient? httpClient = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        _httpClient = httpClient ?? new HttpClient();

        if (Options.ServerUri is not null)
        {
            _httpClient.BaseAddress = Options.ServerUri;
        }

        if (!string.IsNullOrWhiteSpace(Options.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Remove("X-DesktopOps-Key");
            _httpClient.DefaultRequestHeaders.Add("X-DesktopOps-Key", Options.ApiKey.Trim());
        }

        Directory.CreateDirectory(Options.CacheDirectory);
    }

    public UpdateOptions Options { get; }

    public async Task<RegisteredClient> RegisterClientAsync(CancellationToken cancellationToken = default)
    {
        EnsureServerConfigured();

        var programSlug = string.IsNullOrWhiteSpace(Options.ProgramSlug)
            ? AgentProgramSlug
            : Options.ProgramSlug;

        var response = await _httpClient.PostAsJsonAsync(
            "/api/clients/register",
            new
            {
                programSlug,
                userName = Options.UserName,
                machineName = Options.MachineName,
                currentVersion = Options.CurrentVersion.ToString(),
                windowsSid = Options.WindowsSid ?? TryGetCurrentWindowsSid()
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
        _registeredClient = await response.Content.ReadFromJsonAsync<RegisteredClient>(SerializerOptions, cancellationToken)
            ?? throw new InvalidOperationException("Server returned no registration payload.");

        return _registeredClient;
    }

    public async Task<IReadOnlyList<AssignedProgram>> GetAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        EnsureServerConfigured();
        var client = await EnsureRegisteredAsync(cancellationToken);
        var assignments = await _httpClient.GetFromJsonAsync<List<AssignedProgram>>(
            $"/api/clients/{client.Id}/assignments",
            SerializerOptions,
            cancellationToken) ?? [];

        return assignments;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        EnsureServerConfigured();
        EnsureProgramSlugConfigured();
        var client = await EnsureRegisteredAsync(cancellationToken);
        var updates = await _httpClient.GetFromJsonAsync<List<UpdateRelease>>(
            $"/api/clients/{client.Id}/updates?programSlug={Uri.EscapeDataString(Options.ProgramSlug)}",
            SerializerOptions,
            cancellationToken) ?? [];

        var availableReleases = updates
            .Where(release => ParseVersion(release.Version) is { } version && version > Options.CurrentVersion)
            .OrderByDescending(release => ParseVersion(release.Version))
            .ToList();

        if (availableReleases.Count == 0)
        {
            return UpdateCheckResult.NoUpdate(Options.CurrentVersion);
        }

        return new UpdateCheckResult
        {
            CurrentVersion = Options.CurrentVersion,
            AvailableReleases = availableReleases
        };
    }

    public Task<PreparedUpdatePackage> PrepareUpdateAsync(
        UpdateRelease release,
        CancellationToken cancellationToken = default)
    {
        return PrepareUpdateAsync(release, basePackagePath: null, installedVersion: null, cancellationToken);
    }

    public async Task<PreparedUpdatePackage> PrepareUpdateAsync(
        UpdateRelease release,
        string? basePackagePath,
        string? installedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var client = await EnsureRegisteredAsync(cancellationToken);
        await ReportStatusAsync(release.Id, DeploymentStatus.Downloading, "Download started.", cancellationToken: cancellationToken);

        var slug = string.IsNullOrWhiteSpace(Options.ProgramSlug) ? "package" : Options.ProgramSlug;
        var fileName = $"{SanitizeFileName(slug)}-{SanitizeFileName(release.Version)}.zip";
        var targetPath = Path.Combine(Options.CacheDirectory, fileName);

        var usedDelta = false;
        if (ShouldAttemptDelta(release, basePackagePath, installedVersion))
        {
            try
            {
                await DownloadAndApplyDeltaAsync(release, basePackagePath!, targetPath, cancellationToken);
                usedDelta = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                // Fall through to full package download.
            }
        }

        if (!usedDelta)
        {
            var packageUri = new Uri(release.PackageUrl, UriKind.RelativeOrAbsolute);
            if (!packageUri.IsAbsoluteUri)
            {
                packageUri = new Uri(_httpClient.BaseAddress!, packageUri);
            }

            await using (var sourceStream = await _httpClient.GetStreamAsync(packageUri, cancellationToken))
            await using (var targetStream = File.Create(targetPath))
            {
                await sourceStream.CopyToAsync(targetStream, cancellationToken);
            }
        }

        if (!usedDelta && !string.IsNullOrWhiteSpace(release.PackageHash))
        {
            var actualHash = ProgramInstallService.ComputeSha256(targetPath);
            if (!string.Equals(actualHash, release.PackageHash, StringComparison.OrdinalIgnoreCase))
            {
                await ReportStatusAsync(release.Id, DeploymentStatus.Failed, "Package hash mismatch.", cancellationToken: cancellationToken);
                throw new InvalidOperationException("Downloaded package hash does not match the server hash.");
            }
        }

        if (!usedDelta)
        {
            await VerifyCmsSignatureAsync(release, targetPath, cancellationToken);
        }

        await ReportStatusAsync(
            release.Id,
            DeploymentStatus.Available,
            usedDelta ? "Download completed (delta)." : "Download completed.",
            cancellationToken: cancellationToken);

        return new PreparedUpdatePackage
        {
            ClientId = client.Id,
            Release = release,
            PackagePath = targetPath,
            VerifiedViaDeltaEntries = usedDelta
        };
    }

    private bool ShouldAttemptDelta(UpdateRelease release, string? basePackagePath, string? installedVersion)
    {
        if (!Options.EnableDeltaUpdates)
        {
            return false;
        }

        // Reconstructed ZIPs are not byte-identical to the published artifact, so CMS over the
        // full package cannot be verified after a delta apply. Force the full download path.
        if (Options.RequirePackageCmsSignature)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(release.DeltaUrl)
            || string.IsNullOrWhiteSpace(release.DeltaHash)
            || string.IsNullOrWhiteSpace(release.DeltaBaseVersion)
            || string.IsNullOrWhiteSpace(basePackagePath)
            || string.IsNullOrWhiteSpace(installedVersion)
            || !File.Exists(basePackagePath))
        {
            return false;
        }

        if (!string.Equals(installedVersion.Trim(), release.DeltaBaseVersion.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (release.DeltaSize is { } deltaSize
            && release.PackageSize > 0
            && deltaSize >= release.PackageSize * Math.Clamp(Options.DeltaMaxSizeRatio, 0.05, 1.0))
        {
            return false;
        }

        return true;
    }

    private async Task DownloadAndApplyDeltaAsync(
        UpdateRelease release,
        string basePackagePath,
        string targetPackagePath,
        CancellationToken cancellationToken)
    {
        var deltaUri = new Uri(release.DeltaUrl!, UriKind.RelativeOrAbsolute);
        if (!deltaUri.IsAbsoluteUri)
        {
            deltaUri = new Uri(_httpClient.BaseAddress!, deltaUri);
        }

        var deltaPath = Path.Combine(
            Options.CacheDirectory,
            $"{SanitizeFileName(Path.GetFileNameWithoutExtension(targetPackagePath))}.delta.zip");

        await using (var sourceStream = await _httpClient.GetStreamAsync(deltaUri, cancellationToken))
        await using (var targetStream = File.Create(deltaPath))
        {
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
        }

        var actualDeltaHash = ProgramInstallService.ComputeSha256(deltaPath);
        if (!string.Equals(actualDeltaHash, release.DeltaHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Downloaded delta hash does not match the server hash.");
        }

        var manifest = PackageDeltaApplier.ApplyDelta(basePackagePath, deltaPath, targetPackagePath);
        if (!string.IsNullOrWhiteSpace(manifest.TargetPackageHash)
            && !string.IsNullOrWhiteSpace(release.PackageHash)
            && !string.Equals(manifest.TargetPackageHash, release.PackageHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Delta manifest target hash does not match the release package hash.");
        }

        try
        {
            File.Delete(deltaPath);
        }
        catch
        {
            // Best-effort cache cleanup.
        }
    }

    private async Task VerifyCmsSignatureAsync(
        UpdateRelease release,
        string packagePath,
        CancellationToken cancellationToken)
    {
        var hasSignatureUrl = !string.IsNullOrWhiteSpace(release.SignatureUrl);
        if (!hasSignatureUrl)
        {
            if (Options.RequirePackageCmsSignature)
            {
                await ReportStatusAsync(
                    release.Id,
                    DeploymentStatus.Failed,
                    "Package signature missing.",
                    cancellationToken: cancellationToken);
                throw new InvalidOperationException("Package CMS signature is required but was not provided by the server.");
            }

            return;
        }

        HashSet<string> trusted;
        try
        {
            trusted = PackageCmsVerifier.ParseTrustedThumbprints(Options.TrustedCmsThumbprints);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ReportStatusAsync(
                release.Id,
                DeploymentStatus.Failed,
                "Package signature verification failed.",
                cancellationToken: cancellationToken);
            throw new InvalidOperationException("Trusted CMS thumbprint configuration is invalid.", ex);
        }

        if (Options.RequirePackageCmsSignature && trusted.Count == 0)
        {
            await ReportStatusAsync(
                release.Id,
                DeploymentStatus.Failed,
                "Package signature verification failed.",
                cancellationToken: cancellationToken);
            throw new InvalidOperationException(
                "TrustedCmsThumbprints must be configured when RequirePackageCmsSignature is true.");
        }

        var signatureUri = new Uri(release.SignatureUrl!, UriKind.RelativeOrAbsolute);
        if (!signatureUri.IsAbsoluteUri)
        {
            signatureUri = new Uri(_httpClient.BaseAddress!, signatureUri);
        }

        var signaturePath = packagePath + ".p7s";
        await using (var sourceStream = await _httpClient.GetStreamAsync(signatureUri, cancellationToken))
        await using (var targetStream = File.Create(signaturePath))
        {
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
        }

        try
        {
            var signatureBytes = await File.ReadAllBytesAsync(signaturePath, cancellationToken);
            PackageCmsVerifier.VerifyDetachedSignature(packagePath, signatureBytes, trusted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ReportStatusAsync(
                release.Id,
                DeploymentStatus.Failed,
                "Package signature verification failed.",
                cancellationToken: cancellationToken);
            throw new InvalidOperationException("Downloaded package CMS signature verification failed.", ex);
        }
    }

    public async Task ReportStatusAsync(
        Guid releaseId,
        DeploymentStatus status,
        string? message = null,
        string? installedVersion = null,
        CancellationToken cancellationToken = default)
    {
        var client = await EnsureRegisteredAsync(cancellationToken);
        var response = await _httpClient.PostAsJsonAsync(
            $"/api/clients/{client.Id}/events",
            new
            {
                releaseId,
                status,
                message,
                installedVersion
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private async Task<RegisteredClient> EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        return _registeredClient ?? await RegisterClientAsync(cancellationToken);
    }

    private void EnsureServerConfigured()
    {
        if (Options.ServerUri is null)
        {
            throw new InvalidOperationException("ServerUri must be configured.");
        }
    }

    private void EnsureProgramSlugConfigured()
    {
        if (string.IsNullOrWhiteSpace(Options.ProgramSlug))
        {
            throw new InvalidOperationException("ProgramSlug must be configured.");
        }
    }

    private static Version? ParseVersion(string value)
    {
        return Version.TryParse(value, out var version)
            ? version
            : null;
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidChar, '-');
        }

        return value;
    }

    private static string? TryGetCurrentWindowsSid()
    {
        try
        {
            return WindowsIdentity.GetCurrent().User?.Value;
        }
        catch
        {
            return null;
        }
    }
}
