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
        }

        // ── the palette ──────────────────────────────────────────────────────
        CheckPresent("Palette toggle", "Solver_TogglePalette");

        // ── switching to LaTeX swaps the editor ──────────────────────────────
        CheckDoes("Latex tab switches the editor", "Solver_Mode_Latex",
            () => WaitForId("Solver_MarkdownInput", 6) is not null);

        // ── and back again ───────────────────────────────────────────────────
        CheckDoes("Calc tab switches back", "Solver_Mode_Calc",
            () => WaitForId("Solver_CalcInput", 6) is not null);

        AssertJourney();
    }
}
