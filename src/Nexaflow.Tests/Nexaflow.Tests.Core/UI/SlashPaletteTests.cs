using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Core.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.UI;

/// <summary>
/// End-to-end for the AI input's "/" quick-open, asserted by its <em>outcome</em>: typing "/serv" then Enter
/// opens the Services page. This exercises the whole pipeline — candidate match, palette open+select, Enter
/// routed through the palette, tab open. The palette itself is a WPF <c>Popup</c> (a separate top-level HWND
/// that FlaUI can't reliably enumerate), so the observable proof is the tab that Enter opens: if the palette
/// hadn't matched/selected, Enter would have sent "/serv" to the AI instead and no Services tab would appear.
/// The ranking/nav mechanics are covered directly by <c>SlashCommandPaletteTests</c>.
///
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("aibar-slash-palette")]
public class SlashPaletteTests : UITestBase
{
    private AutomationElement AiInput =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AiInputBox"))
        ?? throw new AssertFailedException("AiInputBox not found.");

    [TestMethod]
    public void SlashQuery_ThenEnter_OpensTheMatchedPage()
    {
        var box = AiInput;
        box.Focus();
        Thread.Sleep(2500);   // let the (Debug) feature catalog warm before it drives the palette candidates

        // "Services" is a default-openable page — /serv should match it, auto-select it, and Enter opens it.
        Keyboard.Type("/serv");
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);    // the palette rebuild is debounced off the keystroke
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();

        var tab = WaitForId("TabItem_SystemServices", 8);
        Assert.IsNotNull(tab,
            "Typing /serv and pressing Enter did not open the Services tab — the palette didn't match/route.");
        Assert.IsFalse(App.HasExited, "The app crashed while opening a tab from the palette.");
    }

    private AutomationElement? WaitForId(string automationId, int seconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            AutomationElement? el = null;
            try { el = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)); } catch { }
            if (el is not null) return el;
            Thread.Sleep(150);
        }
        return null;
    }
}
