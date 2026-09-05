using System;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Whether a query can say something definite about its own answer. Silence about freshness is what makes a
/// caller assume the worst and go and rebuild something that takes ninety seconds, so a query has to state
/// either "current" or "these files have moved on".
/// </summary>
[TestClass]
[CoversNode("graph-archive")]
public class GraphFreshnessTests
{
    private string _root = "";
    private string _graphFile = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-freshness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "Proj"));
        _graphFile = Path.Combine(_root, "graph.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    /// <summary>Writes a file and returns its repo-relative path.</summary>
    private string File_(string name, string text = "public class C { }\n")
    {
        var rel = $"src/Proj/{name}";
        System.IO.File.WriteAllText(Path.Combine(_root, "src", "Proj", name), text);
        return rel;
    }

    /// <summary>Stamps the graph as built now — the baseline every file is compared against.</summary>
    private void BuiltNow()
    {
        System.IO.File.WriteAllText(_graphFile, "{}");
        System.IO.File.SetLastWriteTimeUtc(_graphFile, DateTime.UtcNow.AddSeconds(1));
    }

    private static void TouchedSince(string full) =>
        System.IO.File.SetLastWriteTimeUtc(full, DateTime.UtcNow.AddMinutes(5));

    [TestMethod]
    public void SaysCurrent_WhenNothingHasMoved()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");
        BuiltNow();

        var report = GraphFreshness.Check([proj, code], _root, _graphFile);

        Assert.IsTrue(report.IsCurrent, report.Summary());
        StringAssert.Contains(report.Summary(), "current");
    }

    [TestMethod]
    public void NoticesAFileEditedSinceTheGraphWasBuilt()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");
        BuiltNow();
        TouchedSince(Path.Combine(_root, "src", "Proj", "C.cs"));

        var report = GraphFreshness.Check([proj, code], _root, _graphFile);

        CollectionAssert.Contains(report.Changed.ToList(), code);
        Assert.IsFalse(report.IsCurrent);
        StringAssert.Contains(report.Summary(), "--refresh");
    }

    /// <summary>
    /// A file the graph knows and this tree does not is <b>reported, never acted on</b>. graph.json is
    /// shared, and a branch that runs a build publishes its own files into it — so this is as likely to be
    /// a parallel session's work in progress as a deletion, and nothing here can tell them apart. Treating
    /// it as staleness and "refreshing" it would delete someone else's feature from the shared graph.
    /// </summary>
    [TestMethod]
    public void AFileNotInThisTree_IsReportedButNeverQueuedForRefresh()
    {
        var proj  = File_("Proj.csproj", "<Project />\n");
        var mine  = File_("C.cs");
        var other = "src/Nexaflow.Maths/Latex/TexNode.cs";      // another branch's work in progress
        BuiltNow();

        var report = GraphFreshness.Check([proj, mine, other], _root, _graphFile);

        CollectionAssert.Contains(report.Absent.ToList(), other);
        CollectionAssert.DoesNotContain(report.Stale.ToList(), other,
            "refreshing an absent file can only prune it, and that would destroy a parallel branch's work");
        Assert.IsTrue(report.IsCurrent, "the graph knowing something extra does not make this answer wrong");
        StringAssert.Contains(report.Summary(), "another branch's work");
    }

    /// <summary>
    /// Additions are the half that needs a walk, and the walk is scoped to the directories the solution's
    /// projects live in — a pinned submodule cannot gain a file without a deliberate bump, and scanning for
    /// one costs four times as much as everything else put together.
    /// </summary>
    [TestMethod]
    public void NoticesANewFileBesideAProject()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");
        BuiltNow();
        var added = File_("New.cs");

        var report = GraphFreshness.Check([proj, code], _root, _graphFile);

        CollectionAssert.Contains(report.Added.ToList(), added);
    }

    [TestMethod]
    public void IgnoresANewFileFarFromAnyProject()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");
        BuiltNow();

        Directory.CreateDirectory(Path.Combine(_root, "external", "vendor"));
        System.IO.File.WriteAllText(Path.Combine(_root, "external", "vendor", "Vendor.cs"), "class V { }\n");

        var report = GraphFreshness.Check([proj, code], _root, _graphFile);

        Assert.AreEqual(0, report.Added.Count,
            "a file in no project directory is submodule corpus, not something to warn about");
    }

    [TestMethod]
    public void StaleListsEverythingWorthReReading()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var kept = File_("Kept.cs");
        var gone = File_("Gone.cs");
        BuiltNow();

        TouchedSince(Path.Combine(_root, "src", "Proj", "Kept.cs"));
        System.IO.File.Delete(Path.Combine(_root, "src", "Proj", "Gone.cs"));
        var added = File_("Added.cs");

        var stale = GraphFreshness.Check([proj, kept, gone], _root, _graphFile).Stale;

        CollectionAssert.AreEquivalent(new[] { kept, added }, stale.ToList(),
            "what is worth re-reading is what exists — an absent file is reported, not refreshed");
    }

    [TestMethod]
    public void ClaimsNothing_WhenNoGraphHasBeenBuilt()
    {
        var report = GraphFreshness.Check([File_("C.cs")], _root, Path.Combine(_root, "absent.json"));

        Assert.IsFalse(report.Available);
        Assert.IsFalse(report.IsCurrent, "unknown is not the same as current");
        StringAssert.Contains(report.Summary(), "not built");
    }

    /// <summary>From a worktree most of the difference is the branch, and saying so is what stops a large,
    /// permanent-looking number being ignored.</summary>
    [TestMethod]
    public void ExplainsItselfWhenTheGraphCameFromAnotherBranch()
    {
        var proj = File_("Proj.csproj", "<Project />\n");
        var code = File_("C.cs");
        BuiltNow();
        TouchedSince(Path.Combine(_root, "src", "Proj", "C.cs"));

        var summary = GraphFreshness.Check([proj, code], _root, _graphFile, otherBranch: true).Summary();

        StringAssert.Contains(summary, "different branch");
    }
}
