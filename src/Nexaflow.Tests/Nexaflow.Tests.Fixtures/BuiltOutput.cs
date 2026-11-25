namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// The executables this repository builds, found in the configuration (Debug/Release) the caller was itself built in, so
/// a journey or the sandbox agent drives the app from the build it came from — and, run from a snapshot of the build, from
/// that snapshot.
/// </summary>
public static class BuiltOutput
{
    /// <summary>
    /// Names the manifest at the root of a snapshot: build output folders copied with their checkout-relative paths, which a
    /// binary run from inside the copy finds its sibling builds in. The Windows Sandbox tool takes one when it starts a
    /// session, so what a run there executes is the build as it stood then, whatever is rebuilt in the checkout meanwhile.
    /// </summary>
    public const string SnapshotManifestName = "built-output-snapshot.json";

    /// <summary>
    /// Where builds are looked for: the snapshot the caller itself runs from, so that the app a journey starts comes from the
    /// same copy as the journey; otherwise the repository root.
    /// </summary>
    public static string Root => RootLazy.Value;

    private static readonly Lazy<string> RootLazy = new(LocateRoot);

    /// <summary>The app: <c>Nexaflow.exe</c> under <c>src/Nexaflow.Core/bin</c>.</summary>
    public static string App => Find("Nexaflow.Core", "Nexaflow.exe");

    /// <summary>The UI journey suite's runner, under <c>src/Nexaflow.Tests/Nexaflow.Tests.UIJourneys/bin</c>.</summary>
    public static string JourneyRunner =>
        Find(Path.Combine("Nexaflow.Tests", "Nexaflow.Tests.UIJourneys"), "Nexaflow.Tests.UIJourneys.exe");

    /// <summary>
    /// <paramref name="exeName"/> under <c>src/&lt;projectDir&gt;/bin</c> of <see cref="Root"/>: the newest build in the caller's
    /// configuration, else the newest of any. Output lands under <c>bin\[&lt;Platform&gt;\]&lt;Config&gt;\&lt;tfm&gt;\[&lt;rid&gt;\]</c>,
    /// so its depth is not fixed.
    /// </summary>
    public static string Find(string projectDir, string exeName)
    {
        var bin = Path.Combine(Root, "src", projectDir, "bin");
        var remedy = Root == TestSampleData.RepoRoot ? "Build the solution first." : "The snapshot this runs from did not copy it.";
        if (!Directory.Exists(bin))
            throw new FileNotFoundException($"No build output at '{bin}'. {remedy}");

        // The caller's configuration is the folder just above its own TFM folder.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var builds = Directory.EnumerateFiles(bin, exeName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        return builds.FirstOrDefault(p => p.Split(Path.DirectorySeparatorChar).Contains(configuration))
            ?? builds.FirstOrDefault()
            ?? throw new FileNotFoundException($"Could not locate {exeName} under '{bin}'. {remedy}");
    }

    /// <summary>The nearest folder above the caller holding a snapshot manifest; the repository root when there is none.</summary>
    private static string LocateRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, SnapshotManifestName)))
                return dir.FullName;
        return TestSampleData.RepoRoot;
    }
}
