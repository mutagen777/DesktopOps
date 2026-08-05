using System.Security.Cryptography;
using System.Text;

namespace DesktopOps.Server.Data;

/// <summary>Deterministic staged-rollout membership for a user and release.</summary>
public static class RolloutEligibility
{
    /// <summary>
    /// Returns whether <paramref name="userName"/> is included in the rollout bucket
    /// for <paramref name="releaseId"/> given <paramref name="rolloutPercent"/> (0–100).
    /// </summary>
    public static bool IsIncluded(string userName, Guid releaseId, int rolloutPercent)
    {
        var percent = Math.Clamp(rolloutPercent, 0, 100);
        if (percent >= 100)
        {
            return true;
        }

        if (percent <= 0 || string.IsNullOrWhiteSpace(userName))
        {
            return false;
        }

        var material = $"{userName.Trim().ToLowerInvariant()}|{releaseId:N}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bucket = BinaryPrimitivesReadUInt32(hash) % 100;
        return bucket < (uint)percent;
    }

    private static uint BinaryPrimitivesReadUInt32(byte[] hash) =>
        (uint)(hash[0] | (hash[1] << 8) | (hash[2] << 16) | (hash[3] << 24));
}
