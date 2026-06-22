namespace Heimdall.Application;

/// <summary>
/// Parses a request collection (Postman v2.1 export or a <c>.http</c> file) into replayable
/// <see cref="PlaygroundRequest"/>s. Replay-only: scripts are parsed but never run, and an unsupported
/// format or schema version fails loudly (it never silently "supports" the input).
/// </summary>
/// <remarks>
/// One implementation per format; <see cref="CanImport"/> lets a caller pick the right one (or fail
/// loud when none match). <see cref="Import"/> throws on a recognised-but-invalid input - e.g. a JSON
/// document that is not a Postman v2.1 collection.
/// </remarks>
public interface ICollectionImporter
{
    /// <summary>True if this importer handles the given file (by extension, falling back to content).</summary>
    bool CanImport(string fileName, string content);

    /// <summary>
    /// Parses <paramref name="content"/> into replayable requests, rebasing each URL onto
    /// <paramref name="gatewayOrigin"/>. <paramref name="environmentContent"/> is an optional Postman
    /// environment export used to resolve <c>{{vars}}</c> (ignored by formats that have no environments).
    /// </summary>
    CollectionImportResult Import(string fileName, string content, string? environmentContent, Uri gatewayOrigin);
}
