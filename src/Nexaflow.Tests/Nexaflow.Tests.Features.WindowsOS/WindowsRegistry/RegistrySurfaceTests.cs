using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Ribbon;
using Nexaflow.Features.WindowsRegistry.RibbonHandlers;
using Nexaflow.Features.WindowsRegistry.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.WindowsRegistry;

/// <summary>
/// The registry editor's <b>pre-write</b> surface: every destructive action goes through an in-tab overlay
/// first, so what these assert is that the right overlay opens, seeded correctly, and that the guards fire
/// <i>before</i> anything is written — a hive root can't be renamed or deleted, the default value can't be
/// deleted, and a cancelled file picker aborts cleanly. Nothing here touches the live registry: the writes
/// themselves live behind <see cref="RegistryWriterTests"/> (a disposable HKCU subtree) and the elevation
/// bridge.
/// </summary>
[TestClass]
public class RegistrySurfaceTests
{
    private static RegistryViewModel Make(out IShellServices shell)
    {
        shell = Substitute.For<IShellServices>();
        return new RegistryViewModel(shell);
    }

    /// <summary>A view-model parked on a subkey (so the "not at a hive root" guards let actions through).</summary>
    private static RegistryViewModel AtSubKey(out IShellServices shell, string path = @"HKCU\Software")
    {
        var vm = Make(out shell);
        vm.NavigateTo(path);
        return vm;
    }

    // ── Key tree actions ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("registry-new-key")]
    public void NewKey_OpensTheInputPrompt_SeededWithAPlaceholderName()
    {
        var vm = AtSubKey(out _);

        vm.NewKeyCommand.Execute(null);

        Assert.IsTrue(vm.InputPromptVisible);
        Assert.AreEqual("New Key", vm.InputPromptTitle);
        Assert.AreEqual("New Key", vm.InputPromptValue, "the box is pre-filled so Enter alone is meaningful");
    }

    [TestMethod]
    [CoversNode("registry-rename-key")]
    public void RenameKey_SeedsThePromptWithTheCurrentLeafName()
    {
        var vm = AtSubKey(out _, @"HKCU\Software\Microsoft");

        vm.RenameKeyCommand.Execute(null);

        Assert.IsTrue(vm.InputPromptVisible);
        Assert.AreEqual("Rename Key", vm.InputPromptTitle);
        Assert.AreEqual("Microsoft", vm.InputPromptValue, "renaming starts from the existing name");
    }

    [TestMethod]
    [CoversNode("registry-rename-key")]
    public void RenameKey_AtAHiveRoot_IsRefusedBeforeAnyPrompt()
    {
        var vm = Make(out _);
        vm.NavigateTo("HKCU");

        vm.RenameKeyCommand.Execute(null);

        Assert.IsFalse(vm.InputPromptVisible, "a hive root has no name to rename");
    }

    [TestMethod]
    [CoversNode("registry-delete-key")]
    public void DeleteKey_ConfirmsFirst_AndNamesTheKeyItWouldDestroy()
    {
        var vm = AtSubKey(out _, @"HKCU\Software\Microsoft");

        vm.DeleteKeyCommand.Execute(null);

        Assert.IsTrue(vm.ConfirmationVisible);
        Assert.AreEqual("Delete key", vm.ConfirmationTitle);
        StringAssert.Contains(vm.ConfirmationPrompt, @"HKCU\Software\Microsoft");
        StringAssert.Contains(vm.ConfirmationPrompt, "subkeys");
    }

    [TestMethod]
    [CoversNode("registry-delete-key")]
    public void DeleteKey_AtAHiveRoot_IsRefusedBeforeAnyConfirmation()
    {
        var vm = Make(out _);
        vm.NavigateTo("HKLM");

        vm.DeleteKeyCommand.Execute(null);

        Assert.IsFalse(vm.ConfirmationVisible, "a whole hive is never a deletion target");
    }

