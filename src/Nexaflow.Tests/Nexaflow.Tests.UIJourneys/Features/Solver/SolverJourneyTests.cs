using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Solver.UI;

/// <summary>
/// The Solver, driven end to end in the real shell: type a sum, take the offer, read the answer.
/// <para>
/// What only this can check is that the page's three surfaces are actually wired to each other —
/// that typing reaches the solvers, that a chip appears because of what was typed, and that pressing
/// it puts a result on screen. The unit tests prove each of those in isolation; a broken binding, a
/// missing theme key or a mis-set <c>Tag</c> on the mode rail would leave every one of them green
/// and the page inert.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("solver-ui")]
public class SolverJourneyTests : UiJourneyTestBase
{
    /// <summary>Land straight on the Solver rather than clicking through the ribbon.</summary>
    protected override string? LaunchTabKind => "Solver";

    [TestMethod]
    public void Solver_Controls_RespondInOnePass()
    {
        // ── the page loaded at all ───────────────────────────────────────────
        var root = CheckPresent("Solver page", "SolverView", 20);
        if (root is null) { AssertJourney(); return; }

        // ── the mode rail ────────────────────────────────────────────────────
        CheckPresent("Calc tab", "Solver_Mode_Calc");
        CheckPresent("Latex tab", "Solver_Mode_Latex");
        CheckPresent("Text tab", "Solver_Mode_Text");

        // ── typing a sum brings up its chip, and pressing it answers ─────────
        var calc = CheckPresent("Calc input", "Solver_CalcInput");
        if (calc is not null)
        {
            calc.Click();
            Wait.UntilInputIsProcessed();
            Keyboard.Type("2+2*3");
            Wait.UntilInputIsProcessed();

            // The strip is debounced, so give it a moment rather than reading it on the same frame.
            Check("'=' chip appears for an arithmetic definition",
                () => WaitForId("SolverChip_equals_eval", 6) is not null);

            CheckDoes("Run the '=' chip", "SolverChip_equals_eval",
                () => WaitForId("Solver_Results", 6) is not null);

            // The answer must be laid out across the cell, not squeezed into a column of single
            // characters. This is a real regression guard, not a tautology: the results scroller
            // inherits the app's implicit ScrollViewer style, whose HorizontalScrollBarVisibility
            // is Hidden — and Hidden still enables horizontal scrolling, so the whole subtree gets
            // measured at infinite width and a FlowDocument collapses to its minimum page width.
            // Every result then renders one letter per line and display maths vanishes entirely.
            Check("The result body fills the cell rather than collapsing to a column", () =>
            {
                var body = WaitForId("Solver_ResultBody", 6);
                var list = WaitForId("Solver_Results", 3);
                if (body is null || list is null) return false;
                return body.BoundingRectangle.Width > list.BoundingRectangle.Width / 2;
            });

            // ── a result's own buttons ───────────────────────────────────────────
            // Copy leaves nothing on screen to read, so it is pressed for its wiring. An answer goes back in as
            // LaTeX, so using it as the definition opens that editor.
            CheckInvoke("Copy the answer", "Solver_Result_Copy");
            CheckDoes("Use the answer as the definition", "Solver_Result_UseAsDefinition",
                () => WaitForId("Solver_LatexInput", 6) is not null);
            CheckDoes("Remove the result", "Solver_Result_Remove",
                () => WaitForGone("Solver_Results", 4));

            // ── the calculator keypad — a key's id is its label ──────────────────
            CheckDoes("Calc tab brings the keypad back", "Solver_Mode_Calc",
                () => WaitForId("Solver_Key_7", 6) is not null);
            CheckDoes("C clears the definition", "Solver_Key_C",
                () => WaitForFs(() => calc.AsTextBox().Text.Length == 0, 3));
            CheckDoes("7 types a seven", "Solver_Key_7",
                () => WaitForFs(() => calc.AsTextBox().Text == "7", 3));
        }

        // ── the palette ──────────────────────────────────────────────────────
        CheckPresent("Palette toggle", "Solver_TogglePalette");

        // ── switching to LaTeX swaps the editor ──────────────────────────────
        CheckDoes("Latex tab switches the editor", "Solver_Mode_Latex",
            () => WaitForId("Solver_LatexInput", 6) is not null);

        // ── the symbol navigator — every ring, and the way back out of each ──
        // Every group is opened and the first symbol of each ring of symbols pressed; the centre steps back out, so
        // the walk is at the top again between categories. What it presses is what the recent strip remembers.
        var pressed = new List<string>();
        Check("walk every ring of the symbol navigator", () => { WalkNavigator(pressed); return pressed.Count > 0; });
        CheckPresent("the centre reads where the walk ended", "Solver_Navigator_Centre");

        // The editor's content is not readable through automation, but the Clear beside it shows only while there is
        // some — so emptying it and pressing a recent symbol shows as that button going and coming back.
        CheckDoes("Clear empties the definition", "Solver_ClearDefinition",
            () => WaitForGone("Solver_ClearDefinition", 3));
        var recent = $"Solver_Recent_{pressed.LastOrDefault()}";
        Check($"a recent symbol ('{recent}') types into the definition", () =>
            PressNth(recent, 0) && WaitForId("Solver_ClearDefinition", 3) is not null);
        Check("Source toggle shows the LaTeX source", () => SetToggle("Solver_SourceToggle", true));
        Check("and back to the typeset form", () => SetToggle("Solver_SourceToggle", false));

        // ── the breadcrumb — each step goes straight back to its level ───────
        Check("drill two rings down", () => OpenFirstGroup() && OpenFirstGroup());
        var middle = WaitForId("Solver_Crumb_1", 3)?.Name;
        CheckDoes("the middle breadcrumb step goes back one ring", "Solver_Crumb_1",
            () => middle is not null && WaitForFs(() => NavigatorCentre()?.Name == middle, 3));
        CheckDoes("the top breadcrumb step goes back to the top", "Solver_Crumb_0",
            () => WaitForFs(() => NavigatorCentre()?.IsEnabled == false, 3));

        // Each tab has its own editor, so a switch shows the other one rather than re-pointing one at
        // different text — and the Latex one goes away rather than lingering under the Text tab.
        CheckDoes("Text tab switches to its own editor", "Solver_Mode_Text",
            () => WaitForId("Solver_TextInput", 6) is not null);

        // ── and back again ───────────────────────────────────────────────────
        CheckDoes("Calc tab switches back", "Solver_Mode_Calc",
            () => WaitForId("Solver_CalcInput", 6) is not null);

        AssertJourney();
    }

