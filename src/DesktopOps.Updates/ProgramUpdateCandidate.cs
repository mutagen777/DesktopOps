namespace DesktopOps.Updates;

public enum ProgramUpdateAction
{
    Add,
    Update,
    Delete,
    None
}

public sealed class ProgramUpdateCandidate
{
    public required AssignedProgram Program { get; init; }

    public ProgramUpdateAction Action { get; init; }

    public string? InstalledVersion { get; init; }
}
