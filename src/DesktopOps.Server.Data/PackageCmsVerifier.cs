using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace DesktopOps.Server.Data;

/// <summary>Verifies detached CMS/PKCS#7 signatures for package files.</summary>
public static class PackageCmsVerifier
{
    /// <summary>
    /// Verifies that <paramref name="signatureBytes"/> is a detached CMS signature over the package bytes.
    /// When <paramref name="trustedThumbprints"/> is non-empty, the signer must match one of them.
    /// </summary>
    public static void VerifyDetachedSignature(
        string packageAbsolutePath,
        byte[] signatureBytes,
        IReadOnlyCollection<string>? trustedThumbprints = null)
    {
        if (!File.Exists(packageAbsolutePath))
        {
            throw new FileNotFoundException("Package file not found.", packageAbsolutePath);
        }

        if (signatureBytes.Length == 0)
        {
            throw new InvalidOperationException("Package signature is empty.");
        }

        var content = new ContentInfo(File.ReadAllBytes(packageAbsolutePath));
        var signedCms = new SignedCms(content, detached: true);
        signedCms.Decode(signatureBytes);
        signedCms.CheckSignature(verifySignatureOnly: true);

        if (signedCms.SignerInfos.Count == 0)
        {
            throw new InvalidOperationException("Package signature has no signers.");
        }

        var trusted = NormalizeThumbprints(trustedThumbprints);
        if (trusted.Count == 0)
        {
            return;
        }

        foreach (SignerInfo signerInfo in signedCms.SignerInfos)
        {
            var certificate = signerInfo.Certificate
                ?? throw new InvalidOperationException("Package signer certificate is missing.");
            var thumb = PackageCmsSigner.NormalizeThumbprint(certificate.Thumbprint);
            if (trusted.Contains(thumb))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "Package signature was not created by a trusted certificate thumbprint.");
    }

    /// <summary>
    /// Parses a comma/semicolon-separated thumbprint list.
    /// When the config string is non-empty but yields no valid thumbs, throws.
    /// </summary>
    public static HashSet<string> ParseTrustedThumbprints(string? configured)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return set;
        }

        foreach (var part in configured.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var normalized = PackageCmsSigner.NormalizeThumbprint(part);
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }

        if (set.Count == 0)
        {
            throw new InvalidOperationException(
                "Trusted CMS thumbprint list is configured but contains no valid thumbprints.");
        }

        return set;
    }

    private static HashSet<string> NormalizeThumbprints(IReadOnlyCollection<string>? thumbprints)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (thumbprints is null)
        {
            return set;
        }

        foreach (var thumbprint in thumbprints)
        {
            var normalized = PackageCmsSigner.NormalizeThumbprint(thumbprint);
            if (normalized.Length > 0)
            {
                set.Add(normalized);
            }
        }

        return set;
    }
}
