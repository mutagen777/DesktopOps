namespace DesktopOps.Updates;

public sealed class RegisteredClient
{
    public Guid Id { get; init; }

    public string ProgramSlug { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;

    public string MachineName { get; init; } = string.Empty;

    public string CurrentVersion { get; init; } = string.Empty;
}
