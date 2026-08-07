using System.Security.Cryptography;
using System.Text.Json;
using DesktopOps.Licensing;
using Xunit;

namespace DesktopOps.Licensing.Tests;

public sealed class LicenseCryptoTests
{
    private static string CreatePrivatePem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static string PublicFromPrivate(string privatePem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privatePem);
        return rsa.ExportRSAPublicKeyPem();
    }

    [Fact]
    public void Sign_and_verify_round_trip_with_max_seats()
    {
        var priv = CreatePrivatePem();
        var pub = PublicFromPrivate(priv);
        var claims = new LicenseClaims
        {
            LicenseId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Tier = LicenseTiers.Team,
            MaxSeats = 25,
            Customer = "Contoso",
            ValidUntilUtc = new DateTimeOffset(2027, 12, 31, 0, 0, 0, TimeSpan.Zero)
        };

        var doc = LicenseCrypto.Sign(claims, priv);
        var json = LicenseCrypto.SerializeDocument(doc);
        var verified = LicenseCrypto.VerifyDocument(json, pub);

        Assert.Equal(LicenseTiers.Team, verified.Tier);
        Assert.Equal(25, verified.MaxSeats);
        Assert.Equal("Contoso", verified.Customer);
        Assert.Equal(claims.LicenseId, verified.LicenseId);
    }

    [Fact]
    public void Sign_and_verify_unlimited_null_max_seats()
    {
        var priv = CreatePrivatePem();
        var pub = PublicFromPrivate(priv);
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Enterprise,
            MaxSeats = null,
            Customer = "Acme"
        };

        var json = LicenseCrypto.SerializeDocument(LicenseCrypto.Sign(claims, priv));
        Assert.DoesNotContain("maxSeats", json, StringComparison.OrdinalIgnoreCase);

        var verified = LicenseCrypto.VerifyDocument(json, pub);
        Assert.Null(verified.MaxSeats);
        Assert.True(LicenseCrypto.IsUnlimited(verified.MaxSeats));
    }

    [Fact]
    public void Sign_and_verify_unlimited_negative_max_seats()
    {
        var priv = CreatePrivatePem();
        var pub = PublicFromPrivate(priv);
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Enterprise,
            MaxSeats = -1,
            Customer = "Acme"
        };

        var verified = LicenseCrypto.VerifyDocument(
            LicenseCrypto.SerializeDocument(LicenseCrypto.Sign(claims, priv)),
            pub);

        Assert.Equal(-1, verified.MaxSeats);
        Assert.True(LicenseCrypto.IsUnlimited(verified.MaxSeats));
    }

    [Fact]
    public void Tampered_payload_fails_verify()
    {
        var priv = CreatePrivatePem();
        var pub = PublicFromPrivate(priv);
        var claims = new LicenseClaims
        {
            LicenseId = Guid.NewGuid(),
            Tier = LicenseTiers.Team,
            MaxSeats = 10,
            Customer = "Contoso"
        };

        var json = LicenseCrypto.SerializeDocument(LicenseCrypto.Sign(claims, priv));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement.Clone();
        // Bump maxSeats in text.
        var tampered = json.Replace("\"maxSeats\": 10", "\"maxSeats\": 999", StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => LicenseCrypto.VerifyDocument(tampered, pub));
    }

    [Fact]
    public void NormalizeClaims_does_not_invent_license_id()
    {
        var claims = new LicenseClaims
        {
            LicenseId = Guid.Empty,
            Tier = " TEAM ",
            Customer = "  x  ",
            MaxSeats = 5
        };

        LicenseCrypto.NormalizeClaims(claims);
        Assert.Equal(Guid.Empty, claims.LicenseId);
        Assert.Equal(LicenseTiers.Team, claims.Tier);
        Assert.Equal("x", claims.Customer);
    }

    [Fact]
    public void Embedded_product_key_verifies_sample_if_present()
    {
        var sample = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tools", "licensing", "sample-team-25.lic.json"));
        if (!File.Exists(sample))
        {
            return; // optional artifact
        }

        var claims = LicenseCrypto.VerifyDocument(File.ReadAllText(sample));
        Assert.Equal(LicenseTiers.Team, claims.Tier);
        Assert.Equal(25, claims.MaxSeats);
    }
}
