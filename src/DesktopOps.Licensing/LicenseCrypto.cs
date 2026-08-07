using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopOps.Licensing;

/// <summary>Signs and verifies DesktopOps license documents.</summary>
public static class LicenseCrypto
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>Canonical JSON bytes used for signing/verifying claims.</summary>
    public static byte[] GetCanonicalPayloadBytes(LicenseClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return JsonSerializer.SerializeToUtf8Bytes(claims, JsonOptions);
    }

    /// <summary>Signs claims with an RSA private key PEM and returns a document.</summary>
    public static SignedLicenseDocument Sign(LicenseClaims claims, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var payloadBytes = GetCanonicalPayloadBytes(claims);
        var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new SignedLicenseDocument
        {
            Payload = claims,
            Signature = Convert.ToBase64String(signature)
        };
    }

    /// <summary>Serializes a signed document to UTF-8 JSON text.</summary>
    public static string SerializeDocument(SignedLicenseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var options = new JsonSerializerOptions(JsonOptions) { WriteIndented = true };
        return JsonSerializer.Serialize(document, options);
    }

    /// <summary>Parses and verifies a license JSON document. Throws on failure.</summary>
    public static LicenseClaims VerifyDocument(
        string documentJson,
        string? publicKeyPem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentJson);

        var document = JsonSerializer.Deserialize<SignedLicenseDocument>(documentJson, JsonOptions)
            ?? throw new InvalidOperationException("License document is empty.");

        if (document.Payload is null)
        {
            throw new InvalidOperationException("License payload is missing.");
        }

        if (string.IsNullOrWhiteSpace(document.Signature))
        {
            throw new InvalidOperationException("License signature is missing.");
        }

        var pem = string.IsNullOrWhiteSpace(publicKeyPem)
            ? LicensingPublicKeys.DefaultRsaPublicKeyPem
            : publicKeyPem;

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var payloadBytes = GetCanonicalPayloadBytes(document.Payload);
        var signature = Convert.FromBase64String(document.Signature.Trim());
        if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
        {
            throw new InvalidOperationException("License signature verification failed.");
        }

        NormalizeClaims(document.Payload);
        return document.Payload;
    }

    /// <summary>Normalizes tier casing and seat defaults.</summary>
    public static void NormalizeClaims(LicenseClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        claims.Tier = string.IsNullOrWhiteSpace(claims.Tier)
            ? LicenseTiers.Community
            : claims.Tier.Trim().ToLowerInvariant();
        claims.Customer = claims.Customer?.Trim() ?? string.Empty;
        if (claims.LicenseId == Guid.Empty)
        {
            claims.LicenseId = Guid.NewGuid();
        }
    }

    /// <summary>True when max seats means unlimited.</summary>
    public static bool IsUnlimited(int? maxSeats) => maxSeats is null or < 0;
}
