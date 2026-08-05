namespace DesktopOps.Updates;

public sealed class AssignedProgram
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    public UpdateRelease? LatestRelease { get; set; }
}
