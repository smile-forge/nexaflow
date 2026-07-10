using Nexaflow.Tests.Features.UI.Infrastructure;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Projects.UI;

/// <summary>
/// Enabled-state UI journey for the Projects tab: seeds a workspace config with Projects enabled and a
/// real project directory, then exercises the bucket tabs, the archive/shelf/reactivate actions, and
/// opening a project into the tabbed detail view (with the backlog editor + themed status dropdown).
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("projects")]
public class ProjectsEnabledJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Projects";
    protected override void SeedConfig(string configDir) => ProjectsUiSeed.Write(configDir);

    [TestMethod]
    public void Projects_Enabled_ListBucketsAndDetail_RespondInOnePass()
    {
        var view = WaitForId("ProjectsView", 15);
        Assert.IsNotNull(view, "ProjectsView did not open via --openTab Projects.");

        // Enabled → the list + bucket tabs render (not the disabled placeholder).
        CheckPresent("Project list", "Projects_List");
        CheckPresent("Projects bucket", "Projects_BucketProjects");
        CheckPresent("Shelf bucket", "Projects_BucketShelf");
        CheckPresent("Archives bucket", "Projects_BucketArchive");

        // Projects bucket, Alpha auto-selected → Open + Archive/Shelf actions present.
        CheckPresent("Open Project", "Projects_OpenProject");
        CheckPresent("Open Files", "Projects_OpenFiles");
        CheckPresent("Archive action", "Projects_Archive");
        CheckPresent("Shelf action", "Projects_Shelf");

        // Switch to the Shelf bucket → the shelved project shows a Reactivate action.
        CheckInvoke("Switch to Shelf", "Projects_BucketShelf");
        CheckPresent("Reactivate action", "Projects_Reactivate");

        // Back to Projects, then open Alpha into the tabbed detail view.
        CheckInvoke("Switch to Projects", "Projects_BucketProjects");
        CheckInvoke("Open Project", "Projects_OpenProject");

        Assert.IsNotNull(WaitForId("ProjectDetailView", 10), "Project detail view did not open.");
        CheckPresent("Project Details tab", "Projects_Tab_Details");
        CheckPresent("Backlog tab", "Projects_Tab_Backlog");

        // Backlog tab: add-row is always present; selecting the seeded item reveals the status dropdown + AI buttons.
        CheckInvoke("Open Backlog tab", "Projects_Tab_Backlog");
        CheckPresent("New todo box", "Projects_Detail_NewTodoTitle");
        CheckPresent("Add todo", "Projects_Detail_AddTodo");

        var item = WaitForName("First task", 5);
        if (item is not null)
        {
            item.Click();
            System.Threading.Thread.Sleep(250);
            CheckPresent("Status dropdown", "Projects_Detail_StatusCombo");
            CheckPresent("Task to AI", "Projects_Detail_TaskToAi");
            CheckPresent("Plan with AI", "Projects_Detail_PlanWithAi");
        }

        AssertJourney();
    }
}
