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

    public async Task<PreparedUpdatePackage> PrepareUpdateAsync(UpdateRelease release, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var client = await EnsureRegisteredAsync(cancellationToken);
        await ReportStatusAsync(release.Id, DeploymentStatus.Downloading, "Download started.", cancellationToken: cancellationToken);

        var packageUri = new Uri(release.PackageUrl, UriKind.RelativeOrAbsolute);
        if (!packageUri.IsAbsoluteUri)
        {
            packageUri = new Uri(_httpClient.BaseAddress!, packageUri);
        }

        var slug = string.IsNullOrWhiteSpace(Options.ProgramSlug) ? "package" : Options.ProgramSlug;
        var fileName = $"{SanitizeFileName(slug)}-{SanitizeFileName(release.Version)}.zip";
        var targetPath = Path.Combine(Options.CacheDirectory, fileName);

        await using (var sourceStream = await _httpClient.GetStreamAsync(packageUri, cancellationToken))
        await using (var targetStream = File.Create(targetPath))
        {
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(release.PackageHash))
        {
            var actualHash = ProgramInstallService.ComputeSha256(targetPath);
            if (!string.Equals(actualHash, release.PackageHash, StringComparison.OrdinalIgnoreCase))
            {
                await ReportStatusAsync(release.Id, DeploymentStatus.Failed, "Package hash mismatch.", cancellationToken: cancellationToken);
                throw new InvalidOperationException("Downloaded package hash does not match the server hash.");
            }
        }

        await ReportStatusAsync(release.Id, DeploymentStatus.Available, "Download completed.", cancellationToken: cancellationToken);

        return new PreparedUpdatePackage
        {
            ClientId = client.Id,
            Release = release,
            PackagePath = targetPath
        };
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
