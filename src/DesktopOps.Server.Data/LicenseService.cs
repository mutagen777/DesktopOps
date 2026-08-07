using DesktopOps.Licensing;
using Microsoft.EntityFrameworkCore;

namespace DesktopOps.Server.Data;

/// <summary>Counts unique people eligible for software (assigned group members).</summary>
public static class SeatCounter
{
    /// <summary>
    /// Distinct seats: members of groups that have at least one program assignment.
    /// Prefers Windows SID when present (stable person id); otherwise normalized user name.
    /// </summary>
    public static async Task<int> CountAssignedSeatsAsync(
        DesktopOpsDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        var members = await dbContext.UserGroupMembers
            .AsNoTracking()
            .Where(static member => member.UserGroup!.Assignments.Any())
            .Select(static member => new { member.UserName, member.WindowsSid })
            .ToListAsync(cancellationToken);

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            var key = NormalizeSeatKey(member.UserName, member.WindowsSid);
            if (!string.IsNullOrEmpty(key))
            {
                keys.Add(key);
            }
        }

        return keys.Count;
    }

    /// <summary>
    /// Builds a stable seat identity key. SID wins when present so DOMAIN\user and user
    /// with the same SID count as one person.
    /// </summary>
    public static string NormalizeSeatKey(string? userName, string? windowsSid)
    {
        if (!string.IsNullOrWhiteSpace(windowsSid))
        {
            return "s:" + windowsSid.Trim();
        }

        if (!string.IsNullOrWhiteSpace(userName))
        {
            return "u:" + userName.Trim().ToLowerInvariant();
        }

        return string.Empty;
    }
}

/// <summary>Loads, stores, and evaluates the installed license.</summary>
public static class LicenseService
{
    public static readonly Guid SingletonId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static async Task<LicenseEvaluation> EvaluateAsync(
        DesktopOpsDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        var used = await SeatCounter.CountAssignedSeatsAsync(dbContext, cancellationToken);
        var state = await dbContext.LicenseStates.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == SingletonId, cancellationToken);

        if (state is null || string.IsNullOrWhiteSpace(state.LicenseDocumentJson))
        {
            return LicenseEvaluation.Community(used);
        }

        try
        {
            var claims = LicenseCrypto.VerifyDocument(state.LicenseDocumentJson);
            return LicenseEvaluator.Evaluate(claims, used, hasStoredBlob: true, verificationError: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return LicenseEvaluator.Evaluate(
                claims: null,
                usedSeats: used,
                hasStoredBlob: true,
                verificationError: ex.Message);
        }
    }

    public static async Task InstallAsync(
        DesktopOpsDbContext dbContext,
        string documentJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        _ = LicenseCrypto.VerifyDocument(documentJson);

        var state = await dbContext.LicenseStates.FirstOrDefaultAsync(item => item.Id == SingletonId, cancellationToken);
        if (state is null)
        {
            state = new LicenseState { Id = SingletonId };
            dbContext.LicenseStates.Add(state);
        }

        state.LicenseDocumentJson = documentJson.Trim();
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task ClearAsync(
        DesktopOpsDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        var state = await dbContext.LicenseStates.FirstOrDefaultAsync(item => item.Id == SingletonId, cancellationToken);
        if (state is null)
        {
            return;
        }

        state.LicenseDocumentJson = null;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
