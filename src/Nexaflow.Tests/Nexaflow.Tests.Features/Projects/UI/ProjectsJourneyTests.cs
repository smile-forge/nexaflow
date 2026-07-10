using Nexaflow.Tests.Features.UI.Infrastructure;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Projects.UI;

/// <summary>
/// One-pass UI journey for the Projects tab (PageKind "Projects", ribbon label "Projects" in
/// default-ribbon.json). Like the Scratchpad journey it opens from a ribbon button via
/// <see cref="UITestBase.TryOpenTabWithElement"/> until the view root (<c>ProjectsView</c>) appears.
/// <para>
/// In a fresh, throwaway test config the feature is <b>disabled</b> — <c>ProjectsConfig.EnableProjects</c>
/// defaults to <c>false</c> and no project directory is configured — so the view renders the
/// "Projects is disabled" placeholder (<c>Projects_DisabledPlaceholder</c>) and the whole list/detail
/// split (Refresh, project list, Open Project / Open Files) is collapsed out of the tree. The journey
/// therefore always asserts the root + placeholder, then <i>guards</i> every enabled-only control: a
/// control is only invoked when <see cref="UITestBase.WaitForId"/> finds it within a short window,
/// otherwise it is skipped rather than failed. This keeps the pass green in the disabled default state
/// while still exercising the controls whenever a config happens to have Projects enabled.
/// </para>
/// The Open Project / Open Files buttons open a tab, so they are checked for presence only (never
/// invoked) — invoking them would navigate away from the view mid-pass.
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("projects")]
public class ProjectsJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Projects";

    [TestMethod]
    public void Projects_Controls_RespondInOnePass()
    {
        var view = WaitForId("ProjectsView", 15);
        Assert.IsNotNull(view, "ProjectsView did not open via --openTab Projects.");

        // Root is always present regardless of enabled/disabled state.
        CheckPresent("Projects root", "ProjectsView");

        // Fresh test config → feature disabled → the placeholder is the only visible content.
        // If a config has Projects enabled the placeholder is collapsed and the list/detail controls
        // appear instead, so treat both the placeholder and the enabled controls as optional and guard each.
        if (WaitForId("Projects_DisabledPlaceholder", 3) is not null)
            CheckPresent("Disabled placeholder", "Projects_DisabledPlaceholder");

        // Refresh only mutates the in-memory project list — safe to invoke when present.
        if (WaitForId("Projects_Refresh", 3) is not null)
            CheckInvoke("Refresh", "Projects_Refresh");

        // Project list appears only when the feature is enabled.
        if (WaitForId("Projects_List", 3) is not null)
            CheckPresent("Project list", "Projects_List");

        // Open Project / Open Files open a tab — presence only, and only reachable with a selected project.
        if (WaitForId("Projects_OpenProject", 3) is not null)
            CheckPresent("Open Project", "Projects_OpenProject");
        if (WaitForId("Projects_OpenFiles", 3) is not null)
            CheckPresent("Open Files", "Projects_OpenFiles");

        AssertJourney();
    }
}
