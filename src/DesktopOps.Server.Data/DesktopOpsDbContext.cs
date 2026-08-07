using Microsoft.EntityFrameworkCore;

namespace DesktopOps.Server.Data;

public sealed class DesktopOpsDbContext : DbContext
{
    public DesktopOpsDbContext(DbContextOptions<DesktopOpsDbContext> options)
        : base(options)
    {
    }

    public DbSet<ManagedProgram> Programs => Set<ManagedProgram>();

    public DbSet<UserGroup> UserGroups => Set<UserGroup>();

    public DbSet<UserGroupMember> UserGroupMembers => Set<UserGroupMember>();

    public DbSet<ProgramAssignment> ProgramAssignments => Set<ProgramAssignment>();

    public DbSet<ReleasePackage> ReleasePackages => Set<ReleasePackage>();

    public DbSet<ClientRegistration> ClientRegistrations => Set<ClientRegistration>();

    public DbSet<DeploymentEvent> DeploymentEvents => Set<DeploymentEvent>();

    public DbSet<LicenseState> LicenseStates => Set<LicenseState>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<TimestampEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ManagedProgram>(entity =>
        {
            entity.HasIndex(static item => item.Slug).IsUnique();
            entity.Property(static item => item.Name).HasMaxLength(200);
            entity.Property(static item => item.Slug).HasMaxLength(100);
            entity.Property(static item => item.ShortName).HasMaxLength(100);
        });

        modelBuilder.Entity<UserGroup>(entity =>
        {
            entity.HasIndex(static item => item.Name).IsUnique();
            entity.Property(static item => item.Name).HasMaxLength(200);
            entity.Property(static item => item.ActiveDirectoryGroup).HasMaxLength(200);
        });

        modelBuilder.Entity<UserGroupMember>(entity =>
        {
            entity.HasIndex(static item => new { item.UserGroupId, item.UserName }).IsUnique();
            entity.Property(static item => item.UserName).HasMaxLength(200);
            entity.Property(static item => item.WindowsSid).HasMaxLength(200);
        });

        modelBuilder.Entity<ProgramAssignment>(entity =>
        {
            entity.HasIndex(static item => new { item.ProgramId, item.UserGroupId }).IsUnique();
        });

        modelBuilder.Entity<ReleasePackage>(entity =>
        {
            entity.HasIndex(static item => new { item.ProgramId, item.Version }).IsUnique();
            entity.Property(static item => item.Version).HasMaxLength(50);
            entity.Property(static item => item.OriginalFileName).HasMaxLength(260);
            entity.Property(static item => item.PackageHash).HasMaxLength(128);
            entity.Property(static item => item.SignaturePath).HasMaxLength(260);
            entity.Property(static item => item.DeltaPath).HasMaxLength(260);
            entity.Property(static item => item.DeltaHash).HasMaxLength(128);
            entity.Property(static item => item.DeltaBaseVersion).HasMaxLength(50);
        });

        modelBuilder.Entity<ClientRegistration>(entity =>
        {
            entity.HasIndex(static item => new { item.ProgramSlug, item.UserName, item.MachineName }).IsUnique();
            entity.Property(static item => item.ProgramSlug).HasMaxLength(100);
            entity.Property(static item => item.UserName).HasMaxLength(200);
            entity.Property(static item => item.MachineName).HasMaxLength(200);
            entity.Property(static item => item.WindowsSid).HasMaxLength(200);
        });

        modelBuilder.Entity<LicenseState>(entity =>
        {
            entity.HasKey(static item => item.Id);
            entity.Property(static item => item.LicenseDocumentJson).HasColumnType("TEXT");
        });
    }
}
