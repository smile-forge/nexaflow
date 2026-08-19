using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Projects;

/// <summary>
/// Builds the Projects material the UI journey opens. It runs here, with the suite that owns the feature,
/// because the journey suite references nothing but the built app — so the corpus has to exist before it
/// runs, and an absent one leaves that journey inconclusive rather than failing.
/// </summary>
[TestClass]
[NoCoverage("Builds a fixture for another suite; asserts no behaviour of its own.")]
public class ProjectsUiFixtureTests
{
    [TestMethod]
    public void SeedsTheProjectsFixtureForTheUiJourney() => UiFixtures.SeedProjects();
}
