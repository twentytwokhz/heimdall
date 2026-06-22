namespace Heimdall.Tests.Fixtures;

/// <summary>Locates repo-relative paths from the test output directory (walks up to Heimdall.slnx).</summary>
internal static class RepoPaths
{
    public static string SamplesDir() => Path.Combine(RepoRoot(), "samples");

    public static string ApiOpsLayoutDir() => Path.Combine(RepoRoot(), "samples", "apiops-layout");

    public static string ConformanceCasesDir() =>
        Path.Combine(RepoRoot(), "tests", "Heimdall.Tests", "Conformance", "Cases");

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Heimdall.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new InvalidOperationException("Could not locate the repo root (Heimdall.slnx).");
    }
}
