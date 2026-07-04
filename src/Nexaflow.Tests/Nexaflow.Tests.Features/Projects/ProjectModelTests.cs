using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>POCO defaults and JSON round-trip for the project model (<see cref="ProjectInfo"/> / <see cref="BacklogItem"/>).</summary>
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
        Assert.AreEqual(BacklogStatus.NotStarted, a.Status);
        Assert.AreNotEqual(Guid.Empty, a.Id);
        Assert.AreNotEqual(a.Id, b.Id, "each item should get its own id");
    }

    [TestMethod]
    public void ProjectInfo_Defaults_AreEmptyCollectionsAndStrings()
    {
        var p = new ProjectInfo();
        Assert.AreEqual("", p.Name);
        Assert.AreEqual(0, p.Objectives.Count);
        Assert.AreEqual(0, p.Backlog.Count);
        Assert.IsNull(p.SolutionFile);
        Assert.IsNull(p.LastUpdate);
    }

    [TestMethod]
    public void ProjectInfo_RoundTrips_WithEnumAsString()
    {
        var p = new ProjectInfo
        {
            Name = "Demo",
            Description = "d",
            Objectives = { "o1", "o2" },
            Backlog = { new BacklogItem { Title = "t", Status = BacklogStatus.AwaitingDesign } },
        };

        var json = JsonSerializer.Serialize(p, Options);
        StringAssert.Contains(json, "AwaitingDesign");   // enum written as name, not number

        var rt = JsonSerializer.Deserialize<ProjectInfo>(json, Options)!;
        Assert.AreEqual("Demo", rt.Name);
        CollectionAssert.AreEqual(new[] { "o1", "o2" }, rt.Objectives);
        Assert.AreEqual(1, rt.Backlog.Count);
        Assert.AreEqual(BacklogStatus.AwaitingDesign, rt.Backlog[0].Status);
        Assert.AreEqual("t", rt.Backlog[0].Title);
    }
}
