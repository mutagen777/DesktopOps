namespace DesktopOps.Server.Data;

/// <summary>Optional detached CMS signing for program ZIP packages (Admin / Server upload).</summary>
public sealed class PackageSigningOptions
{
    public const string SectionName = "Signing";

    /// <summary>Certificate thumbprint in CurrentUser or LocalMachine\My used to sign ZIPs.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>When true, uploads fail if no signature is produced or supplied.</summary>
    public bool RequireSignature { get; set; }

    /// <summary>True when a signing certificate thumbprint is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(CertificateThumbprint);
}
