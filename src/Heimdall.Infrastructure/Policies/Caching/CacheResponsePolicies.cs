using System.Globalization;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;

namespace Heimdall.Infrastructure.Policies.Caching;

/// <summary>
/// cache-lookup: on a cache hit, replays the cached response and short-circuits the pipeline so the
/// backend is never called. The companion cache-store populates the cache in outbound.
/// </summary>
public sealed class CacheLookupPolicy(ICacheStore cache) : IPolicy
{
    public string ElementName => "cache-lookup";
    public PolicySection Sections => PolicySection.Inbound;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (cache.TryGet(ResponseCacheKey.For(context), out var cached) && cached is CachedResponse response)
        {
            context.Response.StatusCode = response.StatusCode;
            context.Response.Headers = new Dictionary<string, string[]>(response.Headers, StringComparer.OrdinalIgnoreCase);
            context.Response.Body = response.Body is null ? null : new HttpEmuBody(response.Body);
            context.ShortCircuited = true;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// cache-store: caches the response for <c>duration</c> seconds under the request-derived key.
/// The cached headers are replayed verbatim (no Age/Date/Cache-Control adjustment): a tier-1 boundary.
/// </summary>
public sealed class CacheStorePolicy(ICacheStore cache) : IPolicy
{
    public string ElementName => "cache-store";
    public PolicySection Sections => PolicySection.Outbound;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("duration", out var raw) ||
            !int.TryParse(raw, CultureInfo.InvariantCulture, out var seconds))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<cache-store> requires a numeric 'duration' attribute.");
        }

        var snapshot = new CachedResponse(
            context.Response.StatusCode,
            new Dictionary<string, string[]>(context.Response.Headers, StringComparer.OrdinalIgnoreCase),
            context.Response.Body?.As<string>());

        cache.Set(ResponseCacheKey.For(context), snapshot, TimeSpan.FromSeconds(seconds));
        return ValueTask.CompletedTask;
    }
}
