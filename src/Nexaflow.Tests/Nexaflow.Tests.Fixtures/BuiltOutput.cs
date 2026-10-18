namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// The executables this repository builds, found in the configuration (Debug/Release) the caller was itself built in, so
/// a journey or the sandbox agent drives the app from the build it came from.
/// </summary>
public static class BuiltOutput
{
    /// <summary>The app: <c>Nexaflow.exe</c> under <c>src/Nexaflow.Core/bin</c>.</summary>
    public static string App => Find("Nexaflow.Core", "Nexaflow.exe");

    /// <summary>The UI journey suite's runner, under <c>src/Nexaflow.Tests/Nexaflow.Tests.UIJourneys/bin</c>.</summary>
    public static string JourneyRunner =>
        Find(Path.Combine("Nexaflow.Tests", "Nexaflow.Tests.UIJourneys"), "Nexaflow.Tests.UIJourneys.exe");

    /// <summary>
    /// <paramref name="exeName"/> under <c>src/&lt;projectDir&gt;/bin</c>: the newest build in the caller's configuration, else the
    /// newest of any. Output lands under <c>bin\[&lt;Platform&gt;\]&lt;Config&gt;\&lt;tfm&gt;\[&lt;rid&gt;\]</c>, so its depth is not fixed.
    /// </summary>
    public static string Find(string projectDir, string exeName)
    {
        var bin = Path.Combine(TestSampleData.RepoRoot, "src", projectDir, "bin");
        if (!Directory.Exists(bin))
            throw new FileNotFoundException($"No build output at '{bin}'. Build the solution first.");

        // The caller's configuration is the folder just above its own TFM folder.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var builds = Directory.EnumerateFiles(bin, exeName, SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();

        return builds.FirstOrDefault(p => p.Split(Path.DirectorySeparatorChar).Contains(configuration))
            ?? builds.FirstOrDefault()
            ?? throw new FileNotFoundException($"Could not locate {exeName} under '{bin}'. Build the solution first.");
    }
}