    // ── Value list actions ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("registry-new-value")]
    public void NewValue_OpensAnEmptyNamePrompt_TitledForTheChosenType()
    {
        var vm = AtSubKey(out _);

        vm.NewValueCommand.Execute("DWord");

        Assert.IsTrue(vm.InputPromptVisible);
        StringAssert.Contains(vm.InputPromptTitle, "REG_DWORD");
        Assert.AreEqual(string.Empty, vm.InputPromptValue, "a new value starts unnamed");
    }

    [TestMethod]
    [CoversNode("registry-new-value")]
    public void NewValue_WithAnUnknownTypeName_DoesNothing()
    {
        var vm = AtSubKey(out _);

        vm.NewValueCommand.Execute("NotARegistryKind");

        Assert.IsFalse(vm.InputPromptVisible);
    }

    [TestMethod]
    [CoversNode("registry-modify-value")]
    public void EditValue_OpensTheDataPromptForTheSelectedRow()
    {
        var vm = AtSubKey(out _);
        var row = new RegistryValue("SomeName", RegistryValueKind.String, "old data");

        vm.EditValueCommand.Execute(row);

        Assert.IsTrue(vm.InputPromptVisible);
        StringAssert.Contains(vm.InputPromptTitle, "REG_SZ");
        StringAssert.Contains(vm.InputPromptLabel, "SomeName");
    }

    [TestMethod]
    [CoversNode("registry-modify-value")]
    public void EditValue_WithNothingSelected_DoesNothing()
    {
        var vm = AtSubKey(out _);

        vm.EditValueCommand.Execute(null);      // no row passed and none selected

        Assert.IsFalse(vm.InputPromptVisible);
    }

    [TestMethod]
    [CoversNode("registry-delete-value")]
    public void DeleteValue_ConfirmsFirst_ButRefusesTheDefaultValue()
    {
        var vm = AtSubKey(out _);

        vm.DeleteValueCommand.Execute(new RegistryValue("", RegistryValueKind.String, "x"));
        Assert.IsFalse(vm.ConfirmationVisible, "the key's default value can only be cleared, never deleted");

        vm.DeleteValueCommand.Execute(new RegistryValue("Named", RegistryValueKind.String, "x"));
        Assert.IsTrue(vm.ConfirmationVisible);
        StringAssert.Contains(vm.ConfirmationPrompt, "Named");
    }

    // ── Overlays ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("registry-input-prompt")]
    public void InputPrompt_OkHandsBackTheEditedText_AndCloses()
    {
        var vm = Make(out _);
        string? got = null;
        vm.ShowInputPrompt("Title", "Label", "seed", v => got = v, () => Assert.Fail("cancel must not fire"));

        Assert.IsTrue(vm.InputPromptVisible);
        vm.InputPromptValue = "typed by the user";
        vm.ConfirmInputPromptCommand.Execute(null);

        Assert.AreEqual("typed by the user", got);
        Assert.IsFalse(vm.InputPromptVisible);
    }

    [TestMethod]
    [CoversNode("registry-input-prompt")]
    public void InputPrompt_CancelRunsTheCancelPath_AndNeverTheConfirmOne()
    {
        var vm = Make(out _);
        bool cancelled = false;
        vm.ShowInputPrompt("Title", "Label", "seed", _ => Assert.Fail("confirm must not fire"), () => cancelled = true);

        vm.CancelInputPromptCommand.Execute(null);

        Assert.IsTrue(cancelled);
        Assert.IsFalse(vm.InputPromptVisible);
    }

    [TestMethod]
    [CoversNode("registry-input-prompt")]
    public void InputPrompt_CallbacksAreOneShot_SoASecondOkDoesNothing()
    {
        var vm = Make(out _);
        int confirms = 0;
        vm.ShowInputPrompt("Title", "Label", "seed", _ => confirms++, () => { });

        vm.ConfirmInputPromptCommand.Execute(null);
        vm.ConfirmInputPromptCommand.Execute(null);

        Assert.AreEqual(1, confirms, "a stale callback must not re-run against a later key");
    }

