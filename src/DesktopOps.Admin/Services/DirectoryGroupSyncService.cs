using System.Collections.Concurrent;
using DesktopOps.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DesktopOps.Admin.Services;

/// <summary>Result of syncing one DesktopOps group from a directory group.</summary>
public sealed record DirectoryGroupSyncResult(
    Guid GroupId,
    string AdGroupName,
    int Added,
    int Updated,
    int DirectoryMemberCount,
    string? Error);

/// <summary>Imports directory group members into DesktopOps user groups.</summary>
public sealed class DirectoryGroupSyncService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> GroupLocks = new();

    private readonly IDbContextFactory<DesktopOpsDbContext> _dbFactory;
    private readonly IDirectoryAccountLookup _directoryLookup;
    private readonly ILogger<DirectoryGroupSyncService> _logger;

    public DirectoryGroupSyncService(
        IDbContextFactory<DesktopOpsDbContext> dbFactory,
        IDirectoryAccountLookup directoryLookup,
        ILogger<DirectoryGroupSyncService> logger)
    {
        _dbFactory = dbFactory;
        _directoryLookup = directoryLookup;
        _logger = logger;
    }

    public async Task<DirectoryGroupSyncResult> SyncGroupAsync(
        Guid groupId,
        string? adGroupNameOverride = null,
        CancellationToken cancellationToken = default)
    {
        var gate = GroupLocks.GetOrAdd(groupId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            return await SyncGroupCoreAsync(groupId, adGroupNameOverride, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<DirectoryGroupSyncResult> SyncGroupCoreAsync(
        Guid groupId,
        string? adGroupNameOverride,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var group = await dbContext.UserGroups
            .Include(static item => item.Members)
            .FirstOrDefaultAsync(item => item.Id == groupId, cancellationToken);

        if (group is null)
        {
            return new DirectoryGroupSyncResult(groupId, adGroupNameOverride ?? "", 0, 0, 0, "Group not found.");
        }

        var adGroupName = string.IsNullOrWhiteSpace(adGroupNameOverride)
            ? group.ActiveDirectoryGroup
            : adGroupNameOverride.Trim();

        if (string.IsNullOrWhiteSpace(adGroupName))
        {
            return new DirectoryGroupSyncResult(groupId, "", 0, 0, 0, "AD group name is missing.");
        }

        if (!_directoryLookup.IsAvailable)
        {
            return new DirectoryGroupSyncResult(groupId, adGroupName, 0, 0, 0, "Directory services are only available on Windows.");
        }

        IReadOnlyList<DirectoryAccount> accounts;
        try
        {
            accounts = await Task.Run(
                () => _directoryLookup.GetGroupMembers(adGroupName),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DirectoryGroupLookupException ex)
        {
            _logger.LogWarning(ex, "Directory lookup failed for group {AdGroup}", adGroupName);
            return new DirectoryGroupSyncResult(groupId, adGroupName, 0, 0, 0, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Directory lookup failed for group {AdGroup}", adGroupName);
            return new DirectoryGroupSyncResult(groupId, adGroupName, 0, 0, 0, ex.Message);
        }

        group.ActiveDirectoryGroup = adGroupName.Trim();
        var existing = group.Members.ToDictionary(static item => item.UserName, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var updated = 0;

        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existing.TryGetValue(account.UserName, out var member))
            {
                if (string.IsNullOrWhiteSpace(member.WindowsSid) && !string.IsNullOrWhiteSpace(account.WindowsSid))
                {
                    member.WindowsSid = account.WindowsSid;
                    updated++;
                }

                continue;
            }

            group.Members.Add(new UserGroupMember
            {
                UserGroupId = groupId,
                UserName = account.UserName,
                WindowsSid = account.WindowsSid
            });
            added++;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Directory sync save failed for group {GroupId}", groupId);
            return new DirectoryGroupSyncResult(groupId, adGroupName, added, updated, accounts.Count, ex.Message);
        }

        _logger.LogInformation(
            "Directory sync \"{AdGroup}\" for {Group}: {Added} new, {Updated} SID updated, {Total} in directory",
            adGroupName,
            group.Name,
            added,
            updated,
            accounts.Count);

        return new DirectoryGroupSyncResult(groupId, adGroupName, added, updated, accounts.Count, null);
    }

    public async Task<IReadOnlyList<DirectoryGroupSyncResult>> SyncAllLinkedGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var groupIds = await dbContext.UserGroups
            .Where(static item => item.ActiveDirectoryGroup != null && item.ActiveDirectoryGroup != "")
            .Select(static item => item.Id)
            .ToListAsync(cancellationToken);

        var results = new List<DirectoryGroupSyncResult>(groupIds.Count);
        foreach (var groupId in groupIds)
        {
            try
            {
                results.Add(await SyncGroupAsync(groupId, cancellationToken: cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scheduled sync failed for group {GroupId}", groupId);
                results.Add(new DirectoryGroupSyncResult(groupId, "", 0, 0, 0, ex.Message));
            }
        }

        return results;
    }
}
