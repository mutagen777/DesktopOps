using System.Security.Cryptography;
using System.Text;

namespace DesktopOps.Server;

/// <summary>API key settings for agent and optional admin management routes.</summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "Security";

    /// <summary>Default header name for the API key.</summary>
    public const string DefaultHeaderName = "X-DesktopOps-Key";

    /// <summary>HttpContext item key for the authenticated API role.</summary>
    public const string RoleItemKey = "DesktopOps.ApiRole";

    public const string RoleAdmin = "Admin";
    public const string RoleAgent = "Agent";

    /// <summary>
    /// Agent / fleet key. When <see cref="AdminApiKey"/> is empty, this key also unlocks management routes (demo / single-key mode).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Management API key for programs/groups/releases. When set, agents using only <see cref="ApiKey"/> cannot call those routes.
    /// </summary>
    public string AdminApiKey { get; set; } = string.Empty;

    /// <summary>HTTP header that carries the API key.</summary>
    public string ApiKeyHeader { get; set; } = DefaultHeaderName;

    /// <summary>True when at least one API key is configured.</summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(ApiKey) || !string.IsNullOrWhiteSpace(AdminApiKey);

    /// <summary>True when a dedicated admin key is configured.</summary>
    public bool HasSeparateAdminKey => !string.IsNullOrWhiteSpace(AdminApiKey);
}

/// <summary>Requires a valid API key on <c>/api/*</c> and enforces agent vs admin route scope.</summary>
public sealed class ApiKeyMiddleware
{
    private static readonly PathString ClientsPrefix = new("/api/clients");
    private static readonly PathString PackagesPrefix = new("/api/packages");

    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>Validates the API key for API routes.</summary>
    public async Task InvokeAsync(HttpContext context, ApiKeyOptions options)
    {
        if (!options.IsEnabled || !context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (!TryGetPresentedKey(context.Request, options, out var presented))
        {
            await WriteUnauthorizedAsync(context, options);
            return;
        }

        var isAdmin = Matches(presented, options.AdminApiKey)
            || (!options.HasSeparateAdminKey && Matches(presented, options.ApiKey));
        var isAgent = Matches(presented, options.ApiKey);

        if (!isAdmin && !isAgent)
        {
            await WriteUnauthorizedAsync(context, options);
            return;
        }

        var path = context.Request.Path;
        var isAgentRoute = path.StartsWithSegments(ClientsPrefix) || path.StartsWithSegments(PackagesPrefix);
        if (!isAgentRoute && !isAdmin)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Forbidden",
                message = "Management API routes require Security:AdminApiKey when it is configured."
            });
            return;
        }

        context.Items[ApiKeyOptions.RoleItemKey] = isAdmin ? ApiKeyOptions.RoleAdmin : ApiKeyOptions.RoleAgent;
        await _next(context);
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, ApiKeyOptions options)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Unauthorized",
            message = $"Missing or invalid API key. Send header '{options.ApiKeyHeader}'."
        });
    }

    private static bool TryGetPresentedKey(HttpRequest request, ApiKeyOptions options, out string key)
    {
        if (request.Headers.TryGetValue(options.ApiKeyHeader, out var headerValues))
        {
            key = headerValues.ToString();
            return !string.IsNullOrWhiteSpace(key);
        }

        var auth = request.Headers.Authorization.ToString();
        const string prefix = "ApiKey ";
        if (!string.IsNullOrWhiteSpace(auth)
            && auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            key = auth[prefix.Length..].Trim();
            return key.Length > 0;
        }

        key = string.Empty;
        return false;
    }

    private static bool Matches(string presented, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return false;
        }

        var left = Encoding.UTF8.GetBytes(presented.Trim());
        var right = Encoding.UTF8.GetBytes(expected.Trim());
        return left.Length == right.Length
            && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
