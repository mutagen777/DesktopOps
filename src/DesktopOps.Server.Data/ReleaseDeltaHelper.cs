namespace DesktopOps.Server.Data;

/// <summary>Chooses a previous full package and builds an optional delta for a new release.</summary>
public static class ReleaseDeltaHelper
{
    /// <summary>Default: keep delta only when smaller than this fraction of the full package.</summary>
    public const double DefaultMaxSizeRatio = 0.8;

    /// <summary>
    /// Picks the highest-versioned <strong>published</strong> package of the same program that is
    /// strictly older than <paramref name="targetVersion"/>. Drafts are ignored so
    /// <c>DeltaBaseVersion</c> matches versions clients can actually have installed.
    /// </summary>
    public static ReleasePackage? FindPreviousRelease(
        IEnumerable<ReleasePackage> sameProgramReleases,
        string targetVersion)
    {
        var published = sameProgramReleases
            .Where(static release => release.PublishedAtUtc.HasValue)
            .ToList();
        if (published.Count == 0)
        {
            return null;
        }

        if (!Version.TryParse(targetVersion.Trim(), out var target))
        {
            return published
                .OrderByDescending(static item => item.CreatedAtUtc)
                .FirstOrDefault();
        }

        return published
            .Select(release =>
            {
                var ok = Version.TryParse(release.Version, out var version);
                return (Release: release, Ok: ok, Version: version);
            })
            .Where(pair => pair.Ok && pair.Version! < target)
            .OrderByDescending(static pair => pair.Version)
            .ThenByDescending(static pair => pair.Release.CreatedAtUtc)
            .Select(static pair => pair.Release)
            .FirstOrDefault();
    }

    /// <summary>
    /// Builds a delta against <paramref name="baseRelease"/> when beneficial.
    /// Updates <paramref name="target"/> fields only on success; returns false when skipped or failed.
    /// </summary>
    public static bool TryAttachDelta(
        PackageStorageService storage,
        ReleasePackage target,
        ReleasePackage baseRelease,
        double maxSizeRatio = DefaultMaxSizeRatio)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(baseRelease);

        try
        {
            var (relativePath, hash, size) = storage.CreateDeltaFromPackages(
                baseRelease.PackagePath,
                target.PackagePath,
                baseRelease.Version,
                target.Version,
                target.PackageHash);

            if (target.PackageSize > 0 && size >= target.PackageSize * Math.Clamp(maxSizeRatio, 0.05, 1.0))
            {
                TryDeleteDeltaOnly(storage, relativePath);
                return false;
            }

            target.DeltaPath = relativePath;
            target.DeltaHash = hash;
            target.DeltaSize = size;
            target.DeltaBaseVersion = baseRelease.Version;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Best-effort delete of a delta artifact without touching the full package.</summary>
    public static void TryDeleteDeltaOnly(PackageStorageService storage, string? deltaRelativePath)
    {
        if (string.IsNullOrWhiteSpace(deltaRelativePath))
        {
            return;
        }

        try
        {
            var absolute = storage.GetAbsolutePath(deltaRelativePath);
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }
        }
        catch
        {
            // Best-effort.
        }
    }
}
