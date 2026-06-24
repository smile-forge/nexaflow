using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// One generated sample file: its name plus the exact bytes to write. Two flavours —
/// <see cref="Text"/> for canonical UTF-8 text (line endings normalised to LF, compared as
/// text so regeneration never churns on CRLF drift) and <see cref="Raw"/> for byte-exact
/// fixtures whose every byte matters (BOMs, specific line endings, binary blobs).
/// </summary>
public sealed record SampleFile(string Name, byte[] Bytes, bool IsText)
{
    /// <summary>Canonical text sample — stored UTF-8, no BOM, LF line endings.</summary>
    public static SampleFile Text(string name, string content) =>
        new(name, Encoding.UTF8.GetBytes(content.Replace("\r\n", "\n")), IsText: true);

    /// <summary>Byte-exact sample — written verbatim, compared byte-for-byte.</summary>
    public static SampleFile Raw(string name, byte[] bytes) => new(name, bytes, IsText: false);
}

/// <summary>
/// One family of generated sample files (e.g. all markdown fixtures). A set owns a
/// sub-directory under the dataset root and the list of files within it.
/// </summary>
public interface ISampleSet
{
    /// <summary>Sub-directory under the dataset root, e.g. <c>"markdown"</c>.</summary>
    string SubDirectory { get; }

    /// <summary>Every sample this set owns.</summary>
    IReadOnlyList<SampleFile> Files { get; }
}

/// <summary>
/// Resolves and lazily materialises the shared, git-ignored sample-file dataset at
/// <c>&lt;repoRoot&gt;/test-samples</c>.
///
/// The fixtures are deliberately kept out of git (see <c>.gitignore</c>): they are generated
/// once from the catalog below and cached on disk, so a fresh checkout has no binary/sample
/// churn but the files are always available to the tests. Generation is idempotent — a file
/// is only (re)written when missing or its content has drifted from the catalog, so deleting
/// the folder forces a clean rebuild on the next test run.
///
/// To add a new family of samples, implement <see cref="ISampleSet"/> and register it in
/// <see cref="Sets"/>.
/// </summary>
public static class TestSampleData
{
    public const string DirName = "test-samples";

    private static readonly ISampleSet[] Sets =
    [
        new MarkdownSamples(),
        new TabularSamples(),
        new TextSamples(),
        new CodeSamples(),
        new JsonSamples(),
        new LogSamples(),
        new BinarySamples(),
        new ImageSamples(),
        new ArchiveSamples(),
    ];

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
        return set.Files
            .Select(f => System.IO.Path.Combine(Root, subDirectory, f.Name))
            .ToList();
    }

    // ── Generation ──────────────────────────────────────────────────────────

    private static string EnsureAll()
    {
        string root = Locate();
        Directory.CreateDirectory(root);
        WriteIfChanged(new SampleFile("README.md", Encoding.UTF8.GetBytes(ReadmeText.Replace("\r\n", "\n")), IsText: true), root);

        foreach (var set in Sets)
        {
            string subDir = System.IO.Path.Combine(root, set.SubDirectory);
            Directory.CreateDirectory(subDir);
            foreach (var file in set.Files)
                WriteIfChanged(file, subDir);
        }
        return root;
    }

    private static void WriteIfChanged(SampleFile file, string directory)
    {
        var path = System.IO.Path.Combine(directory, file.Name);
        if (file.IsText)
        {
            // Compare as LF-normalised text so an editor (or git autocrlf) re-storing this
            // source with CRLF never forces a rewrite of the on-disk fixture.
            var content = Encoding.UTF8.GetString(file.Bytes);
            if (File.Exists(path) && File.ReadAllText(path).Replace("\r\n", "\n") == content) return;
            File.WriteAllText(path, content);
        }
        else
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(file.Bytes)) return;
            File.WriteAllBytes(path, file.Bytes);
        }
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
        (`Nexaflow.Tests.Fixtures/TestSampleData.cs`) and are intentionally excluded from git
        via `.gitignore`. They are safe to delete: the next test run regenerates anything that
        is missing.

        Sub-directories:

        - `markdown/` — sample markdown documents, one per supported Mermaid diagram type.
        - `tabular/`  — CSV/TSV variations (separators, quoting, headers, column types).
        - `text/`     — plain-text files: short/long, varied BOMs and line endings.
        - `code/`     — source/config files for the editor's syntax highlighting (cs/js/ts/py/ini/xml/css/html).
        - `json/`     — JSON objects, arrays and a large array for seek-by-item windowing.
        - `logs/`     — timestamped log files, short and long (tail-first streaming).
        - `binary/`   — binary blobs for the hex viewer (random, zeros, mixed, image header).
        - `images/`   — solid-colour BMPs in varied aspect ratios for the image viewer.
        """;
}
