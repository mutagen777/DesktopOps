namespace DesktopOps.Updates;

public sealed class ProgramInstallationManifest
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    public List<string> Files { get; set; } = [];

    public string PackageHash { get; set; } = string.Empty;
}
