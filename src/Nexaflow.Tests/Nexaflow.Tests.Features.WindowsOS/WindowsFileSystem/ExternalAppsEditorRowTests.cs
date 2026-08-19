using Nexaflow.Features.WindowsFileSystem.Controls;
using Nexaflow.Features.WindowsFileSystem.FileActions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[CoversNode("winfs-external-apps-editor")]
public class ExternalAppsEditorRowTests
{
    // The Options editor rebuilds ExternalAppDefinitions from its rows on Save; the row must carry
    // the wizard's scope criteria through that round-trip, or saving Options silently wipes scopes.
    [TestMethod]
    public void AppRow_RoundTrip_PreservesCriteria()
    {
        var def = new ExternalAppDefinition
        {
            DisplayName = "X", ApplicationPath = "x.exe",
            Criteria = [new() { Type = CriteriaType.PathPattern, Value = @"C:\proj\**\*.slnx" }],
        };

        var back = ExternalAppsEditorControl.AppRow.FromDefinition(def).ToDefinition();

        Assert.AreEqual(1, back.Criteria.Count);
        Assert.AreEqual(CriteriaType.PathPattern, back.Criteria[0].Type);
        Assert.AreEqual(@"C:\proj\**\*.slnx", back.Criteria[0].Value);
    }

    // A pre-scoping config (legacy Extension, no criteria) migrates to a single Extension criterion,
    // and the legacy field is cleared on the way out.
    [TestMethod]
    public void AppRow_MigratesLegacyExtension_ToCriterion()
    {
        var def = new ExternalAppDefinition { DisplayName = "X", ApplicationPath = "x.exe", Extension = ".slnx" };

        var back = ExternalAppsEditorControl.AppRow.FromDefinition(def).ToDefinition();

        Assert.AreEqual(string.Empty, back.Extension);
        Assert.AreEqual(1, back.Criteria.Count);
        Assert.AreEqual(CriteriaType.Extension, back.Criteria[0].Type);
        Assert.AreEqual("*.slnx", back.Criteria[0].Value);
    }
}
