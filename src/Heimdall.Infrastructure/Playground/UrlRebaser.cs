using System.Text.RegularExpressions;

namespace Heimdall.Infrastructure.Playground;

/// <summary>
/// Rebases an imported request URL onto the local gateway origin: the scheme + authority are swapped for
/// the gateway's, the path/query/fragment kept verbatim (so the API route Heimdall matches on, and any
/// unresolved <c>{{vars}}</c>, survive untouched - no re-encoding). A relative <c>/path</c> URL gets the
/// origin prefixed.
/// </summary>
internal static partial class UrlRebaser
{
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*://[^/]+")]
    private static partial Regex OriginPrefix();

    public static string Rebase(string url, Uri gatewayOrigin, ICollection<string> notes)
    {
        var origin = gatewayOrigin.GetLeftPart(UriPartial.Authority);

        var match = OriginPrefix().Match(url);
        if (match.Success)
        {
            return origin + url[match.Length..];
        }

        if (url.StartsWith('/'))
        {
            return origin + url;
        }

        notes.Add($"URL left unchanged (no host to rebase after variable resolution): {url}");
        return url;
    }
}
