using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Git;

/// <summary>
/// Builds the repository the Git UI journey opens. Here rather than in the journey suite, which references
/// nothing but the built app.
/// </summary>
[TestClass]
[NoCoverage("Builds a fixture for another suite; asserts no behaviour of its own.")]
public class GitUiFixtureTests
{
    [TestMethod]
    public void SeedsTheGitRepositoryForTheUiJourney() => UiFixtures.SeedGitRepo();
}
