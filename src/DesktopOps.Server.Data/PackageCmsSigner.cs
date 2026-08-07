using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace DesktopOps.Server.Data;

/// <summary>Creates detached CMS/PKCS#7 signatures for package files.</summary>
public static class PackageCmsSigner
{
    /// <summary>Writes a detached .p7s next to <paramref name="packageAbsolutePath"/> and returns the absolute signature path.</summary>
    public static string SignPackageFile(string packageAbsolutePath, string certificateThumbprint)
    {
        if (!File.Exists(packageAbsolutePath))
        {
            throw new FileNotFoundException("Package file not found.", packageAbsolutePath);
        }

        var thumb = NormalizeThumbprint(certificateThumbprint);
        if (thumb.Length != 40)
        {
            throw new ArgumentException("Certificate thumbprint must be 40 hex characters.", nameof(certificateThumbprint));
        }

        using var certificate = FindCertificate(thumb)
            ?? throw new InvalidOperationException(
                $"Signing certificate with thumbprint {thumb} was not found in CurrentUser\\My or LocalMachine\\My.");

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException($"Certificate {thumb} does not have a private key.");
        }

        var content = new ContentInfo(File.ReadAllBytes(packageAbsolutePath));
        var signedCms = new SignedCms(content, detached: true);
        var signer = new CmsSigner(certificate)
        {
            IncludeOption = X509IncludeOption.ExcludeRoot,
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1") // SHA-256
        };
        signedCms.ComputeSignature(signer);

        var signaturePath = packageAbsolutePath + ".p7s";
        File.WriteAllBytes(signaturePath, signedCms.Encode());
        return signaturePath;
    }

    /// <summary>Normalizes a certificate thumbprint to uppercase hex without spaces.</summary>
    public static string NormalizeThumbprint(string thumbprint)
    {
        return string.Concat(thumbprint.Where(static c => !char.IsWhiteSpace(c))).ToUpperInvariant();
    }

    private static X509Certificate2? FindCertificate(string normalizedThumbprint)
    {
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            var matches = store.Certificates.Find(X509FindType.FindByThumbprint, normalizedThumbprint, validOnly: false);
            if (matches.Count > 0)
            {
                return matches[0];
            }
        }

        return null;
    }
}
