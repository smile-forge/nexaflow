using Nexaflow.Core.ViewModels.Overlays;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The shell's two built-in questions: the yes/no confirmation every feature reaches through
/// <c>IShellServices.ConfirmAsync</c> / <c>ShowConfirmation</c>, and the text prompt behind
/// <c>ShowPrompt</c>. Each is one request object that the overlay host shows and drops the moment it is
/// answered, so what a caller depends on is asserted here: its callback runs exactly once, the host is
/// clear before it runs, and a question that supersedes another never leaves the first caller waiting.
/// </summary>
[TestClass]
public class OverlayCoordinatorTests
{
    private static OverlayCoordinator Make() => new(() => false, () => false, () => { }, () => { });

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    public void Confirming_RunsTheCallbackOnce_WithTheHostAlreadyClear()
    {
        var o = Make();
        int confirms = 0;
        object? hostWhenItRan = "unset";
        o.ShowConfirmation("Delete", "Really?", () => { confirms++; hostWhenItRan = o.ActiveOverlay; });

        var request = o.Confirmation!;
        Assert.AreSame(request, o.ActiveOverlay, "the host renders the request itself");

        request.ConfirmCommand.Execute(null);
        request.ConfirmCommand.Execute(null);

        Assert.AreEqual(1, confirms, "a second click on an answered dialog must not re-run the action");
        Assert.IsNull(hostWhenItRan, "the dialog is gone before the action runs");
        Assert.IsFalse(o.ConfirmationVisible);
        Assert.IsNull(o.ActiveOverlay);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    public void Cancelling_RunsTheCancelPath_AndNeverTheConfirmOne()
    {
        var o = Make();
        bool cancelled = false;
        o.ShowConfirmation("Delete", "Really?", () => Assert.Fail("confirm must not fire"), () => cancelled = true);

        o.Confirmation!.CancelCommand.Execute(null);

        Assert.IsTrue(cancelled);
        Assert.IsNull(o.Confirmation);
        Assert.IsNull(o.ActiveOverlay);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    public void BlankButtonLabels_FallBackToTheGenericPair()
    {
        var o = Make();

        o.ShowConfirmation("T", "P", () => { }, confirmLabel: " ", cancelLabel: null);
        Assert.AreEqual("Confirm", o.Confirmation!.ConfirmLabel);
        Assert.AreEqual("Cancel",  o.Confirmation.CancelLabel);

        o.ShowConfirmation("T", "P", () => { }, confirmLabel: "Delete", cancelLabel: "Keep");
        Assert.AreEqual("Delete", o.Confirmation!.ConfirmLabel);
        Assert.AreEqual("Keep",   o.Confirmation.CancelLabel);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    public void ASecondQuestion_ReplacesTheFirst_WhichIsAnsweredCancel()
    {
        var o = Make();
        bool firstCancelled = false;
        o.ShowConfirmation("First", "?", () => Assert.Fail("nobody said yes to the first question"),
                           () => firstCancelled = true);

        o.ShowConfirmation("Second", "?", () => { });

        Assert.IsTrue(firstCancelled, "an awaiting ConfirmAsync completes instead of hanging on an unreachable dialog");
        Assert.AreEqual("Second", o.Confirmation!.Title, "the dialog shows the question its buttons answer");
        Assert.AreSame(o.Confirmation, o.ActiveOverlay);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    public void ACallbackThatAsksAgain_OpensAFreshQuestion()
    {
        var o = Make();
        o.ShowConfirmation("First", "?", () => o.ShowConfirmation("Then", "?", () => { }));

        o.Confirmation!.ConfirmCommand.Execute(null);

        Assert.AreEqual("Then", o.Confirmation?.Title);
        Assert.AreSame(o.Confirmation, o.ActiveOverlay);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    public void CloseOverlay_AnswersAnOpenConfirmationCancel()
    {
        var o = Make();
        bool cancelled = false;
        o.ShowConfirmation("T", "P", () => Assert.Fail("confirm must not fire"), () => cancelled = true);

        o.CloseOverlay();

        Assert.IsTrue(cancelled);
        Assert.IsNull(o.ActiveOverlay);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    public void OpeningAndAnswering_EachRaiseConfirmationVisible()
    {
        var o = Make();
        var raised = new List<string?>();
        o.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        o.ShowConfirmation("T", "P", () => { });
        o.Confirmation!.ConfirmCommand.Execute(null);

        // ShellViewModel re-raises this name, and a WebView2 tab hides its native window on the first and
        // restores it on the second — a missed notification leaves the dialog hidden or the page blank.
        Assert.AreEqual(2, raised.Count(n => n == nameof(OverlayCoordinator.ConfirmationVisible)));
    }

    [TestMethod]
    [CoversNode("chrome-prompt-input")]
    public void Prompt_HandsBackTheEditedValue_Once()
    {
        var o = Make();
        var got = new List<string>();
        o.ShowPrompt("Rename", "Name", "old.txt", got.Add);

        var request = o.Prompt!;
        Assert.AreSame(request, o.ActiveOverlay);
        Assert.AreEqual("old.txt", request.Value, "the box starts from the seed");

        request.Value = "new.txt";
        request.ConfirmCommand.Execute(null);
        request.ConfirmCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "new.txt" }, got);
        Assert.IsFalse(o.PromptVisible);
        Assert.IsNull(o.ActiveOverlay);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-input")]
    public void Prompt_Cancel_RunsTheCancelPath_AndNeverTheConfirmOne()
    {
        var o = Make();
        bool cancelled = false;
        o.ShowPrompt("Rename", "Name", "old.txt", _ => Assert.Fail("confirm must not fire"), () => cancelled = true);

        o.Prompt!.CancelCommand.Execute(null);

        Assert.IsTrue(cancelled);
        Assert.IsNull(o.Prompt);
    }

    [TestMethod]
    [CoversNode("chrome-prompt-confirm")]
    [CoversNode("chrome-prompt-input")]
    public void AConfirmationRaisedOverAPrompt_HandsTheHostBackToThePrompt()
    {
        var o = Make();
        o.ShowPrompt("Save as", "File name", "a.txt", _ => { });
        o.ShowConfirmation("Overwrite?", "a.txt exists.", () => { });

        Assert.AreSame(o.Confirmation, o.ActiveOverlay, "the confirmation outranks the prompt");

        o.Confirmation!.ConfirmCommand.Execute(null);

        Assert.AreSame(o.Prompt, o.ActiveOverlay, "the prompt comes back once the confirmation is answered");
    }
}
