using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace DesktopOps.Admin.Services;

/// <summary>Resolved directory account used for group membership.</summary>
public sealed record DirectoryAccount(string UserName, string? WindowsSid, string? DisplayName);

/// <summary>Looks up directory users and group members (Windows/AD and/or Entra ID).</summary>
public interface IDirectoryAccountLookup
{
    /// <summary>True when at least one directory backend is configured and usable.</summary>
    bool IsAvailable { get; }

    /// <summary>Resolves a user by SAM account name, UPN, DOMAIN\user, or Entra object id.</summary>
    Task<DirectoryAccount?> ResolveUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>Lists enabled user principals that are members of the given directory group.</summary>
    /// <exception cref="DirectoryGroupLookupException">Group missing or directory error.</exception>
    Task<IReadOnlyList<DirectoryAccount>> GetGroupMembersAsync(
        string groupName,
        CancellationToken cancellationToken = default);
}

/// <summary>Directory group could not be resolved or queried.</summary>
public sealed class DirectoryGroupLookupException : Exception
{
    public DirectoryGroupLookupException(string message) : base(message)
    {
    }

    public DirectoryGroupLookupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Helpers for Entra vs Windows group reference strings.</summary>
public static class DirectoryGroupReference
{
    public const string EntraPrefix = "entra:";

    /// <summary>True when the value is an Entra object id or uses the entra: prefix.</summary>
    public static bool IsEntraReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith(EntraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Guid.TryParse(trimmed, out _);
    }

    /// <summary>Strips the optional entra: prefix; returns remaining group id or name.</summary>
    public static string NormalizeEntraReference(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith(EntraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed[EntraPrefix.Length..].Trim();
        }

        return trimmed;
    }
}

/// <summary>Windows AccountManagement-based directory lookup.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDirectoryAccountLookup : IDirectoryAccountLookup
{
    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public Task<DirectoryAccount?> ResolveUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ResolveUser(userName));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DirectoryAccount>> GetGroupMembersAsync(
        string groupName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(GetGroupMembers(groupName));
    }

    private DirectoryAccount? ResolveUser(string userName)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var trimmed = userName.Trim();
        try
        {
            using var context = CreateContext(trimmed);
            var principal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, GetSamAccountName(trimmed))
                ?? UserPrincipal.FindByIdentity(context, trimmed);

            return principal is null ? TryTranslateSid(trimmed) : ToAccount(principal);
        }
        catch
        {
            return TryTranslateSid(trimmed);
        }
    }

    private IReadOnlyList<DirectoryAccount> GetGroupMembers(string groupName)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(groupName))
        {
            return [];
        }

        var trimmed = groupName.Trim();
        try
        {
            using var context = CreateContext(trimmed);
            using var group = GroupPrincipal.FindByIdentity(context, GetSamAccountName(trimmed))
                ?? GroupPrincipal.FindByIdentity(context, trimmed);

            if (group is null)
            {
                throw new DirectoryGroupLookupException($"Directory group not found: {trimmed}");
            }

            var results = new List<DirectoryAccount>();
            foreach (var member in group.GetMembers(recursive: true))
            {
                if (member is UserPrincipal user && user.Enabled != false)
                {
                    results.Add(ToAccount(user));
                }

                member.Dispose();
            }

            return results
                .GroupBy(static item => item.UserName, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static item => item.UserName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (DirectoryGroupLookupException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DirectoryGroupLookupException($"Directory lookup failed for \"{trimmed}\".", ex);
        }
    }

    private static DirectoryAccount ToAccount(UserPrincipal principal)
    {
        var userName = principal.SamAccountName
            ?? principal.UserPrincipalName
            ?? principal.Name
            ?? principal.Sid.Value;

        return new DirectoryAccount(userName, principal.Sid.Value, principal.DisplayName);
    }

    private static DirectoryAccount? TryTranslateSid(string userName)
    {
        try
        {
            var account = new NTAccount(userName);
            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            var sam = GetSamAccountName(userName);
            return new DirectoryAccount(sam, sid.Value, null);
        }
        catch
        {
            return null;
        }
    }

    private static PrincipalContext CreateContext(string identityHint)
    {
        if (identityHint.Contains('\\') || identityHint.Contains('@'))
        {
            try
            {
                return new PrincipalContext(ContextType.Domain);
            }
            catch
            {
                return new PrincipalContext(ContextType.Machine);
            }
        }

        try
        {
            return new PrincipalContext(ContextType.Domain);
        }
        catch
        {
            return new PrincipalContext(ContextType.Machine);
        }
    }

    private static string GetSamAccountName(string value)
    {
        var trimmed = value.Trim();
        var slash = trimmed.LastIndexOf('\\');
        if (slash >= 0 && slash < trimmed.Length - 1)
        {
            return trimmed[(slash + 1)..];
        }

        var at = trimmed.IndexOf('@');
        if (at > 0)
        {
            return trimmed[..at];
        }

        return trimmed;
    }
}

/// <summary>No-op lookup used when no directory backend is available.</summary>
public sealed class NullDirectoryAccountLookup : IDirectoryAccountLookup
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<DirectoryAccount?> ResolveUserAsync(string userName, CancellationToken cancellationToken = default)
        => Task.FromResult<DirectoryAccount?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<DirectoryAccount>> GetGroupMembersAsync(
        string groupName,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DirectoryAccount>>([]);
}
