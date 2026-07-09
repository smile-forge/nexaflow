using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>
/// Migration of a legacy (pre-2.0) <c>.project</c> forward to the v2 model: the tolerant loader maps it
/// in memory without writing, folding removed fields into the new markdown fields; an explicit upgrade
/// persists it as v2.
/// </summary>
[TestClass]
public class ProjectMigrationTests
{
    private const string LegacyJson = """
    {
      "Name": "Legacy",
      "Description": "Base description.",
      "Scope": "In scope: X.",
      "Objectives": ["Obj one", "Obj two"],
      "SolutionFile": "app.sln",
      "Backlog": [
        {
          "Id": "11111111-1111-1111-1111-111111111111",
          "Title": "Task A",
          "Notes": "some notes",
          "Status": "AwaitingDesign",
          "ImpPlan": "the impl plan",
          "TestPlan": "the test plan"
        }
      ]
    }
    """;

    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "legacy"));
        File.WriteAllText(Path.Combine(_root, "legacy", ".project"), LegacyJson);
    }

    [TestCleanup]
    public void Teardown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [TestMethod]
    public void TolerantLoad_MapsLegacyForward_InMemory()
    {
        var info = ProjectOperations.LoadProjectFile(Path.Combine(_root, "legacy"));

        Assert.AreEqual(1, info.SchemaVersion, "a legacy file loads flagged as needing upgrade");
        Assert.AreEqual("Legacy", info.Name);
        Assert.AreEqual("app.sln", info.SolutionFile);

        // Scope rolled into the description under a heading.
        StringAssert.Contains(info.Description, "Base description.");
        StringAssert.Contains(info.Description, "## Scope");
        StringAssert.Contains(info.Description, "In scope: X.");

        // Objectives → completion criteria (all "should").
        Assert.AreEqual(2, info.CompletionCriteria.Count);
        Assert.AreEqual("Obj one", info.CompletionCriteria[0].Text);
        Assert.AreEqual(CompletionStatus.Should, info.CompletionCriteria[0].Status);

        // Backlog item: status key preserved; notes/plans folded under headings.
        var item = info.Backlog.Single();
        Assert.AreEqual("Task A", item.Title);
        Assert.AreEqual("AwaitingDesign", item.StatusKey);
        StringAssert.Contains(item.Description, "## Notes");
        StringAssert.Contains(item.Description, "some notes");
        StringAssert.Contains(item.Description, "## Implementation Plan");
        StringAssert.Contains(item.Description, "the impl plan");
        StringAssert.Contains(item.Description, "## Test Plan");
        StringAssert.Contains(item.Description, "the test plan");
    }

    [TestMethod]
    public void TolerantLoad_DoesNotRewriteTheFile()
    {
        var before = File.ReadAllText(Path.Combine(_root, "legacy", ".project"));
        _ = ProjectOperations.LoadProjectFile(Path.Combine(_root, "legacy"));
        var after = File.ReadAllText(Path.Combine(_root, "legacy", ".project"));
        Assert.AreEqual(before, after, "loading a legacy project must never write to disk");
    }

    [TestMethod]
    public void UpgradeSchema_PersistsAsV2()
    {
        var ops = new ProjectOperations(new ProjectsConfig { ProjectDirectory = _root });
        ops.UpgradeSchema("legacy");

        var reloaded = ProjectOperations.LoadProjectFile(Path.Combine(_root, "legacy"));
        Assert.AreEqual(ProjectInfo.CurrentSchemaVersion, reloaded.SchemaVersion);
        StringAssert.Contains(File.ReadAllText(Path.Combine(_root, "legacy", ".project")), "\"SchemaVersion\": 2");

        // Folded data survives the round-trip.
        Assert.AreEqual(2, reloaded.CompletionCriteria.Count);
        StringAssert.Contains(reloaded.Backlog.Single().Description, "## Implementation Plan");
    }
}
