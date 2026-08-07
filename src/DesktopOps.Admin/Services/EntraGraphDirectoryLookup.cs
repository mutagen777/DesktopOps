using Azure.Identity;
using DesktopOps.Admin;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace DesktopOps.Admin.Services;

/// <summary>Microsoft Graph lookup for Entra ID groups and users.</summary>
public sealed class EntraGraphDirectoryLookup : IDirectoryAccountLookup
{
    private readonly IOptionsMonitor<DirectorySyncOptions> _options;
    private readonly ILogger<EntraGraphDirectoryLookup> _logger;
    private readonly object _clientLock = new();
    private GraphServiceClient? _client;
    private string? _clientKey;

    public EntraGraphDirectoryLookup(
        IOptionsMonitor<DirectorySyncOptions> options,
        ILogger<EntraGraphDirectoryLookup> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => _options.CurrentValue.Entra.IsConfigured;

    /// <inheritdoc />
    public async Task<DirectoryAccount?> ResolveUserAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(userName))
        {
            return null;
        }

        var trimmed = DirectoryGroupReference.NormalizeEntraReference(userName);
        try
        {
            var client = GetClient();
            User? user = null;

            if (Guid.TryParse(trimmed, out _))
            {
                user = await client.Users[trimmed].GetAsync(
                    static request =>
                    {
                        request.QueryParameters.Select =
                        [
                            "id",
                            "displayName",
                            "userPrincipalName",
                            "mail",
                            "onPremisesSamAccountName",
                            "onPremisesSecurityIdentifier",
                            "accountEnabled"
                        ];
                    },
                    cancellationToken);
            }
            else
            {
                var filter = BuildUserFilter(trimmed);
                var page = await client.Users.GetAsync(
                    request =>
                    {
                        request.QueryParameters.Filter = filter;
                        request.QueryParameters.Top = 2;
                        request.QueryParameters.Select =
                        [
                            "id",
                            "displayName",
                            "userPrincipalName",
                            "mail",
                            "onPremisesSamAccountName",
                            "onPremisesSecurityIdentifier",
                            "accountEnabled"
                        ];
                    },
                    cancellationToken);

                user = page?.Value?.FirstOrDefault();
            }

            if (user is null || user.AccountEnabled == false)
            {
                return null;
            }

            return ToAccount(user);
        }
        catch (ODataError ex)
        {
            _logger.LogDebug(ex, "Entra user lookup failed for {UserName}", trimmed);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Entra user lookup failed for {UserName}", trimmed);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DirectoryAccount>> GetGroupMembersAsync(
        string groupName,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            throw new DirectoryGroupLookupException("Entra ID (Graph) is not configured.");
        }

        if (string.IsNullOrWhiteSpace(groupName))
        {
            return [];
        }

        var reference = DirectoryGroupReference.NormalizeEntraReference(groupName);
        try
        {
            var client = GetClient();
            var groupId = await ResolveGroupIdAsync(client, reference, cancellationToken);
            var results = new List<DirectoryAccount>();
            var page = await client.Groups[groupId].TransitiveMembers.GetAsync(
                static request =>
                {
                    request.QueryParameters.Select =
                    [
                        "id",
                        "displayName",
                        "userPrincipalName",
                        "mail",
                        "onPremisesSamAccountName",
                        "onPremisesSecurityIdentifier",
                        "accountEnabled"
                    ];
                },
                cancellationToken);

            while (page is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (page.Value is not null)
                {
                    foreach (var directoryObject in page.Value)
                    {
                        if (directoryObject is User user && user.AccountEnabled != false)
                        {
                            var account = ToAccount(user);
                            if (!string.IsNullOrWhiteSpace(account.UserName))
                            {
                                results.Add(account);
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(page.OdataNextLink))
                {
                    break;
                }

                page = await client.Groups[groupId].TransitiveMembers
                    .WithUrl(page.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
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
        catch (ODataError ex)
        {
            throw new DirectoryGroupLookupException(
                $"Entra group lookup failed for \"{reference}\": {ex.Error?.Message ?? ex.Message}",
                ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new DirectoryGroupLookupException($"Entra group lookup failed for \"{reference}\".", ex);
        }
    }

    private async Task<string> ResolveGroupIdAsync(
        GraphServiceClient client,
        string reference,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(reference, out _))
        {
            try
            {
                var byId = await client.Groups[reference].GetAsync(
                    static request => request.QueryParameters.Select = ["id", "displayName"],
                    cancellationToken);
                if (byId?.Id is not null)
                {
                    return byId.Id;
                }
            }
            catch (ODataError)
            {
                // Fall through to name search only when not a GUID path — GUID miss is not found.
            }

            throw new DirectoryGroupLookupException($"Entra group not found: {reference}");
        }

        var escaped = EscapeODataString(reference);
        var filter = $"displayName eq '{escaped}' or mailNickname eq '{escaped}'";
        var page = await client.Groups.GetAsync(
            request =>
            {
                request.QueryParameters.Filter = filter;
                request.QueryParameters.Top = 5;
                request.QueryParameters.Select = ["id", "displayName", "mailNickname"];
            },
            cancellationToken);

        var matches = page?.Value ?? [];
        if (matches.Count == 0)
        {
            throw new DirectoryGroupLookupException($"Entra group not found: {reference}");
        }

        if (matches.Count > 1)
        {
            throw new DirectoryGroupLookupException(
                $"Multiple Entra groups match \"{reference}\". Use the group object id or entra:<object-id>.");
        }

        return matches[0].Id
            ?? throw new DirectoryGroupLookupException($"Entra group not found: {reference}");
    }

    private GraphServiceClient GetClient()
    {
        var entra = _options.CurrentValue.Entra;
        if (!entra.IsConfigured)
        {
            throw new DirectoryGroupLookupException("Entra ID (Graph) is not configured.");
        }

        var key = $"{entra.TenantId}|{entra.ClientId}|{entra.ClientSecret}";
        lock (_clientLock)
        {
            if (_client is not null && string.Equals(_clientKey, key, StringComparison.Ordinal))
            {
                return _client;
            }

            var credential = new ClientSecretCredential(
                entra.TenantId!.Trim(),
                entra.ClientId!.Trim(),
                entra.ClientSecret!.Trim());
            _client = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
            _clientKey = key;
            return _client;
        }
    }

    private static string BuildUserFilter(string value)
    {
        var escaped = EscapeODataString(value);
        if (value.Contains('@', StringComparison.Ordinal))
        {
            return $"userPrincipalName eq '{escaped}' or mail eq '{escaped}'";
        }

        return $"onPremisesSamAccountName eq '{escaped}' or userPrincipalName eq '{escaped}' or mailNickname eq '{escaped}'";
    }

    private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static DirectoryAccount ToAccount(User user)
    {
        var userName = user.OnPremisesSamAccountName;
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = user.UserPrincipalName;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = user.Mail;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = user.Id;
        }

        return new DirectoryAccount(
            userName!.Trim(),
            string.IsNullOrWhiteSpace(user.OnPremisesSecurityIdentifier)
                ? null
                : user.OnPremisesSecurityIdentifier,
            user.DisplayName);
    }
}
