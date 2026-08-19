using Nexaflow.Tests.UIJourneys.Infrastructure;

using Nexaflow.Tests.Fixtures;
using System.IO;

namespace Nexaflow.Tests.Features.Projects.UI;

/// <summary>
/// The Projects UI journey — one launch covering both surfaces the feature presents: the backlog-status
/// folder viewlet in the file explorer, and the Projects tab's list / bucket / detail flow.
/// <para>
/// It runs against a seeded workspace (Projects enabled, a real project directory) and starts on the
/// default file-browser tab, so the viewlet is reachable before anything navigates away; the Projects tab
/// is then opened from the ribbon. The order matters — <c>NavigateFileBrowserTo</c> needs the file browser
/// on screen, and once the Projects tab is active it is not.
/// </para>
/// <para>
/// The <b>disabled</b> state is deliberately not a journey. Projects is off in a fresh config, so a
/// journey for it spent a full app launch to assert one placeholder and then waited out a timeout per
/// enabled-only control proving absences the view had collapsed out of the tree by construction. What it
/// actually verified — that a disabled feature lists nothing — is a view-model question, and
/// <c>ProjectsViewModelTests.Disabled_ShowsNoProjects</c> already answers it in milliseconds.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("projects")]
public class ProjectsJourneyTests : UiJourneyTestBase
{
    // No LaunchTabKind: the default file-browser tab is where the viewlet half of the journey starts.
    /// <summary>Stages the prebuilt Projects fixture into this test's isolated config dir, so the feature
    /// launches enabled with real projects to show. Copying, not writing: the config's shape belongs to the
    /// feature, and a journey that knew it would be asserting against something it had authored.</summary>
    /// <summary>Stages the prebuilt workspace config into this test's throwaway config dir, so the feature
    /// launches enabled. Only the config is copied — it points at the corpus's project folders, which stay
    /// where they were built.</summary>
    protected override void SeedConfig(string configDir) =>
        RequiredFixture.CopyInto(Path.Combine("projects", "Contexts"), Path.Combine(configDir, "Contexts"),
                                 "ProjectsUiFixtureTests in Nexaflow.Tests.Features");

    [TestMethod]
    [CoversNode("projects-ui")]
    [CoversNode("projects-viewlets")]
    [CoversNode("projects-backlog-viewlet")]
    public void Projects_ViewletListBucketsAndDetail_RespondInOnePass()
    {
        // ── Explorer surface: a folder holding a .project file grows the backlog-status viewlet ──
        NavigateFileBrowserTo(RequiredFixture.Folder(Path.Combine("projects", "_projects", "Alpha"), "ProjectsUiFixtureTests in Nexaflow.Tests.Features"));

        Assert.IsNotNull(WaitForId("Projects_BacklogViewlet", 10),
            "The backlog-status viewlet did not appear for a folder containing a .project file.");
        CheckPresent("Backlog viewlet", "Projects_BacklogViewlet");

        // ── Tab surface: open Projects from the ribbon ──
        Assert.IsNotNull(TryOpenTabWithElement("ProjectsView"), "ProjectsView did not open from the ribbon.");

        // Enabled → the list + bucket tabs render (not the disabled placeholder).
        CheckPresent("Project list", "Projects_List");
        CheckPresent("Projects bucket", "Projects_BucketProjects");
        CheckPresent("Shelf bucket", "Projects_BucketShelf");
        CheckPresent("Archives bucket", "Projects_BucketArchive");

        // Projects bucket, Alpha auto-selected.
        CheckPresent("Open Project", "Projects_OpenProject");
        // Open Files and Archive/Shelf are presence-only on purpose: the first navigates away mid-journey,
        // and the other two move Alpha out of the bucket every later step depends on. Their behaviour is
        // covered headlessly by ProjectOperationsTests.
        CheckPresent("Open Files", "Projects_OpenFiles");
        CheckPresent("Archive action", "Projects_Archive");
        CheckPresent("Shelf action", "Projects_Shelf");

        // Each bucket switch has to actually re-bind the list, so assert the action only that bucket
        // offers: shelf → Reactivate, projects → Archive. A bucket button that changed nothing would
        // satisfy a bare invoke.
        CheckDoes("Switch to Shelf", "Projects_BucketShelf",
                  () => WaitForId("Projects_Reactivate", 5) is not null);
        CheckDoes("Switch to Projects", "Projects_BucketProjects",
                  () => WaitForId("Projects_Archive", 5) is not null);

        // Open Alpha into the tabbed detail view — the effect IS the detail view.
        CheckDoes("Open Project", "Projects_OpenProject",
                  () => WaitForId("ProjectDetailView", 10) is not null);
        CheckPresent("Project Details tab", "Projects_Tab_Details");

        CheckDoes("Open Backlog tab", "Projects_Tab_Backlog",
                  () => WaitForId("Projects_Detail_NewTodoTitle", 5) is not null);

        // Actually add a backlog item rather than noting the button exists: type a title, press Add, and
        // require the row to show up. This is the one write path the journey can exercise without
        // disturbing the seeded state the checks above depend on.
        const string added = "Journey task";
        SetText("Projects_Detail_NewTodoTitle", added);
        CheckDoes("Add todo", "Projects_Detail_AddTodo", () => WaitForName(added, 5) is not null);

        // Selecting a row reveals the per-item controls.
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
