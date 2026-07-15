using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Core.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.UI;

/// <summary>
/// End-to-end for the AI input's "/" quick-open: typing "/serv" into the real shell surfaces a palette row
/// for the Services page, and pressing Enter opens that tab. Exercises the popup + keyboard path the unit
/// tests can't (they cover ranking/nav only); the base class also fails on any unhandled UI-thread error.
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
    public void SlashQuery_ShowsAMatch_AndEnterOpensThePage()
    {
        var box = AiInput;
        box.Focus();
        Thread.Sleep(150);

        // "Services" is a default-openable page — /serv should surface it in the palette.
        Keyboard.Type("/serv");
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);   // the palette rebuild is debounced off the keystroke

        Assert.IsNotNull(WaitForName("Services", 6),
            "The '/' palette did not surface a row for the Services page.");

        // Enter opens the highlighted row.
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);

        var tab = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabItem_SystemServices"));
        Assert.IsNotNull(tab, "Pressing Enter on the palette row did not open the Services tab.");
        Assert.IsFalse(App.HasExited, "The app crashed while opening a tab from the palette.");
    }

    private AutomationElement? WaitForName(string name, int seconds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            AutomationElement? el = null;
            try { el = MainWindow.FindFirstDescendant(cf => cf.ByName(name)); } catch { }
            if (el is not null && !el.IsOffscreen) return el;
            Thread.Sleep(150);
        }
        return null;
    }
}
