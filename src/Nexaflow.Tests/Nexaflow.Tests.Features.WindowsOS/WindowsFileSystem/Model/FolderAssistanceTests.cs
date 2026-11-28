using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.Model;

/// <summary>
/// What the assistant is given of a tab, with no view over it: the folder with what it lists and what is selected, the
/// folder its tools are confined to, and how risky acting there is.
/// </summary>
[TestClass]
[CoversNode("win-file-system-ai-act")]
public class FolderAssistanceTests
{
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch()
    {
        _scratch = ModelHarness.Scratch();
        Directory.CreateDirectory(Path.Combine(_scratch, "sub"));
        File.WriteAllText(Path.Combine(_scratch, "a.txt"), "a");
    }

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

    [TestMethod]
    public async Task TheContext_NamesTheFolder_WhatItLists_AndWhatIsSelected()
    {
        var model = await ModelHarness.AtAsync(_scratch);
        model.Selection = [model.Listing.Entries.Single(e => e.Name == "a.txt")];

        var context = model.Assistance.GetContext();

        StringAssert.Contains(context, _scratch);
        StringAssert.Contains(context, "Folders (1): sub");
        StringAssert.Contains(context, "Files (1): a.txt");
        StringAssert.Contains(context, "Selected: a.txt");
    }

    [TestMethod]
    public async Task TheToolsAreConfinedToTheOpenFolder_WhereActingIsLowRisk()
    {
        var model = await ModelHarness.AtAsync(_scratch);

        Assert.AreEqual(_scratch, model.Assistance.GetSecurityContext());
        CollectionAssert.Contains(model.Assistance.GetContextGrant().Folders.ToArray(), _scratch);
        Assert.AreEqual(ContextSecurityRisk.Low, model.Assistance.GetContextSecurityRisk());
        Assert.IsTrue(model.Assistance.GetClientTools().Any(tool => tool.Name == "get_file_list"));
    }

    [TestMethod]
    public void OnThisPc_TheAssistantReachesEveryDrive_AndActingIsHighRisk()
    {
        var model = ModelHarness.ThisPc();

        Assert.AreEqual("This PC", model.Assistance.GetSecurityContext());
        Assert.AreEqual(ContextSecurityRisk.High, model.Assistance.GetContextSecurityRisk());
        StringAssert.StartsWith(model.Assistance.GetContext(), "File browser — This PC");
    }
}
