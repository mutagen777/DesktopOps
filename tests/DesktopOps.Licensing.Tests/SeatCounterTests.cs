using DesktopOps.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DesktopOps.Licensing.Tests;

public sealed class SeatCounterTests
{
    [Theory]
    [InlineData("DOMAIN\\alice", "S-1-5-21-1", "s:S-1-5-21-1")]
    [InlineData("alice", null, "u:alice")]
    [InlineData("  Alice  ", null, "u:alice")]
    [InlineData(null, "S-1-5-21-9", "s:S-1-5-21-9")]
    [InlineData("", "", "")]
    [InlineData("  ", null, "")]
    public void NormalizeSeatKey_prefers_sid(string? user, string? sid, string expected)
    {
        Assert.Equal(expected, SeatCounter.NormalizeSeatKey(user, sid));
    }

    [Fact]
    public async Task CountAssignedSeats_dedupes_same_sid_different_usernames()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var db = harness.Db;
        var program = new ManagedProgram { Name = "App", Slug = "app" };
        var group = new UserGroup { Name = "Users" };
        db.Programs.Add(program);
        db.UserGroups.Add(group);
        await db.SaveChangesAsync();

        db.ProgramAssignments.Add(new ProgramAssignment { ProgramId = program.Id, UserGroupId = group.Id });
        db.UserGroupMembers.AddRange(
            new UserGroupMember { UserGroupId = group.Id, UserName = @"DOMAIN\alice", WindowsSid = "S-1-5-21-1" },
            new UserGroupMember { UserGroupId = group.Id, UserName = "alice", WindowsSid = "S-1-5-21-1" },
            new UserGroupMember { UserGroupId = group.Id, UserName = "bob", WindowsSid = "S-1-5-21-2" });
        await db.SaveChangesAsync();

        Assert.Equal(2, await SeatCounter.CountAssignedSeatsAsync(db));
    }

    [Fact]
    public async Task CountAssignedSeats_ignores_members_without_assignment()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var db = harness.Db;
        var assigned = new UserGroup { Name = "Assigned" };
        var idle = new UserGroup { Name = "Idle" };
        var program = new ManagedProgram { Name = "App", Slug = "app" };
        db.UserGroups.AddRange(assigned, idle);
        db.Programs.Add(program);
        await db.SaveChangesAsync();

        db.ProgramAssignments.Add(new ProgramAssignment { ProgramId = program.Id, UserGroupId = assigned.Id });
        db.UserGroupMembers.AddRange(
            new UserGroupMember { UserGroupId = assigned.Id, UserName = "alice" },
            new UserGroupMember { UserGroupId = idle.Id, UserName = "carol" });
        await db.SaveChangesAsync();

        Assert.Equal(1, await SeatCounter.CountAssignedSeatsAsync(db));
    }

    [Fact]
    public async Task LicenseService_community_without_blob()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var db = harness.Db;
        await DatabaseInitializer.InitializeAsync(db);

        var community = await LicenseService.EvaluateAsync(db);
        Assert.Equal("community", community.Tier);
        Assert.Equal(3, community.MaxSeats);
        Assert.False(community.BlocksMutations);

        await LicenseService.ClearAsync(db);
        Assert.False((await LicenseService.EvaluateAsync(db)).HasInstalledLicense);
    }

    [Fact]
    public async Task LicenseService_invalid_blob_blocks_mutations()
    {
        await using var harness = await SqliteHarness.CreateAsync();
        var db = harness.Db;
        await DatabaseInitializer.InitializeAsync(db);
        db.LicenseStates.Add(new LicenseState
        {
            Id = LicenseService.SingletonId,
            LicenseDocumentJson = """{"payload":{"tier":"team","maxSeats":25},"signature":"AAAA"}"""
        });
        await db.SaveChangesAsync();

        var eval = await LicenseService.EvaluateAsync(db);
        Assert.True(eval.BlocksMutations);
        Assert.False(eval.SignatureValid);
    }
}
