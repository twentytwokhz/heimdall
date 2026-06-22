namespace Heimdall.Infrastructure.ApiOpsLoader;

/// <summary>
/// Folder and file names of the APIOps v6 extractor layout, plus a guard that fails loud on the
/// pre-v6 (space-named) layout. Centralizes the on-disk contract so the loader reads it in one place.
/// </summary>
internal static class ApiOpsLayout
{
    public const string ApisDir = "apis";
    public const string ProductsDir = "products";
    public const string NamedValuesDir = "namedValues";
    public const string BackendsDir = "backends";
    public const string PolicyFragmentsDir = "policyFragments";
    public const string OperationsDir = "operations";
    public const string GlobalPolicyDir = "policies";

    public const string ApiInformationFile = "apiInformation.json";
    public const string ProductInformationFile = "productInformation.json";
    public const string NamedValueInformationFile = "namedValueInformation.json";
    public const string BackendInformationFile = "backendInformation.json";
    public const string PolicyFile = "policy.xml";
    public const string OverridesFile = "heimdall.overrides.json";

    private static readonly string[] SpecFileNames =
        ["specification.yaml", "specification.yml", "specification.json"];

    // APIOps v4/v5 wrote space-named folders; v6 switched to camelCase. If we see the old shape, the
    // rest of the loader would silently find nothing, so fail loud with a clear remediation instead.
    private static readonly string[] LegacyFolders = ["named values", "policy fragments"];

    public static void EnsureV6Layout(string root)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"APIOps source folder '{root}' does not exist.");
        }

        foreach (var legacy in LegacyFolders)
        {
            if (Directory.Exists(Path.Combine(root, legacy)))
            {
                throw new InvalidOperationException(
                    $"APIOps folder '{root}' uses a pre-v6 layout (found '{legacy}/'). Heimdall targets APIOps v6 " +
                    "(camelCase folders: namedValues/, policyFragments/). Re-extract with APIOps v6.");
            }
        }
    }

    /// <summary>The OpenAPI spec file for an API directory (yaml preferred), or null if absent.</summary>
    public static string? FindSpecFile(string apiDir) =>
        SpecFileNames.Select(name => Path.Combine(apiDir, name)).FirstOrDefault(File.Exists);
}
