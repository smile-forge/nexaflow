using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// End-to-end: define a template in Options (name/icon/extension + source file), Save, then create a
/// file from that template via the New overlay. Covers both "setting up the options" and "creating the
/// file". Requires an interactive desktop — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
public class TemplatedCreateOptionsTests : FileSystemUiTestBase
{
    private const string TemplateBody = "TEMPLATE-CONTENT-XYZ";
    private string _folder = null!;
    private string _sourceTemplate = null!;

    protected override void OnUISetup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "nexaflow-tpl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _sourceTemplate = Path.Combine(Path.GetTempPath(),
            "nexaflow-tplsrc-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(_sourceTemplate, TemplateBody);
        NavigateFileBrowserTo(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { }
        try { if (File.Exists(_sourceTemplate)) File.Delete(_sourceTemplate); } catch { }
    }

    [TestMethod]
    public void OptionsSetup_ThenCreateFromTemplate()
    {
        // 1. Open Options.
        var optionsBtn = WaitForName("Options", 6);
        if (optionsBtn is null) Assert.Inconclusive("Could not find the Options button.");
        optionsBtn!.AsButton().Invoke();

        // 2. Select the "Templated Create" section.
        var section = WaitForName("Templated Create", 6);
        if (section is null) Assert.Inconclusive("Templated Create section not found in Options.");
        section!.Click();
        Wait.UntilInputIsProcessed();

        // 3. Add a row and fill it in.
        var add = WaitForId("AddTemplateButton", 5);
        Assert.IsNotNull(add, "Add button missing in Templated Create editor.");
        add!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        SetText("TemplateNameBox", "My Note");
        SetText("TemplateExtBox", ".md");
        SetText("TemplateIconBox", "📝");
        SetText("TemplateSourceBox", _sourceTemplate);

        // 4. Save.
        var save = WaitForName("Save", 5);
        Assert.IsNotNull(save, "Save button missing.");
        save!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        // 5. The template file is copied into the isolated config dir, content intact.
        var tplDir = Path.Combine(ConfigDir, "templatedcreate", "templates");
        Assert.IsTrue(WaitForFs(() => Directory.Exists(tplDir) && Directory.GetFiles(tplDir).Length > 0, 8),
            "Template file was not copied into appdata.");
        Assert.AreEqual(TemplateBody, File.ReadAllText(Directory.GetFiles(tplDir).First()));

        // 6. Create a file from the new template via the New overlay (the tab is still on _folder).
        var newBtn = WaitForId("New", 10);
        Assert.IsNotNull(newBtn, "New button not found after saving the template.");
        newBtn!.AsButton().Invoke();
        // CreateOverlay is a Border (no UI Automation peer) — gate on its filename box instead.
        Assert.IsNotNull(WaitForId("CreateFileNameBox", 6), "Create overlay did not open.");

        var myType = WaitForId("My Note", 6);
        Assert.IsNotNull(myType, "The template type was not shown in the create overlay.");
        myType!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        SetText("CreateFileNameBox", "from-template.md");
        WaitForId("CreateConfirmButton")!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        var outFile = Path.Combine(_folder, "from-template.md");
        Assert.IsTrue(WaitForFs(() => File.Exists(outFile), 8), "File was not created from the template.");
        Assert.AreEqual(TemplateBody, File.ReadAllText(outFile));
    }
}
