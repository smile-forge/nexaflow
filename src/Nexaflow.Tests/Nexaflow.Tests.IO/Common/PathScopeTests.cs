using System.Runtime.InteropServices;
using System.Text;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// What a user has put in front of the assistant, and whether a path falls inside it: a folder holds what is under it and
/// never what is above or beside it, a file given alone holds itself, and a path is read as the user reads it — full, its
/// "..", case, slashes, short names and prefixes worked out — so none of those reaches past what the user can see.
/// </summary>
[TestClass]
[CoversNode("ai-context-paths")]
public class PathScopeTests
{
    private static readonly PathScope Special = new([], [@"C:\temp\special"]);

    [TestMethod]
    [DataRow(@"C:\temp\special", DisplayName = "the folder itself")]
    [DataRow(@"C:\temp\special\other", DisplayName = "under it")]
    [DataRow(@"C:\temp\special\other\deeper\file.png", DisplayName = "deep under it")]
    [DataRow(@"c:/TEMP/Special/Other", DisplayName = "in another case, with forward slashes")]
    [DataRow(@"C:\temp\special\a\..\b", DisplayName = "a '..' that stays inside")]
    [DataRow(@"C:\temp\special\", DisplayName = "with a separator at its end")]
    [DataRow(@"\\?\C:\temp\special\other", DisplayName = "behind a long-path prefix")]
    [DataRow(@"\\.\C:\temp\special\other", DisplayName = "behind a device prefix on a drive")]
    public void AFolder_HoldsItselfAndWhatIsUnderIt(string path)
        => Assert.IsNotNull(Special.Resolve(path), path);

    [TestMethod]
    [DataRow(@"C:\temp", DisplayName = "the folder above")]
    [DataRow(@"C:\", DisplayName = "the drive")]
    [DataRow(@"C:\temp\specialist", DisplayName = "a sibling whose name starts the same")]
    [DataRow(@"C:\temp\special\..\secret.txt", DisplayName = "a '..' climbing out")]
    [DataRow(@"C:\temp\special\other\..\..\..\Windows", DisplayName = "several '..' climbing out")]
    [DataRow(@"D:\temp\special\other", DisplayName = "the same path on another drive")]
    [DataRow(@"temp\special\other", DisplayName = "a relative path")]
    [DataRow(@"\temp\special\other", DisplayName = "a path rooted on no drive")]
    [DataRow(@"C:temp\special\other", DisplayName = "a drive-relative path")]
    [DataRow(@"C:\temp\special\file.txt:hidden", DisplayName = "a named stream")]
    [DataRow(@"C:\temp\special\*.png", DisplayName = "a wildcard")]
    [DataRow(@"\\.\PhysicalDrive0", DisplayName = "a raw device")]
    [DataRow(@"\\?\Volume{12345678-1234-1234-1234-123456789abc}\temp\special", DisplayName = "a volume by its name")]
    [DataRow("", DisplayName = "nothing")]
    public void AFolder_HoldsNothingAboveOrBesideIt(string path)
        => Assert.IsNull(Special.Resolve(path), path);

    [TestMethod]
    public void AFileGivenAlone_HoldsItself_NotItsSiblingsOrItsFolder()
    {
        var scope = new PathScope([@"C:\pictures\cat.png"], []);

        Assert.AreEqual(@"C:\pictures\cat.png", scope.Resolve(@"c:\PICTURES\cat.PNG"), "itself, in any case");
        Assert.IsNull(scope.Resolve(@"C:\pictures\dog.png"), "a sibling");
        Assert.IsNull(scope.Resolve(@"C:\pictures"), "its folder");
        Assert.IsNull(scope.Resolve(@"C:\pictures\cat.png\more"), "nothing under a file");
    }

    [TestMethod]
    public void ANewFile_GoesOnlyIntoAFolderGiven_NeverBesideAFileGivenAlone()
    {
        var scope = new PathScope([@"C:\pictures\cat.png"], [@"C:\temp\special"]);

        Assert.AreEqual(@"C:\temp\special\made\dog.png", scope.ResolveInFolder(@"C:\temp\special\made\dog.png"));
        Assert.IsNull(scope.ResolveInFolder(@"C:\pictures\cat.png"), "a file given alone is no place to write");
        Assert.IsNull(scope.ResolveInFolder(@"C:\pictures\cat (2).png"), "nor is the folder it is in");
        Assert.IsNull(scope.ResolveInFolder(@"C:\temp\special\..\dog.png"), "a '..' climbing out");
        Assert.IsNull(scope.ResolveInFolder(@"dog.png"), "a relative path");
    }

    [TestMethod]
    public void ADriveRoot_HoldsTheWholeDrive_AndNoOther()
    {
        var scope = new PathScope([], [@"C:\"]);

        Assert.IsNotNull(scope.Resolve(@"C:\anything\at\all"));
        Assert.IsNull(scope.Resolve(@"D:\anything"));
    }

    [TestMethod]
    public void AShare_HoldsWhatIsUnderIt_ThroughEitherSpelling()
    {
        var scope = new PathScope([], [@"\\server\share\photos"]);

        Assert.IsNotNull(scope.Resolve(@"\\server\share\photos\2026\a.jpg"));
        Assert.IsNotNull(scope.Resolve(@"\\?\UNC\server\share\photos\2026\a.jpg"), "through the long-path spelling of a share");
        Assert.IsNull(scope.Resolve(@"\\server\share\private"));
        Assert.IsNull(scope.Resolve(@"\\server\other\photos"));
    }

    [TestMethod]
    public void Scopes_Join_AndNothingIsInsideNone()
    {
        var joined = Special.With(new PathScope([@"D:\notes.md"], []));

        Assert.IsNotNull(joined.Resolve(@"C:\temp\special\x"));
        Assert.IsNotNull(joined.Resolve(@"D:\notes.md"));
        Assert.IsTrue(PathScope.None.IsEmpty);
        Assert.IsNull(PathScope.None.Resolve(@"C:\temp\special\x"));
    }

    [TestMethod]
    [DataRow(@"C:\a\b", @"C:\a", true)]
    [DataRow(@"C:\a", @"C:\a\", true)]
    [DataRow(@"C:\ab", @"C:\a", false)]
    [DataRow(@"C:\A\B", @"c:\a", true)]
    [DataRow(@"C:\x", @"C:\", true)]
    public void IsWithin_MatchesAtSeparators_CaseIgnored(string path, string folder, bool within)
        => Assert.AreEqual(within, PathScope.IsWithin(path, folder));

    [TestMethod]
    public void AShortName_IsReadAsTheLongNameItStandsFor()
    {
        var root = Directory.CreateTempSubdirectory("nexaflow-scope-").FullName;
        try
        {
            var inside = Directory.CreateDirectory(Path.Combine(root, "A long folder name here")).FullName;
            var outside = Directory.CreateDirectory(Path.Combine(root, "Another long folder name")).FullName;
            var shortInside = ShortName(inside);
            if (shortInside is null || shortInside.Equals(inside, StringComparison.OrdinalIgnoreCase))
                Assert.Inconclusive("This volume makes no short names.");

            var scope = new PathScope([], [inside]);
            Assert.IsNotNull(scope.Resolve(Path.Combine(shortInside, "file.png")), "the short name of the folder given");
            Assert.IsNull(scope.Resolve(Path.Combine(ShortName(outside)!, "file.png")), "the short name of a sibling");
            Assert.IsNotNull(new PathScope([], [shortInside]).Resolve(Path.Combine(inside, "file.png")), "a folder given by its short name");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathNameW(string longPath, StringBuilder shortPath, uint size);

    private static string? ShortName(string path)
    {
        var buffer = new StringBuilder(1024);
        return GetShortPathNameW(path, buffer, (uint)buffer.Capacity) == 0 ? null : buffer.ToString();
    }
}
