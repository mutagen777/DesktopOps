namespace DesktopOps.Licensing;

/// <summary>Effective license + seat usage for Admin/Server gates.</summary>
public sealed class LicenseEvaluation
{
    public required string Tier { get; init; }

    public string Customer { get; init; } = string.Empty;

    public Guid? LicenseId { get; init; }

    public DateTimeOffset? ValidUntilUtc { get; init; }

    /// <summary>Null means unlimited seats.</summary>
    public int? MaxSeats { get; init; }

    public int UsedSeats { get; init; }

    public bool HasInstalledLicense { get; init; }

    public bool SignatureValid { get; init; }

    public bool IsExpired { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsCommunity =>
        string.Equals(Tier, LicenseTiers.Community, StringComparison.OrdinalIgnoreCase);

    public bool IsOverSeatLimit =>
        MaxSeats is int max && max >= 0 && UsedSeats > max;

    /// <summary>
    /// When true, block new assignments and publish (invalid/expired paid license).
    /// Community overage stays soft and does not set this.
    /// </summary>
    public bool BlocksMutations { get; init; }

    public static LicenseEvaluation Community(int usedSeats) => new()
    {
        Tier = LicenseTiers.Community,
        MaxSeats = LicenseDefaults.CommunityMaxSeats,
        UsedSeats = usedSeats,
        HasInstalledLicense = false,
        SignatureValid = true,
        IsExpired = false,
        BlocksMutations = false
    };
}

/// <summary>Builds a <see cref="LicenseEvaluation"/> from optional verified claims and seat usage.</summary>
public static class LicenseEvaluator
{
    public static LicenseEvaluation Evaluate(
        LicenseClaims? claims,
        int usedSeats,
        bool hasStoredBlob,
        string? verificationError,
        DateTimeOffset? utcNow = null)
    {
        var now = utcNow ?? DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(verificationError))
        {
            return new LicenseEvaluation
            {
                Tier = LicenseTiers.Community,
                MaxSeats = LicenseDefaults.CommunityMaxSeats,
                UsedSeats = usedSeats,
                HasInstalledLicense = hasStoredBlob,
                SignatureValid = false,
                IsExpired = false,
                ErrorMessage = verificationError,
                BlocksMutations = hasStoredBlob
            };
        }

        if (claims is null)
        {
            return LicenseEvaluation.Community(usedSeats);
        }

        LicenseCrypto.NormalizeClaims(claims);
        var expired = claims.ValidUntilUtc is { } until && IsExpired(until, now);
        var isCommunity = string.Equals(claims.Tier, LicenseTiers.Community, StringComparison.OrdinalIgnoreCase);
        var maxSeats = LicenseCrypto.IsUnlimited(claims.MaxSeats)
            ? null
            : claims.MaxSeats ?? (isCommunity ? LicenseDefaults.CommunityMaxSeats : claims.MaxSeats);

        if (isCommunity && maxSeats is null)
        {
            maxSeats = LicenseDefaults.CommunityMaxSeats;
        }

        // Paid tiers that are expired block mutations; community stays soft.
        var blocks = !isCommunity && expired;

        return new LicenseEvaluation
        {
            Tier = claims.Tier,
            Customer = claims.Customer,
            LicenseId = claims.LicenseId == Guid.Empty ? null : claims.LicenseId,
            ValidUntilUtc = claims.ValidUntilUtc,
            MaxSeats = maxSeats,
            UsedSeats = usedSeats,
            HasInstalledLicense = true,
            SignatureValid = true,
            IsExpired = expired,
            ErrorMessage = expired ? "License has expired." : null,
            BlocksMutations = blocks
        };
    }

    /// <summary>
    /// Midnight UTC dates are treated as inclusive end-of-day (valid through that calendar day).
    /// Explicit times expire at the exact instant.
    /// </summary>
    public static bool IsExpired(DateTimeOffset validUntilUtc, DateTimeOffset utcNow)
    {
        var deadline = validUntilUtc.TimeOfDay == TimeSpan.Zero
            ? validUntilUtc.AddDays(1)
            : validUntilUtc;
        return utcNow >= deadline;
    }
}
