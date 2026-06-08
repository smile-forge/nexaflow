using System.IO;

namespace Nexaflow.Tests.Core.Infrastructure;

/// <summary>
/// One family of generated sample files (e.g. all markdown fixtures).  A set owns a
/// sub-directory under the dataset root and the name→content map of files within it.
/// </summary>
internal interface ISampleSet
{
    /// <summary>Sub-directory under the dataset root, e.g. <c>"markdown"</c>.</summary>
    string SubDirectory { get; }

    /// <summary>File name → file content for every sample this set owns.</summary>
    IReadOnlyDictionary<string, string> Files { get; }
}

/// <summary>
/// Resolves and lazily materialises the shared, git-ignored sample-file dataset at
/// <c>&lt;repoRoot&gt;/test-samples</c>.
///
/// The fixtures are deliberately kept out of git (see <c>.gitignore</c>): they are generated
/// once from the catalog below and cached on disk, so a fresh checkout has no binary/sample
/// churn but the files are always available to the tests.  Generation is idempotent — a file
/// is only (re)written when missing or its content has drifted from the catalog, so deleting
/// the folder forces a clean rebuild on the next test run.
///
/// To add a new family of samples, implement <see cref="ISampleSet"/> and register it in
/// <see cref="Sets"/>.
/// </summary>
public static class TestSampleData
{
    public const string DirName = "test-samples";

    private static readonly ISampleSet[] Sets = [new MarkdownSamples()];

    private static readonly Lazy<string> RootLazy = new(EnsureAll);

    /// <summary>The dataset root (<c>&lt;repoRoot&gt;/test-samples</c>), generating any missing samples on first access.</summary>
    public static string Root => RootLazy.Value;

    /// <summary>Absolute path of a sample under the dataset root; ensures the dataset is generated first.</summary>
    public static string Path(params string[] segments) =>
        System.IO.Path.Combine([Root, .. segments]);

    /// <summary>Absolute paths of every file owned by the set for <paramref name="subDirectory"/>.</summary>
    public static IReadOnlyList<string> Files(string subDirectory)
    {
        var set = Sets.FirstOrDefault(s => s.SubDirectory == subDirectory)
            ?? throw new ArgumentException($"No sample set registered for '{subDirectory}'.", nameof(subDirectory));
        return set.Files.Keys
            .Select(name => System.IO.Path.Combine(Root, subDirectory, name))
            .ToList();
    }

    // ── Generation ──────────────────────────────────────────────────────────

    private static string EnsureAll()
    {
        string root = Locate();
        Directory.CreateDirectory(root);
        WriteIfChanged(System.IO.Path.Combine(root, "README.md"), ReadmeText);

        foreach (var set in Sets)
        {
            string subDir = System.IO.Path.Combine(root, set.SubDirectory);
            Directory.CreateDirectory(subDir);
            foreach (var (name, content) in set.Files)
                WriteIfChanged(System.IO.Path.Combine(subDir, name), content);
        }
        return root;
    }

    private static void WriteIfChanged(string path, string content)
    {
        // Normalise to LF so the catalog stays canonical regardless of how this source
        // file (or an editor) stored line endings — keeps regeneration churn-free.
        content = content.Replace("\r\n", "\n");
        if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == content) return;
        File.WriteAllText(path, content);
    }

    /// <summary>Walks up from the test binary to the repo root (the folder holding <c>Nexaflow.slnx</c>).</summary>
    private static string Locate()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Nexaflow.slnx")))
                return System.IO.Path.Combine(dir.FullName, DirName);
        }
        throw new InvalidOperationException(
            $"Could not locate the repo root (no Nexaflow.slnx above '{AppContext.BaseDirectory}').");
    }

    private const string ReadmeText =
        """
        # test-samples

        Generated, **git-ignored** test fixtures — a cached dataset, not source.

        These files are materialised on demand by the test suite
        (`Nexaflow.Tests.Core/Infrastructure/TestSampleData.cs`) and are intentionally
        excluded from git via `.gitignore`. They are safe to delete: the next test run
        regenerates anything that is missing.

        Sub-directories:

        - `markdown/` — sample markdown documents, one per supported Mermaid diagram type.
        """;
}
