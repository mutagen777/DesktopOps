namespace DesktopOps.Licensing;

/// <summary>Well-known license tiers.</summary>
public static class LicenseTiers
{
    public const string Community = "community";

    public const string Team = "team";

    public const string Enterprise = "enterprise";
}

/// <summary>Claims inside a signed DesktopOps license file.</summary>
public sealed class LicenseClaims
{
    /// <summary>Stable license id. Default empty — callers/issuers must set explicitly before signing.</summary>
    public Guid LicenseId { get; set; }

    public string Tier { get; set; } = LicenseTiers.Community;

    /// <summary>Maximum unique seats. Null or negative means unlimited. No default — omit in JSON for unlimited.</summary>
    public int? MaxSeats { get; set; }

    public string Customer { get; set; } = string.Empty;

    /// <summary>Optional expiry (UTC date). Null means perpetual.</summary>
    public DateTimeOffset? ValidUntilUtc { get; set; }
}

/// <summary>Default Community entitlements when no paid license is installed.</summary>
public static class LicenseDefaults
{
    public const int CommunityMaxSeats = 3;
}

/// <summary>Envelope written to disk / stored in the database.</summary>
public sealed class SignedLicenseDocument
{
    public LicenseClaims Payload { get; set; } = new();

    /// <summary>Base64 RSA-SHA256 signature over the canonical JSON of <see cref="Payload"/>.</summary>
    public string Signature { get; set; } = string.Empty;
}
