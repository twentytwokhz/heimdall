using Heimdall.Application;
using Heimdall.Infrastructure.Context;
using Heimdall.Infrastructure.Expressions;

namespace Heimdall.Tests.Fixtures;

/// <summary>Builds fabricated <see cref="PolicyContext"/>s for per-policy unit tests.</summary>
public static class PolicyContexts
{
    public static PolicyContext For(
        PolicySection section,
        string method = "GET",
        string url = "http://localhost/catalog/items",
        string body = "",
        string? ip = null,
        IExpressionEvaluator? expressions = null,
        INamedValues? namedValues = null)
        => new()
        {
            Request = new EmuRequest
            {
                Method = method,
                Url = new Uri(url),
                Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase),
                Body = new HttpEmuBody(body),
                IpAddress = ip,
            },
            Api = new ApiInfo("acme", "Acme Platform API", "/catalog"),
            Operation = new OperationInfo("listCatalogItems", "GET", "/items"),
            Expressions = expressions ?? new RoslynExpressionEvaluator(),
            NamedValues = namedValues!,
            CurrentSection = section,
        };
}
