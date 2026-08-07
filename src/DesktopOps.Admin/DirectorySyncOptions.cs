namespace DesktopOps.Admin;

/// <summary>Background AD/Windows group sync settings.</summary>
public sealed class DirectorySyncOptions
{
    public const string SectionName = "DirectorySync";

    /// <summary>When true, Admin periodically syncs groups that have ActiveDirectoryGroup set.</summary>
    public bool Enabled { get; set; }

    /// <summary>Minutes between full sync passes (minimum 5).</summary>
    public int IntervalMinutes { get; set; } = 60;
}
