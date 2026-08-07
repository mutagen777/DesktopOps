namespace DesktopOps.Server.Data;

public abstract class TimestampEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ManagedProgram : TimestampEntity
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? ShortName { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public List<ProgramAssignment> Assignments { get; set; } = [];

    public List<ReleasePackage> Releases { get; set; } = [];
}

public sealed class UserGroup : TimestampEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Optional AD/local security group used as the source for member sync.</summary>
    public string? ActiveDirectoryGroup { get; set; }

    public List<UserGroupMember> Members { get; set; } = [];

    public List<ProgramAssignment> Assignments { get; set; } = [];
}

public sealed class UserGroupMember : TimestampEntity
{
    public Guid UserGroupId { get; set; }

    public UserGroup? UserGroup { get; set; }

    public string UserName { get; set; } = string.Empty;

    /// <summary>Optional Windows SID for domain-joined agents.</summary>
    public string? WindowsSid { get; set; }
}

public sealed class ProgramAssignment : TimestampEntity
{
    public Guid ProgramId { get; set; }

    public ManagedProgram? Program { get; set; }

    public Guid UserGroupId { get; set; }

    public UserGroup? UserGroup { get; set; }
}

public sealed class ReleasePackage : TimestampEntity
{
    public Guid ProgramId { get; set; }

    public ManagedProgram? Program { get; set; }

    public string Version { get; set; } = string.Empty;

    public string PackagePath { get; set; } = string.Empty;

    public string OriginalFileName { get; set; } = string.Empty;

    public string PackageHash { get; set; } = string.Empty;

    public long PackageSize { get; set; }

    /// <summary>Relative path to detached CMS/PKCS#7 signature (.p7s), when present.</summary>
    public string? SignaturePath { get; set; }

    /// <summary>Relative path to optional delta ZIP from <see cref="DeltaBaseVersion"/>.</summary>
    public string? DeltaPath { get; set; }

    /// <summary>SHA-256 of the delta ZIP.</summary>
    public string? DeltaHash { get; set; }

    public long? DeltaSize { get; set; }

    /// <summary>Installed/base package version required to apply the delta.</summary>
    public string? DeltaBaseVersion { get; set; }

    public string? ReleaseNotes { get; set; }

    public bool IsMandatory { get; set; }

    /// <summary>
    /// Percentage of assigned users (0–100) that receive this published release.
    /// Stable per user+release via hash. 100 = everyone.
    /// </summary>
    public int RolloutPercent { get; set; } = 100;

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public bool IsPublished => PublishedAtUtc.HasValue;

    public List<DeploymentEvent> DeploymentEvents { get; set; } = [];
}

/// <summary>Registration for an embedded app client or the tray agent.</summary>
public sealed class ClientRegistration : TimestampEntity
{
    /// <summary>Program slug for app clients, or <c>_agent_</c> for the tray agent.</summary>
    public string ProgramSlug { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string MachineName { get; set; } = string.Empty;

    public string? WindowsSid { get; set; }

    public string CurrentVersion { get; set; } = string.Empty;

    public DateTimeOffset LastSeenAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<DeploymentEvent> DeploymentEvents { get; set; } = [];
}

public sealed class DeploymentEvent : TimestampEntity
{
    public Guid ClientRegistrationId { get; set; }

    public ClientRegistration? ClientRegistration { get; set; }

    public Guid ReleasePackageId { get; set; }

    public ReleasePackage? ReleasePackage { get; set; }

    public DeploymentStatus Status { get; set; }

    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public string? Message { get; set; }
}

public enum DeploymentStatus
{
    Available,
    Downloading,
    Installed,
    Failed,
    Removed
}
