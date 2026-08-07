namespace DesktopOps.Admin.Services;

/// <summary>
/// Routes directory lookups to Windows/AD and/or Entra Graph.
/// Explicit Entra refs (GUID or entra:…) always use Graph when configured;
/// other names prefer Windows when available, otherwise Entra.
/// </summary>
public sealed class CompositeDirectoryAccountLookup : IDirectoryAccountLookup
{
    private readonly IDirectoryAccountLookup? _windows;
    private readonly IDirectoryAccountLookup _entra;

    public CompositeDirectoryAccountLookup(
        IDirectoryAccountLookup? windowsLookup,
        IDirectoryAccountLookup entraLookup)
    {
        _windows = windowsLookup;
        _entra = entraLookup;
    }

    /// <inheritdoc />
    public bool IsAvailable =>
        (_windows?.IsAvailable == true) || _entra.IsAvailable;

    /// <inheritdoc />
    public async Task<DirectoryAccount?> ResolveUserAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) || !IsAvailable)
        {
            return null;
        }

        if (DirectoryGroupReference.IsEntraReference(userName) || PreferEntraOnly())
        {
            return await _entra.ResolveUserAsync(userName, cancellationToken);
        }

        if (_windows?.IsAvailable == true)
        {
            var windowsAccount = await _windows.ResolveUserAsync(userName, cancellationToken);
            if (windowsAccount is not null)
            {
                return windowsAccount;
            }
        }

        if (_entra.IsAvailable)
        {
            return await _entra.ResolveUserAsync(userName, cancellationToken);
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectoryAccount>> GetGroupMembersAsync(
        string groupName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            return [];
        }

        if (!IsAvailable)
        {
            throw new DirectoryGroupLookupException("No directory backend is available.");
        }

        if (DirectoryGroupReference.IsEntraReference(groupName) || PreferEntraOnly())
        {
            EnsureEntraConfigured(groupName);
            return await _entra.GetGroupMembersAsync(groupName, cancellationToken);
        }

        if (_windows?.IsAvailable == true)
        {
            return await _windows.GetGroupMembersAsync(groupName, cancellationToken);
        }

        EnsureEntraConfigured(groupName);
        return await _entra.GetGroupMembersAsync(groupName, cancellationToken);
    }

    private bool PreferEntraOnly() =>
        _windows?.IsAvailable != true && _entra.IsAvailable;

    private void EnsureEntraConfigured(string groupName)
    {
        if (!_entra.IsAvailable)
        {
            throw new DirectoryGroupLookupException(
                $"Entra group \"{groupName}\" requires DirectorySync:Entra (TenantId, ClientId, ClientSecret).");
        }
    }
}
