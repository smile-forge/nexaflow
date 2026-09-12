using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// One-pass journey for the Help pane — Core's own page, opened beside the page in use by the chrome's ? button and
/// by F1. Walks it the way a reader would: open the help for the default File System tab (the window splits, help on
/// the right), search it for "pane" — a word the File System and Help showcases both use — open the match from the
/// other help page, list every help page, then F1 to point help back at the page in use and F1 again to close it.
/// <para>
/// The showcases come out of the English language pack beside the app, so this also proves the pack shipped with the
/// build and that pages load from it. Interactive desktop only — run with --filter "TestCategory=UI".
/// </para>
/// </summary>
[TestClass]
[CoversNode("help-ui")]
public class HelpJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Help_OpensBeside_Searches_AndCloses()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        CheckDoes("? opens help beside the page in use", "Chrome_HelpButton",
                  () => WaitForId("Help_Document", Affordable(8)) is not null && TabStrips() == 2);
        CheckPresent("the Help tab", "TabItem_Help");

        Check("typing searches the page shown, then every other help page", () =>
        {
            var box = WaitForId("Help_SearchBox", Affordable(5));
            if (box is null) return false;
            box.Focus();
            box.AsTextBox().Text = "pane";
            Wait.UntilInputIsProcessed();
            return WaitForId("Help_SearchStatus", Affordable(6)) is not null
                && WaitForId("Help_OtherResults", Affordable(8)) is not null;
        });
        CheckPresent("the match count", "Help_SearchMatchCount");
        CheckInvoke("next match",       "Help_SearchNext");
        CheckInvoke("previous match",   "Help_SearchPrevious");

        Check("a match on another help page opens it, with the search carried over", () =>
        {
            var first = WaitForId("Help_OtherResults", Affordable(5))
                ?.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem));
            if (first is null) return false;
            first.AsListBoxItem().Select();
            Wait.UntilInputIsProcessed();
            Thread.Sleep(500);
            return WaitForId("Help_Document", Affordable(3)) is not null
                && WaitForId("Help_SearchStatus", Affordable(3)) is not null;
        });

        CheckDoes("clearing ends the search", "Help_SearchClear",
                  () => WaitForId("Help_SearchStatus", 1) is null);
        CheckDoes("the index button lists every help page", "Help_IndexButton",
                  () => WaitForId("Help_Document", Affordable(3)) is not null);

        // Help's own page is where locate: links are shown off — the one kind of link that points at a control on screen
        // instead of navigating. Reaching it from the index is the index doing its job.
        Check("an index entry opens that help page", () => InvokeLink(name => name == "Help"));
        Check("a locate: link lassoes the control it names", () =>
            InvokeLink(name => name.Contains("like this one", StringComparison.Ordinal))
            && WaitForId("Locate_Lasso", Affordable(4)) is not null);
        Check("and a click takes the lasso down", () =>
        {
            WaitForId("Help_SearchBox", Affordable(3))?.Click();
            Wait.UntilInputIsProcessed();
            Thread.Sleep(600);
            return MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Locate_Lasso")) is null;
        });

        // The reader moved within help (a result, the index), so the pane stayed where they took it: F1 points it back
        // at the page in use, and pressed again while it shows that page, closes it.
        Check("F1 points help back at the page in use", () =>
        {
            Keyboard.Press(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(400);
            return WaitForId("Help_Document", Affordable(3)) is not null;
        });
        Check("F1 again closes help, and the split with it", () =>
        {
            Keyboard.Press(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(500);
            return WaitForId("Help_Document", 1) is null && TabStrips() == 1;
        });

        AssertJourney();
    }

    /// <summary>Follows a link in the help document by its text. Rendered hyperlinks are the document's own UIA children;
    /// a locate: link's name carries its pin glyph too, so callers match on part of the text rather than all of it.</summary>
    private bool InvokeLink(Func<string, bool> matches)
    {
        var link = WaitForId("Help_Document", Affordable(3))
            ?.FindAllDescendants(cf => cf.ByControlType(ControlType.Hyperlink))
            .FirstOrDefault(l => l.Name is { } name && matches(name));
        if (link?.Patterns.Invoke.PatternOrDefault is not { } invoke) return false;

        invoke.Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);
        return true;
    }

    private int TabStrips() => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabStrip")).Length;
}
