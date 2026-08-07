using DesktopOps.Licensing;
using Xunit;

namespace DesktopOps.Licensing.Tests;

public sealed class LicenseEvaluatorTests
{
    private static readonly DateTimeOffset NoonUtc =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Community_default_soft_overage()
    {
        var eval = LicenseEvaluation.Community(usedSeats: 5);
        Assert.Equal(LicenseTiers.Community, eval.Tier);
        Assert.Equal(3, eval.MaxSeats);
        Assert.True(eval.IsOverSeatLimit);
        Assert.False(eval.BlocksMutations);
    }

    [Fact]
    public void Null_claims_is_community()
    {
        var eval = LicenseEvaluator.Evaluate(null, usedSeats: 1, hasStoredBlob: false, verificationError: null, NoonUtc);
        Assert.True(eval.IsCommunity);
        Assert.Equal(3, eval.MaxSeats);
        Assert.False(eval.BlocksMutations);
    }

    [Fact]
    public void Invalid_stored_blob_hard_blocks()
    {
        var eval = LicenseEvaluator.Evaluate(
            claims: null,
            usedSeats: 1,
            hasStoredBlob: true,
            verificationError: "bad sig",
            utcNow: NoonUtc);

        Assert.True(eval.BlocksMutations);
        Assert.False(eval.SignatureValid);
        Assert.Equal("bad sig", eval.ErrorMessage);
    }

    [Fact]
    public void Team_license_within_limit_allows_mutations()
    {
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Team,
            MaxSeats = 25,
            Customer = "Contoso",
            ValidUntilUtc = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var eval = LicenseEvaluator.Evaluate(claims, usedSeats: 10, hasStoredBlob: true, null, NoonUtc);
        Assert.False(eval.BlocksMutations);
        Assert.False(eval.IsOverSeatLimit);
        Assert.Equal(25, eval.MaxSeats);
    }

    [Fact]
    public void Team_overage_is_soft_when_not_expired()
    {
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Team,
            MaxSeats = 5,
            Customer = "Contoso",
            ValidUntilUtc = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        var eval = LicenseEvaluator.Evaluate(claims, usedSeats: 9, hasStoredBlob: true, null, NoonUtc);
        Assert.True(eval.IsOverSeatLimit);
        Assert.False(eval.BlocksMutations);
    }

    [Fact]
    public void Expired_team_midnight_date_valid_through_end_of_day()
    {
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Team,
            MaxSeats = 25,
            Customer = "Contoso",
            ValidUntilUtc = new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero)
        };

        var duringDay = new DateTimeOffset(2026, 6, 15, 23, 59, 59, TimeSpan.Zero);
        var afterDay = new DateTimeOffset(2026, 6, 16, 0, 0, 0, TimeSpan.Zero);

        var ok = LicenseEvaluator.Evaluate(claims, 1, true, null, duringDay);
        Assert.False(ok.IsExpired);
        Assert.False(ok.BlocksMutations);

        var blocked = LicenseEvaluator.Evaluate(claims, 1, true, null, afterDay);
        Assert.True(blocked.IsExpired);
        Assert.True(blocked.BlocksMutations);
    }

    [Fact]
    public void Explicit_expiry_time_is_exact()
    {
        var until = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Assert.False(LicenseEvaluator.IsExpired(until, until.AddSeconds(-1)));
        Assert.True(LicenseEvaluator.IsExpired(until, until));
        Assert.True(LicenseEvaluator.IsExpired(until, until.AddSeconds(1)));
    }

    [Fact]
    public void Enterprise_unlimited_never_over_limit()
    {
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Enterprise,
            MaxSeats = null,
            Customer = "Acme"
        };

        var eval = LicenseEvaluator.Evaluate(claims, usedSeats: 10_000, hasStoredBlob: true, null, NoonUtc);
        Assert.Null(eval.MaxSeats);
        Assert.False(eval.IsOverSeatLimit);
        Assert.False(eval.BlocksMutations);
    }
}
