namespace Heimdall.Application;

/// <summary>One request header, name and value, as imported or replayed.</summary>
public sealed record PlaygroundHeader(string Name, string Value);

/// <summary>
/// One multipart/form-data field. A text field carries <see cref="TextValue"/>; a file field is a slot
/// (both null at import) whose <see cref="FileBase64"/> is filled with the chosen file's base64 bytes
/// before replay. The two are mutually exclusive: a file field never carries a TextValue.
/// </summary>
public sealed record PlaygroundFormField(string Name, string? TextValue = null, string? FileBase64 = null);

/// <summary>
/// A structured multipart/form-data body. The replay client assembles the wire multipart from these
/// fields (text parts inline, file parts decoded from base64), letting HttpClient own the boundary.
/// </summary>
public sealed record PlaygroundFormDataBody(IReadOnlyList<PlaygroundFormField> Fields);

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
    IReadOnlyList<string> Notes,
    PlaygroundFormDataBody? FormData = null);

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
