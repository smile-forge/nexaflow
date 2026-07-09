using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>POCO defaults and JSON round-trip for the v2 project model.</summary>
[TestClass]
public class ProjectModelTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [TestMethod]
    public void BacklogItem_Defaults_AreNotStarted_WithUniqueId()
    {
        var a = new BacklogItem();
        var b = new BacklogItem();
        Assert.AreEqual("NotStarted", a.StatusKey);
        Assert.AreEqual("", a.Description);
        Assert.AreNotEqual(Guid.Empty, a.Id);
        Assert.AreNotEqual(a.Id, b.Id, "each item should get its own id");
    }

    [TestMethod]
    public void ProjectInfo_Defaults_AreV2_WithEmptyCollections()
    {
        var p = new ProjectInfo();
        Assert.AreEqual(ProjectInfo.CurrentSchemaVersion, p.SchemaVersion);
        Assert.AreEqual("", p.Name);
        Assert.AreEqual(0, p.CompletionCriteria.Count);
        Assert.AreEqual(0, p.Backlog.Count);
        Assert.IsNull(p.SolutionFile);
        Assert.IsNull(p.LastUpdate);
    }

    [TestMethod]
    public void ProjectInfo_RoundTrips_WithStatusKeyAndCriterionStatusAsString()
    {
        var p = new ProjectInfo
        {
            Name        = "Demo",
            Description  = "d",
            CompletionCriteria =
            {
                new CompletionCriterion { Text = "ships", Status = CompletionStatus.Done },
                new CompletionCriterion { Text = "no bugs", Status = CompletionStatus.Faulted },
            },
            Backlog = { new BacklogItem { Title = "t", StatusKey = "AwaitingDesign", Description = "body" } },
        };

        var json = JsonSerializer.Serialize(p, Options);
        StringAssert.Contains(json, "\"SchemaVersion\": 2");
        StringAssert.Contains(json, "Done");           // criterion status written as name
        StringAssert.Contains(json, "AwaitingDesign"); // backlog status key is a plain string

        var rt = JsonSerializer.Deserialize<ProjectInfo>(json, Options)!;
        Assert.AreEqual("Demo", rt.Name);
        Assert.AreEqual(2, rt.CompletionCriteria.Count);
        Assert.AreEqual(CompletionStatus.Done, rt.CompletionCriteria[0].Status);
        Assert.AreEqual(1, rt.Backlog.Count);
        Assert.AreEqual("AwaitingDesign", rt.Backlog[0].StatusKey);
        Assert.AreEqual("body", rt.Backlog[0].Description);
    }
}
