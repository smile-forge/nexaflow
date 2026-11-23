using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.Projects;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>The project folder's viewlets: its summary, kept as written, and its backlog by status.</summary>
[TestClass]
public class ProjectViewletViewModelTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup() => _root = TestTemp.CreateSubdirectory("proj-viewlet-").FullName;

    [TestMethod]
    [CoversNode("projects-ai-summary-viewlet")]
    public void ASavedSummary_IsWhatTheFolderShowsNextTime()
    {
        var ops = new ProjectOperations(new ProjectsConfig { ProjectDirectory = _root });
        var controller = Substitute.For<IViewletController>();

        var first = new AiSummaryViewletViewModel(ops, _root, controller);
        Assert.AreEqual(string.Empty, first.SavedText, "a folder with no summary yet");
        first.Save("Ships the drop router.");

        Assert.AreEqual("Ships the drop router.", first.SavedText);
        Assert.AreEqual("Ships the drop router.", new AiSummaryViewletViewModel(ops, _root, controller).SavedText);
    }

    [TestMethod]
    [CoversNode("projects-backlog-viewlet")]
    public void AFolderWithNoProject_CountsNothing_AndStillOpensTheProject()
    {
        var shell = Substitute.For<IShellServices>();
        var backlog = new BacklogStatusViewletViewModel(new ProjectsConfig { ProjectDirectory = _root }, shell, _root);

        Assert.AreEqual(0, backlog.Tallies.Count);

        backlog.OpenProject();
        shell.Received(1).OpenTab("ProjectDetail", Arg.Is<Dictionary<string, string>?>(p => p != null && p["path"] == _root),
                                  Arg.Any<Page?>(), Arg.Any<bool>());
    }
}
