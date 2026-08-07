using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace DesktopOps.Admin.Services;

/// <summary>Resolved directory account used for group membership.</summary>
public sealed record DirectoryAccount(string UserName, string? WindowsSid, string? DisplayName);

/// <summary>Looks up Windows / Active Directory users and group members.</summary>
public interface IDirectoryAccountLookup
{
    /// <summary>True when running on Windows and directory APIs are usable.</summary>
    bool IsAvailable { get; }

    /// <summary>Resolves a user by SAM account name, UPN, or DOMAIN\user.</summary>
    DirectoryAccount? ResolveUser(string userName);

    /// <summary>Lists enabled user principals that are members of the given AD/local group.</summary>
    /// <exception cref="DirectoryGroupLookupException">Group missing or directory error.</exception>
    IReadOnlyList<DirectoryAccount> GetGroupMembers(string groupName);
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

/// <summary>Windows AccountManagement-based directory lookup.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDirectoryAccountLookup : IDirectoryAccountLookup
{
    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public DirectoryAccount? ResolveUser(string userName)
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

    /// <inheritdoc />
    public IReadOnlyList<DirectoryAccount> GetGroupMembers(string groupName)
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

/// <summary>No-op lookup used on non-Windows hosts.</summary>
public sealed class NullDirectoryAccountLookup : IDirectoryAccountLookup
{
    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public DirectoryAccount? ResolveUser(string userName) => null;

    /// <inheritdoc />
    public IReadOnlyList<DirectoryAccount> GetGroupMembers(string groupName) => [];
}