    [TestMethod]
    [CoversNode("registry-confirmation")]
    public void Confirmation_DeleteRunsTheAction_CancelRunsTheCancelPath()
    {
        var vm = Make(out _);
        bool confirmed = false, cancelled = false;

        vm.ShowConfirmation("T", "P", () => confirmed = true, () => { });
        Assert.IsTrue(vm.ConfirmationVisible);
        vm.ConfirmActionCommand.Execute(null);
        Assert.IsTrue(confirmed);
        Assert.IsFalse(vm.ConfirmationVisible);

        vm.ShowConfirmation("T", "P", () => Assert.Fail("confirm must not fire"), () => cancelled = true);
        vm.CancelConfirmationCommand.Execute(null);
        Assert.IsTrue(cancelled);
        Assert.IsFalse(vm.ConfirmationVisible);
    }

    // ── Export / Import ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("registry-export")]
    public async Task Export_OffersTheKeyNameAsTheFileName_AndAbortsWhenTheDialogIsCancelled()
    {
        var vm = AtSubKey(out var shell, @"HKCU\Software\Microsoft");
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>())
             .Returns(Task.FromResult<string?>(null));      // the user cancels

        await vm.ExportCommand.ExecuteAsync(null);

        await shell.Received().PickSaveFileAsync("Microsoft.reg",
            Arg.Is<IReadOnlyList<string>>(e => e.Contains(".reg")), Arg.Any<string>());
        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
        shell.DidNotReceiveWithAnyArgs().ShowNotification(default!);
    }

    [TestMethod]
    [CoversNode("registry-import")]
    public async Task Import_AbortsCleanlyWhenTheDialogIsCancelled()
    {
        var vm = AtSubKey(out var shell);
        shell.PickOpenFileAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>())
             .Returns(Task.FromResult<string?>(null));      // the user cancels

        await vm.ImportCommand.ExecuteAsync(null);

        await shell.Received().PickOpenFileAsync(
            Arg.Is<IReadOnlyList<string>>(e => e.Contains(".reg")), Arg.Any<string>());
        shell.DidNotReceiveWithAnyArgs().ShowError(default!);
        shell.DidNotReceiveWithAnyArgs().ShowNotification(default!);
    }

    // ── Ribbon pin ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pinning bakes the <i>current key</i> into the button so it re-opens exactly there. With no view
    /// attached the handler falls back to the tab's own parameters rather than inventing a location.
    /// </summary>
    [TestMethod]
    [CoversNode("registry-pin-to-ribbon")]
    public void Pin_WithNoLoadedView_CarriesTheTabsOwnParameters()
    {
        var handler = new RegistryTabPinHandler();
        var tab = new Page
        {
            Title      = "HKCU\\Software",
            Icon       = "🗝",
            PageParams = new Dictionary<string, string> { ["hive"] = "HKCU", ["path"] = "Software" },
        };

        var result = handler.Pin(tab);

        Assert.IsNotNull(result);
        Assert.AreEqual(handler.TabPageKind, result!.PageKind);
        Assert.AreEqual("HKCU\\Software", result.Label);
        Assert.AreEqual("HKCU", result.PageParams!["hive"]);
        Assert.AreEqual("Software", result.PageParams["path"]);
    }

    [TestMethod]
    [CoversNode("registry-pin-to-ribbon")]
    public void Pin_CopiesTheParameters_SoLaterTabEditsDontMutateThePinnedButton()
    {
        var pageParams = new Dictionary<string, string> { ["hive"] = "HKCU" };
        var result = new RegistryTabPinHandler().Pin(new Page { Title = "HKCU", PageParams = pageParams });

        pageParams["hive"] = "HKLM";

        Assert.AreEqual("HKCU", result!.PageParams!["hive"]);
    }
}
