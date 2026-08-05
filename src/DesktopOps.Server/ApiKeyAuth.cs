using System.Security.Cryptography;
using System.Text;

namespace DesktopOps.Server;

/// <summary>Shared API key settings for client and management API calls.</summary>
public sealed class ApiKeyOptions
{
    public const string SectionName = "Security";

    /// <summary>Default header name for the API key.</summary>
    public const string DefaultHeaderName = "X-DesktopOps-Key";

    /// <summary>
    /// Shared secret. When empty, API key checks are disabled (local demo).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>HTTP header that carries the API key.</summary>
    public string ApiKeyHeader { get; set; } = DefaultHeaderName;

    /// <summary>True when a non-empty API key is configured.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Requires <see cref="ApiKeyOptions.ApiKey"/> on <c>/api/*</c> when configured.</summary>
public sealed class ApiKeyMiddleware
{
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

        if (!TryGetPresentedKey(context.Request, options, out var presented)
            || !FixedTimeEquals(presented, options.ApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Unauthorized",
                message = $"Missing or invalid API key. Send header '{options.ApiKeyHeader}'."
            });
            return;
        }

        await _next(context);
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

    private static bool FixedTimeEquals(string presented, string expected)
    {
        var left = Encoding.UTF8.GetBytes(presented.Trim());
        var right = Encoding.UTF8.GetBytes(expected.Trim());
        return left.Length == right.Length
            && CryptographicOperations.FixedTimeEquals(left, right);
    }
}
