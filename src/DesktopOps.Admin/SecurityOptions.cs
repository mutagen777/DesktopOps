namespace DesktopOps.Admin;

/// <summary>Windows / AD authorization settings for the Blazor admin portal.</summary>
public sealed class SecurityOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Security";

    /// <summary>
    /// When false (default for local demo), auth policies always succeed.
    /// When true, Windows Negotiate auth and AD group membership are required.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>AD group for managers (assignments, groups, rollouts). Developers are included automatically.</summary>
    public string ADGroup { get; set; } = string.Empty;

    /// <summary>AD group for developers (programs, releases, publish).</summary>
    public string DeveloperADGroup { get; set; } = string.Empty;
}
