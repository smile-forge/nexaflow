using Nexaflow.Tests.Features.UI.Infrastructure;

namespace Nexaflow.Tests.Features.Projects.UI;

/// <summary>
/// Enabled-state UI journey for the backlog-status folder viewlet: seeds a project directory, then
/// navigates the file browser to a folder containing a <c>.project</c> file and asserts the viewlet
/// appears. Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
public class ProjectsViewletJourneyTests : UiJourneyTestBase
{
    // Default file-browser launch tab (no --openTab) — NavigateFileBrowserTo drives it.
    protected override void SeedConfig(string configDir) => ProjectsUiSeed.Write(configDir);

    [TestMethod]
    public void BacklogViewlet_ShowsForProjectFolder()
    {
        NavigateFileBrowserTo(ProjectsUiSeed.AlphaFolder(ConfigDir));

        Assert.IsNotNull(WaitForId("Projects_BacklogViewlet", 10),
            "The backlog-status viewlet did not appear for a folder containing a .project file.");
        CheckPresent("Backlog viewlet", "Projects_BacklogViewlet");

        AssertJourney();
    }
}
