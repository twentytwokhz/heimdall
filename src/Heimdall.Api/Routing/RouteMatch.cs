using Heimdall.Domain;

namespace Heimdall.Api.Routing;

/// <summary>A matched API operation plus the URI-template values captured from the request path.</summary>
public sealed record RouteMatch(Heimdall.Domain.Api Api, Operation Operation, IReadOnlyDictionary<string, string> TemplateValues);
