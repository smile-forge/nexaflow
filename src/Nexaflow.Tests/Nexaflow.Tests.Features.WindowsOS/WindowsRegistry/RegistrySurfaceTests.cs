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
/// The registry editor's <b>pre-write</b> surface: every destructive action asks first — a delete through the
/// shell's window-modal confirmation, a name or value through the in-tab prompt — so what these assert is that
/// the right question is asked, seeded correctly, and that the guards fire <i>before</i> anything is written: a
/// hive root can't be renamed or deleted, the default value can't be deleted, and a cancelled file picker
/// aborts cleanly. Nothing here touches the live registry: every confirmation is declined (the substitute's
/// default), the writes themselves live behind <see cref="RegistryWriterTests"/>, and "declined writes
/// nothing" is shown on a disposable key by <see cref="RegistryDeleteConfirmationTests"/>.
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

    /// <summary>Every question the view-model asked the shell — title and message — whichever of
    /// <c>ConfirmAsync</c>'s overloads or the callback-style <c>ShowConfirmation</c> it used.</summary>
    private static List<(string Title, string Message)> Asked(IShellServices shell) =>
        shell.ReceivedCalls()
             .Where(c => c.GetMethodInfo().Name is nameof(IShellServices.ConfirmAsync)
                                                or nameof(IShellServices.ShowConfirmation))
             .Select(c => ((string)c.GetArguments()[0]!, (string)c.GetArguments()[1]!))
             .ToList();

    // ── Key tree actions ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("registry-new-key")]
    public void NewKey_OpensTheInputPrompt_SeededWithAPlaceholderName()
    {
        var vm = AtSubKey(out _);

        vm.NewKeyCommand.Execute(null);

        Assert.IsTrue(vm.InputPrompt?.IsOpen == true);
        Assert.AreEqual("New Key", vm.InputPrompt!.Title);
        Assert.AreEqual("New Key", vm.InputPrompt!.Value, "the box is pre-filled so Enter alone is meaningful");
    }

    [TestMethod]
    [CoversNode("registry-rename-key")]
    public void RenameKey_SeedsThePromptWithTheCurrentLeafName()
    {
        var vm = AtSubKey(out _, @"HKCU\Software\Microsoft");

        vm.RenameKeyCommand.Execute(null);

        Assert.IsTrue(vm.InputPrompt?.IsOpen == true);
        Assert.AreEqual("Rename Key", vm.InputPrompt!.Title);
        Assert.AreEqual("Microsoft", vm.InputPrompt!.Value, "renaming starts from the existing name");
    }

    [TestMethod]
    [CoversNode("registry-rename-key")]
    public void RenameKey_AtAHiveRoot_IsRefusedBeforeAnyPrompt()
    {
        var vm = Make(out _);
        vm.NavigateTo("HKCU");

        vm.RenameKeyCommand.Execute(null);

        Assert.IsFalse(vm.InputPrompt?.IsOpen == true, "a hive root has no name to rename");
    }

    [TestMethod]
    [CoversNode("registry-delete-key")]
    public async Task DeleteKey_ConfirmsFirst_AndNamesTheKeyItWouldDestroy()
    {
        var vm = AtSubKey(out var shell, @"HKCU\Software\Microsoft");

        await vm.DeleteKeyCommand.ExecuteAsync(null);

        var (title, message) = Asked(shell).Single();
        Assert.AreEqual("Delete key", title);
        StringAssert.Contains(message, @"HKCU\Software\Microsoft");
        StringAssert.Contains(message, "subkeys");
    }

    [TestMethod]
    [CoversNode("registry-delete-key")]
    public async Task DeleteKey_AtAHiveRoot_IsRefusedBeforeAnyConfirmation()
    {
        var vm = Make(out var shell);
        vm.NavigateTo("HKLM");

        await vm.DeleteKeyCommand.ExecuteAsync(null);

        Assert.AreEqual(0, Asked(shell).Count, "a whole hive is never a deletion target");
    }

    // ── Value list actions ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("registry-new-value")]
    public void NewValue_OpensAnEmptyNamePrompt_TitledForTheChosenType()
    {
        var vm = AtSubKey(out _);

        vm.NewValueCommand.Execute("DWord");

        Assert.IsTrue(vm.InputPrompt?.IsOpen == true);
        StringAssert.Contains(vm.InputPrompt!.Title, "REG_DWORD");
        Assert.AreEqual(string.Empty, vm.InputPrompt!.Value, "a new value starts unnamed");
    }

    [TestMethod]
    [CoversNode("registry-new-value")]
    public void NewValue_WithAnUnknownTypeName_DoesNothing()
    {
        var vm = AtSubKey(out _);

        vm.NewValueCommand.Execute("NotARegistryKind");

        Assert.IsFalse(vm.InputPrompt?.IsOpen == true);
    }

    [TestMethod]
    [CoversNode("registry-modify-value")]
    public void EditValue_OpensTheDataPromptForTheSelectedRow()
    {
        var vm = AtSubKey(out _);
        var row = new RegistryValue("SomeName", RegistryValueKind.String, "old data");

        vm.EditValueCommand.Execute(row);

        Assert.IsTrue(vm.InputPrompt?.IsOpen == true);
        StringAssert.Contains(vm.InputPrompt!.Title, "REG_SZ");
        StringAssert.Contains(vm.InputPrompt!.Label, "SomeName");
    }

    [TestMethod]
    [CoversNode("registry-modify-value")]
    public void EditValue_WithNothingSelected_DoesNothing()
    {
        var vm = AtSubKey(out _);

        vm.EditValueCommand.Execute(null);      // no row passed and none selected

        Assert.IsFalse(vm.InputPrompt?.IsOpen == true);
    }

    [TestMethod]
    [CoversNode("registry-delete-value")]
    public async Task DeleteValue_ConfirmsFirst_ButRefusesTheDefaultValue()
    {
        var vm = AtSubKey(out var shell);

        await vm.DeleteValueCommand.ExecuteAsync(new RegistryValue("", RegistryValueKind.String, "x"));
        Assert.AreEqual(0, Asked(shell).Count, "the key's default value can only be cleared, never deleted");

        await vm.DeleteValueCommand.ExecuteAsync(new RegistryValue("Named", RegistryValueKind.String, "x"));
        StringAssert.Contains(Asked(shell).Single().Message, "Named");
    }

    // ── Overlays ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("registry-input-prompt")]
    public void InputPrompt_OkHandsBackTheEditedText_AndCloses()
    {
        var vm = Make(out _);
        string? got = null;
        vm.ShowInputPrompt("Title", "Label", "seed", v => got = v, () => Assert.Fail("cancel must not fire"));

        Assert.IsTrue(vm.InputPrompt?.IsOpen == true);
        vm.InputPrompt!.Value = "typed by the user";
        vm.InputPrompt!.ConfirmCommand.Execute(null);

        Assert.AreEqual("typed by the user", got);
        Assert.IsFalse(vm.InputPrompt?.IsOpen == true);
    }

    [TestMethod]
    [CoversNode("registry-input-prompt")]
    public void InputPrompt_CancelRunsTheCancelPath_AndNeverTheConfirmOne()
    {
        var vm = Make(out _);
        bool cancelled = false;
        vm.ShowInputPrompt("Title", "Label", "seed", _ => Assert.Fail("confirm must not fire"), () => cancelled = true);

        vm.InputPrompt!.CancelCommand.Execute(null);

        Assert.IsTrue(cancelled);
        Assert.IsFalse(vm.InputPrompt?.IsOpen == true);
    }

    [TestMethod]
    [CoversNode("registry-input-prompt")]
    public void InputPrompt_CallbacksAreOneShot_SoASecondOkDoesNothing()
    {
        var vm = Make(out _);
        int confirms = 0;
        vm.ShowInputPrompt("Title", "Label", "seed", _ => confirms++, () => { });

        vm.InputPrompt!.ConfirmCommand.Execute(null);
        vm.InputPrompt!.ConfirmCommand.Execute(null);

        Assert.AreEqual(1, confirms, "a stale callback must not re-run against a later key");
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
