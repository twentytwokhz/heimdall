using Heimdall.Application;

namespace Heimdall.Infrastructure.Policies.Caching;

/// <summary>
/// Derives the response cache key for cache-lookup/cache-store from the request method and URL.
/// Vary-by-header/query keying is a documented tier-1 boundary (not yet modelled).
/// </summary>
internal static class ResponseCacheKey
{
    public static string For(IPolicyContext context) => $"response:{context.Request.Method}:{context.Request.Url}";
}
