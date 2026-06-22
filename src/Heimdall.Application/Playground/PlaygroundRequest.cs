namespace Heimdall.Application;

/// <summary>One request header, name and value, as imported or replayed.</summary>
public sealed record PlaygroundHeader(string Name, string Value);

/// <summary>
/// One replayable request, parsed from an imported collection. The URL is rebased onto the local
/// gateway origin (host swapped, path + query kept) so it hits Heimdall; <see cref="OriginalUrl"/>
/// keeps the as-imported value for reference. <see cref="Notes"/> carries import-time flags
/// (unresolved <c>{{vars}}</c>, ignored scripts, ignored file fields) - surfaced, never silent.
/// </summary>
public sealed record PlaygroundRequest(
    string Name,
    string Method,
    string Url,
    string OriginalUrl,
    IReadOnlyList<PlaygroundHeader> Headers,
    string? Body,
    string? BodyMediaType,
    IReadOnlyList<string> Notes);

/// <summary>
/// The result of importing one collection file: a flat list of replayable requests plus
/// collection-level <see cref="Notes"/> (e.g. an environment file was/was not supplied).
/// </summary>
public sealed record CollectionImportResult(
    string Source,
    IReadOnlyList<PlaygroundRequest> Requests,
    IReadOnlyList<string> Notes);

/// <summary>
/// The outcome of replaying one <see cref="PlaygroundRequest"/> through the gateway.
/// <see cref="RequestId"/> correlates to the recorded trace (<c>GET /_apim/traces/{id}</c> or the
/// live SignalR feed).
/// </summary>
public sealed record PlaygroundResponse(
    Guid RequestId,
    int StatusCode,
    IReadOnlyList<PlaygroundHeader> Headers,
    string? Body);