    private AutomationElement? NavigatorCentre() =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Solver_Navigator_Centre"));

    private AutomationElement? NavigatorTile(int index) =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId($"Solver_Navigator_Tile{index}"));

    /// <summary>
    /// Opens every group in the navigator's current ring, depth first, stepping back out through the centre after
    /// each, and presses the first symbol of every ring of symbols it reaches — recording each in
    /// <paramref name="pressed"/>. Throws, naming the tile, when one does not open or the centre does not step back.
    /// </summary>
    private void WalkNavigator(List<string> pressed)
    {
        var here = NavigatorCentre()?.Name ?? throw new InvalidOperationException("the navigator has no centre");
        var symbolPressed = false;
        for (var i = 0; i < 8; i++)
        {
            if (NavigatorTile(i) is not { } tile) continue;
            var label = tile.Name;

            if (tile.Properties.ItemType.ValueOrDefault != "Group")
            {
                if (symbolPressed) continue;
                tile.AsButton().Invoke();
                Wait.UntilInputIsProcessed();
                pressed.Add(label);
                symbolPressed = true;
                continue;
            }

            tile.AsButton().Invoke();
            if (!WaitForFs(() => NavigatorCentre()?.Name == label, 3))
                throw new InvalidOperationException($"'{label}' under '{here}' did not open");

            WalkNavigator(pressed);

            NavigatorCentre()!.AsButton().Invoke();
            if (!WaitForFs(() => NavigatorCentre()?.Name == here, 3))
                throw new InvalidOperationException($"the centre did not step back out of '{label}' to '{here}'");
        }
    }

    /// <summary>Opens the first group tile in the current ring. True once the centre reads its label.</summary>
    private bool OpenFirstGroup()
    {
        for (var i = 0; i < 8; i++)
        {
            if (NavigatorTile(i) is not { } tile || tile.Properties.ItemType.ValueOrDefault != "Group") continue;
            var label = tile.Name;
            tile.AsButton().Invoke();
            return WaitForFs(() => NavigatorCentre()?.Name == label, 3);
        }
        return false;
    }
}
