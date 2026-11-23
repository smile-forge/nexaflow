using Nexaflow.Tests.Fixtures;
using System.IO;
using System.Text.Json;

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
    public void SeedsTheProjectsFixtureForTheUiJourney()
    {
        UiFixtures.SeedProjects();

        // The config names its folders by the root token, so staged anywhere the corpus is reached — Windows
        // Sandbox maps the checkout elsewhere — it opens the same folders rather than the host's paths.
        var config = File.ReadAllText(Path.Combine(UiFixtures.ProjectsRoot, "Contexts", "Default", "projects", "config_0.0.0.1.json"));
        StringAssert.Contains(config, UiFixtures.RootToken);
        using var staged = JsonDocument.Parse(UiFixtures.ResolveRootInJson(config));
        foreach (var key in new[] { "ProjectDirectory", "ShelfDirectory", "ArchiveDirectory" })
        {
            var folder = staged.RootElement.GetProperty(key).GetString()!;
            Assert.IsTrue(Directory.Exists(folder), $"{key} resolves to '{folder}', which does not exist.");
        }
    }
}
