using System.IO;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.UIJourneys.Infrastructure;

/// <summary>
/// A journey opens files; it does not make them. Anything that has to be constructed rather than clicked
/// is built elsewhere and simply looked up here — present, and the test runs; absent, and it reports
/// inconclusive naming what is missing and what produces it.
/// <para>
/// This is what keeps the suite black-box. Building a fixture in-place meant referencing the very assembly
/// the journey is meant to drive through its UI (the Projects config writer, the git library, the disk-image
/// writer), so the test both prepared and asserted the same code, and the suite grew compile-time
/// dependencies on the product it is supposed to know only as a running application.
/// </para>
/// An absent fixture is deliberately not a failure: it means this machine has not built the corpus, which
/// says nothing about whether the app works.
/// </summary>
public static class RequiredFixture
{
    /// <summary>Where the built corpus lives — the <c>ui</c> subtree of the git-ignored sample dataset.</summary>
    public static string Root => TestSampleData.Path("ui");

    /// <summary>A fixture folder, or inconclusive if the corpus has not been built on this machine.</summary>
    public static string Folder(string name, string builtBy)
    {
        var path = Path.Combine(Root, name);
        if (!Directory.Exists(path)) Missing(path, builtBy);
        return path;
    }

    /// <summary>A fixture file, or inconclusive if the corpus has not been built on this machine.</summary>
    public static string File(string relativePath, string builtBy)
    {
        var path = Path.Combine(Root, relativePath);
        if (!System.IO.File.Exists(path)) Missing(path, builtBy);
        return path;
    }

    /// <summary>
    /// Copies a fixture folder into <paramref name="destination"/> — for the fixtures the app reads from its
    /// own config dir, which is per-test and throwaway. Staging a built tree, not authoring one.
    /// </summary>
    public static void CopyInto(string name, string destination, string builtBy)
    {
        var source = Folder(name, builtBy);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, destination));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            System.IO.File.Copy(file, file.Replace(source, destination), overwrite: true);
    }

    private static void Missing(string path, string builtBy) =>
        Assert.Inconclusive(
            $"The UI fixture '{path}' has not been built on this machine, so there is nothing to open. "
            + $"It is produced by {builtBy}. Build the corpus and re-run.");
}
