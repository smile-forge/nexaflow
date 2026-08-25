using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Solver;
using Nexaflow.Features.Solver.Palette;
using Nexaflow.Features.Solver.Solving;
using Nexaflow.Features.Solver.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Solver;

/// <summary>
/// The page ViewModel: what the definition area, the palette and the result list do, with no window
/// anywhere. Everything the UI journey clicks is driven from here first.
/// </summary>
[TestClass]
public class SolverViewModelTests
{
    /// <summary>Chip refresh is debounced and runs off the UI thread, so a test has to wait for it.</summary>
    private const int SettleMs = 2_000;

    private static SolverViewModel Build(SolverConfig? config = null, string? aiAnswer = "**42**")
        => new(config ?? new SolverConfig { ShowPalette = true },
               SolverTestDoubles.Shell(),
               SolverTestDoubles.Ai(aiAnswer));

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(SettleMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    private static Task<bool> WaitForChip(SolverViewModel vm, string label)
        => WaitUntil(() => vm.Chips.Any(c => c.Label == label));

    // ── Definition area ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-latex-fence")]
    public void TheLatexTabIsOneFormulaAndTheTextTabIsAWholeDocument()
    {
        // The fence is not this ViewModel's business and never was the user's: the Latex tab tells the
        // editor that what it holds is a single formula, and the editor puts the $$ on to typeset it
        // and takes it off again. Fencing here as well would leave two ideas of what the text is.
        var vm = Build();

        vm.Mode = DefinitionMode.Latex;
        Assert.IsTrue(vm.IsSingleFormula, "the Latex tab is one formula");

        vm.Mode = DefinitionMode.Text;
        Assert.IsFalse(vm.IsSingleFormula,
            "the Text tab is a full markdown editor — blocks, headings, diagrams and maths of its own");
    }

    [TestMethod]
    [CoversNode("solver-latex-fence")]
    public void ADefinitionIsTheFormulaAndNeverAMathsBlock()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;

        const string source = @"\cos^2 \alpha - \sin^2 \alpha";
        vm.DefinitionText = source;

        Assert.AreEqual(source, vm.DefinitionText, "no fence reaches the definition");
        Assert.AreEqual(source, vm.CurrentInput.Text, "nor the solvers, which are given a formula to read");
    }

    [TestMethod]
    [CoversNode("solver-markdown-input")]
    public void TheEditorStartsRenderedAndSourceIsAnEscapeHatch()
    {
        var vm = Build();

        Assert.IsFalse(vm.ShowsSource,
            "typing straight into the rendered maths is the point of the tab — source is for getting "
            + "out of trouble with one formula, so a freshly opened tab is never in it");

        vm.ShowsSource = true;
        Assert.IsTrue(vm.ShowsSource);
    }

    [TestMethod]
    [CoversNode("solver-latex-fence")]
    public void TheTextTabIsPlainMarkdownWithNoFencing()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Text;

        vm.DefinitionText = "how many primes below 100?";

        Assert.AreEqual("how many primes below 100?", vm.DefinitionText);
        Assert.IsFalse(vm.IsSingleFormula);
    }

    [TestMethod]
    [CoversNode("solver-mode-rail")]
    public void EachTabKeepsItsOwnText()
    {
        var vm = Build();

        vm.Mode = DefinitionMode.Calc;
        vm.DefinitionText = "2+2";
        vm.Mode = DefinitionMode.Latex;
        vm.DefinitionText = @"\alpha";

        vm.Mode = DefinitionMode.Calc;
        Assert.AreEqual("2+2", vm.DefinitionText, "switching tabs must not destroy what is in the other one");

        vm.Mode = DefinitionMode.Latex;
        Assert.AreEqual(@"\alpha", vm.DefinitionText);
    }

    [TestMethod]
    [CoversNode("solver-mode-rail")]
    public void TheRailSelectsByName()
    {
        var vm = Build();

        vm.ModeName = "Latex";

        Assert.AreEqual(DefinitionMode.Latex, vm.Mode);
        Assert.AreEqual("Latex", vm.ModeName);
    }

    [TestMethod]
    [CoversNode("solver-calc-input")]
    public void TheStartTabComesFromSettings()
    {
        var vm = Build(new SolverConfig { StartMode = "Latex" });

        Assert.AreEqual(DefinitionMode.Latex, vm.Mode);
    }

    // ── Chips ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-chip-offers")]
    public async Task TypingAFormulaBringsUpItsChips()
    {
        var vm = Build();
        vm.DefinitionText = "4x + 3x";

        Assert.IsTrue(await WaitForChip(vm, "simplify"), "chips: " + Labels(vm));
        Assert.IsFalse(vm.HasNoChips);
    }

    [TestMethod]
    [CoversNode("solver-chip-offers")]
    public async Task ClearingTheDefinitionTakesTheChipsAwayAgain()
    {
        var vm = Build();
        vm.DefinitionText = "4x + 3x";
        Assert.IsTrue(await WaitForChip(vm, "simplify"));

        vm.ClearDefinitionCommand.Execute(null);

        Assert.IsTrue(await WaitUntil(() => vm.Chips.Count == 0), "chips: " + Labels(vm));
        Assert.IsTrue(vm.IsEmpty);
    }

    [TestMethod]
    [CoversNode("solver-angle-toggle")]
    public async Task TheAngleToggleReOffersTheChipsUnderTheNewReading()
    {
        var vm = Build(new SolverConfig { Angles = "Radians" });
        Assert.AreEqual("RAD", vm.AngleLabel);

        vm.DefinitionText = "sin(45)";
        Assert.IsTrue(await WaitForChip(vm, "="));

        vm.ToggleAngleUnitCommand.Execute(null);

        Assert.AreEqual("DEG", vm.AngleLabel);
        Assert.AreEqual(AngleUnit.Degrees, vm.CurrentInput.AngleUnit,
            "the toggle has to reach the solvers, not just the label");
    }

    // ── Running a chip ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-chip-run")]
    public async Task PressingAChipAppendsAnAnswer()
    {
        var vm = Build();
        vm.DefinitionText = "2+2*3";
        Assert.IsTrue(await WaitForChip(vm, "="));

        await vm.Chips.First(c => c.Label == "=").RunCommand.ExecuteAsync(null);

        Assert.AreEqual(1, vm.Results.Count);
        Assert.IsTrue(vm.HasResults);

        var cell = vm.Results[0];
        Assert.IsFalse(cell.IsBusy);
        Assert.IsFalse(cell.IsError);
        StringAssert.Contains(cell.Markdown, "8");
        Assert.AreEqual("2+2*3", cell.Definition, "a cell records the definition it answered");
    }

    [TestMethod]
    [CoversNode("solver-chip-run")]
    public async Task EachChipAddsAnotherCellRatherThanReplacingTheLast()
    {
        var vm = Build();
        vm.DefinitionText = "4x + 3x";
        Assert.IsTrue(await WaitForChip(vm, "simplify"));

        await vm.Chips.First(c => c.Label == "simplify").RunCommand.ExecuteAsync(null);
        await vm.Chips.First(c => c.Label == "d/dx").RunCommand.ExecuteAsync(null);

        Assert.AreEqual(2, vm.Results.Count, "the result list is a notebook, not a single answer slot");
    }

    [TestMethod]
    [CoversNode("solver-result-remove")]
    public async Task RemovingACellDropsItAndCancelsItsWork()
    {
        var vm = Build();
        vm.DefinitionText = "2+2";
        Assert.IsTrue(await WaitForChip(vm, "="));
        await vm.Chips.First(c => c.Label == "=").RunCommand.ExecuteAsync(null);

        var cell = vm.Results[0];
        cell.RemoveCommand.Execute(null);

        Assert.AreEqual(0, vm.Results.Count);
        Assert.IsFalse(vm.HasResults);
        Assert.IsTrue(cell.Token.IsCancellationRequested,
            "a slow AI answer must actually stop, not complete into a cell nobody can see");
    }

    [TestMethod]
    [CoversNode("solver-result-reuse")]
    public async Task AnAnswerCanBecomeTheNextDefinition()
    {
        var vm = Build();
        vm.DefinitionText = "4x + 3x";
        Assert.IsTrue(await WaitForChip(vm, "simplify"));
        await vm.Chips.First(c => c.Label == "simplify").RunCommand.ExecuteAsync(null);

        vm.Results[0].UseAsDefinitionCommand.Execute(null);

        Assert.AreEqual(DefinitionMode.Latex, vm.Mode);
        Assert.AreEqual("7 x", vm.DefinitionText,
            "only the right-hand side carries forward — feeding back '4x+3x = 7x' would re-solve what was just solved");
    }

    [TestMethod]
    [CoversNode("solver-result-cell")]
    public async Task ASolverFailureBecomesAnErrorCellRatherThanNothing()
    {
        var vm = Build();
        vm.DefinitionText = "e^(x^2)";
        Assert.IsTrue(await WaitForChip(vm, "∫ dx"));

        await vm.Chips.First(c => c.Label == "∫ dx").RunCommand.ExecuteAsync(null);

        Assert.AreEqual(1, vm.Results.Count);
        Assert.IsTrue(vm.Results[0].IsError);
        Assert.IsFalse(vm.Results[0].IsBusy, "an error still ends the spinner");
    }

    [TestMethod]
    [CoversNode("solver-chip-run")]
    public async Task DisposingThePageCancelsEverythingStillRunning()
    {
        var vm = Build();
        vm.DefinitionText = "2+2";
        Assert.IsTrue(await WaitForChip(vm, "="));
        await vm.Chips.First(c => c.Label == "=").RunCommand.ExecuteAsync(null);
        var cell = vm.Results[0];

        vm.Dispose();

        Assert.IsTrue(cell.Token.IsCancellationRequested);
        Assert.AreEqual(0, vm.Results.Count);
    }

    // ── Palette ─────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-key-insert")]
    public void PressingAKeyAsksTheViewToApplyIt()
    {
        var vm = Build();
        PaletteKey? asked = null;
        vm.InsertRequested += key => asked = key;

        var sqrt = new PaletteKey("√", "sqrt(", "Square root") { InsertKind = KeyInsert.Wrapping, Close = ")" };
        vm.PressKeyCommand.Execute(sqrt);

        Assert.IsNotNull(asked);
        Assert.AreEqual("sqrt(", asked.Insert);
        Assert.AreEqual(KeyInsert.Wrapping, asked.InsertKind,
            "the view needs the whole key, not just its text — how it meets the selection is the point");
    }

    [TestMethod]
    [CoversNode("solver-key-insert")]
    public void AStructuralKeyCarriesItsCaretOffset()
    {
        var vm = Build();
        PaletteKey? asked = null;
        vm.InsertRequested += key => asked = key;

        var frac = LatexPalette.Find("latex.basics.frac")!.Keys.First(k => k.Insert.StartsWith(@"\frac{"));
        vm.PressKeyCommand.Execute(frac);

        Assert.AreEqual(3, asked!.CaretBack,
            @"\frac{}{} must land the caret in the numerator, not past the whole thing");
    }

    [TestMethod]
    [CoversNode("solver-key-insert")]
    public void AFunctionKeyBracketsRatherThanReplaces()
    {
        // The complaint this encodes: clicking sin with "45" selected used to delete the 45.
        foreach (var label in new[] { "sin", "cos", "tan", "ln", "√", "|x|" })
        {
            var key = CalcPalette.Main.Keys.First(k => k.Label == label);
            Assert.AreEqual(KeyInsert.Wrapping, key.InsertKind, $"'{label}' should bracket its argument");
            Assert.AreEqual(")", key.Close, $"'{label}' has no closing bracket");
        }
    }

    [TestMethod]
    [CoversNode("solver-key-insert")]
    public void AnOperatorThatNeedsAnOperandSaysSo()
    {
        // n!, x², xʸ and % all follow something. Marking them Postfix is what lets the view refuse
        // them at the start of a line or straight after an operator.
        foreach (var label in new[] { "n!", "x²", "xʸ", "%" })
        {
            var key = CalcPalette.Main.Keys.First(k => k.Label == label);
            Assert.AreEqual(KeyInsert.Postfix, key.InsertKind, $"'{label}' should follow an operand");
        }
    }

    [TestMethod]
    [CoversNode("solver-key-insert")]
    public void TheKeysThatCausedComplaintsTypeWhatTheirCapSays()
    {
        Assert.AreEqual("!", CalcPalette.Main.Keys.First(k => k.Label == "n!").Insert,
            "n! should add a factorial, not spell out factorial(");
        Assert.AreEqual("%", CalcPalette.Main.Keys.First(k => k.Label == "%").Insert,
            "% should type % rather than silently dividing by 100");
    }

    [TestMethod]
    [CoversNode("solver-palette-pages")]
    public void TheShiftKeysSwapThePageAndComeBack()
    {
        var vm = Build();
        Assert.AreEqual(CalcPalette.Main.Id, vm.CalcPage.Id);

        Press(vm, CalcPalette.SecondPageId);
        Assert.AreEqual(CalcPalette.Second.Id, vm.CalcPage.Id);

        Press(vm, CalcPalette.SecondPageId);
        Assert.AreEqual(CalcPalette.Main.Id, vm.CalcPage.Id, "pressing 2nd again is how you get back");

        Press(vm, CalcPalette.ConstPageId);
        Assert.AreEqual(CalcPalette.Constants.Id, vm.CalcPage.Id);
    }

    [TestMethod]
    [CoversNode("solver-palette-pages")]
    public void EveryPageKeepsTheDigitsInTheSamePlace()
    {
        // Paging must never move the key you were about to press.
        var main = CalcPalette.Main.Keys.Select((k, i) => (k, i)).Where(x => x.k.Label == "7").Select(x => x.i).ToArray();
        foreach (var page in new[] { CalcPalette.Second, CalcPalette.Constants })
        {
            var at = page.Keys.Select((k, i) => (k, i)).Where(x => x.k.Label == "7").Select(x => x.i).ToArray();
            CollectionAssert.AreEqual(main, at, $"'7' moved on the {page.Label} page");
        }
    }

    [TestMethod]
    [CoversNode("solver-palette-toggle")]
    public void ThePaletteCanBeFoldedAway()
    {
        var vm = Build();
        Assert.IsTrue(vm.IsPaletteOpen);

        vm.TogglePaletteCommand.Execute(null);

        Assert.IsFalse(vm.IsPaletteOpen);
    }

    [TestMethod]
    [CoversNode("solver-palette-toggle")]
    public void TheSettingDecidesWhetherThePaletteStartsOpen()
        => Assert.IsFalse(Build(new SolverConfig { ShowPalette = false }).IsPaletteOpen);

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void DrillingReachesTheSymbolsThemselves()
    {
        // The navigator is the only way into the palette, so the drill has to end at something you
        // can type. If the last ring were still groups there would be nowhere for a symbol to live.
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;

        vm.SelectLatexTileCommand.Execute("latex.logic");
        Assert.IsTrue(vm.LatexTiles.All(t => t.IsGap || t.Opens), "a category's ring is its groups");

        vm.SelectLatexTileCommand.Execute("latex.logic.arrows");

        Assert.IsTrue(vm.LatexTiles.Any(t => t.Key?.Insert.Contains(@"\Rightarrow") == true),
            "the last ring must be the symbols, not another level of names");
        Assert.IsTrue(vm.LatexTiles.Where(t => !t.IsGap).All(t => !t.Opens));
        CollectionAssert.AreEqual(new[] { "Symbols", "Logic & sets", "Arrows" },
            vm.LatexCrumbs.Select(c => c.Label).ToArray());
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void ClickingASymbolTypesIt()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;
        PaletteKey? asked = null;
        vm.InsertRequested += key => asked = key;

        vm.SelectLatexTileCommand.Execute("latex.logic");
        vm.SelectLatexTileCommand.Execute("latex.logic.arrows");
        var arrow = vm.LatexTiles.First(t => !t.IsGap);
        vm.SelectLatexTileCommand.Execute(arrow.Id);

        Assert.IsNotNull(asked, "a symbol position types its key rather than drilling nowhere");
        Assert.AreEqual(arrow.Key, asked);
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void TheTopRingIsTheCategoriesAndHasNowhereFurtherUp()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;

        CollectionAssert.AreEqual(
            LatexPalette.Categories.Select(c => c.Id).ToArray(),
            vm.LatexTiles.Where(t => !t.IsGap).Select(t => t.Id).ToArray());
        Assert.IsFalse(vm.CanGoUpLatex, "the categories are the top; there is no mode above them");
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void TheCentreStepsBackUpAgain()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;
        var top = vm.LatexTiles.Select(t => t.Id).ToArray();

        vm.SelectLatexTileCommand.Execute("latex.greek");
        vm.SelectLatexTileCommand.Execute("latex.greek.lower1");
        Assert.IsTrue(vm.CanGoUpLatex);

        vm.LatexNavigateUpCommand.Execute(null);
        vm.LatexNavigateUpCommand.Execute(null);

        Assert.IsFalse(vm.CanGoUpLatex);
        CollectionAssert.AreEqual(top, vm.LatexTiles.Select(t => t.Id).ToArray(),
            "stepping back up must land exactly where it started");
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void ABreadcrumbStepGoesStraightBackToThatLevel()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;

        vm.SelectLatexTileCommand.Execute("latex.greek");
        vm.SelectLatexTileCommand.Execute("latex.greek.lower1");

        // Depth 1 is the category, so one click from the symbols rather than two off the centre.
        vm.NavigateToCrumbCommand.Execute(1);

        Assert.AreEqual("Greek", vm.LatexCentreLabel);
        CollectionAssert.AreEqual(new[] { "Symbols", "Greek" }, vm.LatexCrumbs.Select(c => c.Label).ToArray());

        vm.NavigateToCrumbCommand.Execute(0);
        Assert.IsFalse(vm.CanGoUpLatex, "the first step is the top of the tree");
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void TheStepYouAreOnLeadsNowhere()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;
        vm.SelectLatexTileCommand.Execute("latex.greek");

        var current = vm.LatexCrumbs.Single(c => c.IsCurrent);
        Assert.AreEqual("Greek", current.Label);

        vm.NavigateToCrumbCommand.Execute(current.Depth);
        Assert.AreEqual("Greek", vm.LatexCentreLabel, "clicking where you already are must not move you");
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void EveryRingHoldsExactlyEightPositions()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;
        Assert.AreEqual(LatexPalette.Slots, vm.LatexTiles.Count);

        foreach (var category in LatexPalette.Categories)
        {
            vm.SelectLatexTileCommand.Execute(category.Id);
            Assert.AreEqual(LatexPalette.Slots, vm.LatexTiles.Count, category.Label);

            foreach (var group in category.Children)
            {
                vm.SelectLatexTileCommand.Execute(group.Id);
                Assert.AreEqual(LatexPalette.Slots, vm.LatexTiles.Count, $"{category.Label} › {group.Label}");
                vm.LatexNavigateUpCommand.Execute(null);
            }

            vm.LatexNavigateUpCommand.Execute(null);
        }
    }

    [TestMethod]
    [CoversNode("solver-recent-keys")]
    public void UsingASymbolPutsItInTheRecentlyUsedStrip()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;
        Assert.IsFalse(vm.HasRecentKeys, "nothing used yet, so the strip should not be taking width");

        var alpha = LatexPalette.Find("latex.greek.lower1")!.Keys[0];
        vm.PressKeyCommand.Execute(alpha);

        Assert.IsTrue(vm.HasRecentKeys);
        CollectionAssert.AreEqual(new[] { alpha }, vm.RecentKeys.ToArray());
    }

    [TestMethod]
    [CoversNode("solver-recent-keys")]
    public void ReusingASymbolDoesNotMoveIt()
    {
        // The whole value of the strip is that it becomes a fixed set of targets. Re-ordering it on
        // every press would mean the button you just learned is somewhere else next time.
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;

        var keys = LatexPalette.Find("latex.greek.lower1")!.Keys;
        foreach (var key in keys.Take(3)) vm.PressKeyCommand.Execute(key);
        vm.PressKeyCommand.Execute(keys[0]);

        CollectionAssert.AreEqual(keys.Take(3).ToArray(), vm.RecentKeys.ToArray(),
            "re-using the first symbol must not promote it past the other two");
    }

    [TestMethod]
    [CoversNode("solver-recent-keys")]
    public void TheStripDropsTheLeastRecentlyUsedOnceItIsFull()
    {
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;

        var pool = SymbolPool().Take(SolverViewModel.RecentCapacity).ToList();
        foreach (var key in pool) vm.PressKeyCommand.Execute(key);
        Assert.AreEqual(SolverViewModel.RecentCapacity, vm.RecentKeys.Count);

        // Touch the oldest so it is no longer the oldest, then overflow by one.
        vm.PressKeyCommand.Execute(pool[0]);
        var extra = SymbolPool().First(k => !pool.Contains(k));
        vm.PressKeyCommand.Execute(extra);

        Assert.AreEqual(SolverViewModel.RecentCapacity, vm.RecentKeys.Count, "the strip is capped");
        Assert.IsTrue(vm.RecentKeys.Contains(pool[0]), "the one just re-used is not the stalest any more");
        Assert.IsFalse(vm.RecentKeys.Contains(pool[1]), "the stalest is what gets pushed out");
        Assert.IsTrue(vm.RecentKeys.Contains(extra));
    }

    [TestMethod]
    [CoversNode("solver-recent-keys")]
    public void ANewSymbolTakesOverTheStalestPositionRatherThanTheEnd()
    {
        // Appending after a removal slides everything past the evicted slot along by one, which is
        // the same disruption re-ordering causes — rarer, and so more surprising when it happens.
        var vm = Build();
        vm.Mode = DefinitionMode.Latex;

        var pool = SymbolPool().Take(SolverViewModel.RecentCapacity).ToList();
        foreach (var key in pool) vm.PressKeyCommand.Execute(key);

        vm.PressKeyCommand.Execute(pool[0]);                       // slot 1 is now the stalest
        var extra = SymbolPool().First(k => !pool.Contains(k));
        vm.PressKeyCommand.Execute(extra);

        Assert.AreEqual(extra, vm.RecentKeys[1], "the newcomer takes the vacated slot");
        Assert.AreEqual(pool[0], vm.RecentKeys[0], "and nothing else moves");
        Assert.AreEqual(pool[2], vm.RecentKeys[2]);
        Assert.AreEqual(pool[^1], vm.RecentKeys[^1]);
    }

    [TestMethod]
    [CoversNode("solver-recent-keys")]
    public void TheStripAndItsRecencySurviveAReopen()
    {
        var config = new SolverConfig();
        var first = Build(config);
        first.Mode = DefinitionMode.Latex;

        var pool = SymbolPool().Take(3).ToList();
        foreach (var key in pool) first.PressKeyCommand.Execute(key);
        first.PressKeyCommand.Execute(pool[0]);                    // pool[1] is now the stalest
        first.Dispose();                                           // what closing the tab does

        var reopened = Build(config);
        CollectionAssert.AreEqual(pool, reopened.RecentKeys.ToArray(),
            "a strip that emptied itself on restart would never be worth learning");

        // The recency came back too, so eviction picks up where it left off rather than guessing.
        reopened.Mode = DefinitionMode.Latex;
        while (reopened.RecentKeys.Count < SolverViewModel.RecentCapacity)
            reopened.PressKeyCommand.Execute(SymbolPool().First(k => !reopened.RecentKeys.Contains(k)));

        reopened.PressKeyCommand.Execute(SymbolPool().First(k => !reopened.RecentKeys.Contains(k)));
        Assert.IsFalse(reopened.RecentKeys.Contains(pool[1]), "the stalest from the previous session goes first");
        Assert.IsTrue(reopened.RecentKeys.Contains(pool[0]));
    }

    [TestMethod]
    [CoversNode("solver-recent-keys")]
    public void ASymbolNoLongerInTheTreeDoesNotComeBack()
    {
        var config = new SolverConfig
        {
            RecentSymbols = [new RecentSymbol { Insert = @"\thisWasRemoved ", LastUsed = DateTimeOffset.UtcNow }],
        };

        Assert.IsFalse(Build(config).HasRecentKeys,
            "the strip is resolved against the live tree, so a dropped symbol is simply not restored");
    }

    /// <summary>Distinct, pressable symbols from across the tree, in a stable order.</summary>
    private static IEnumerable<PaletteKey> SymbolPool()
        => LatexPalette.LeafGroups().SelectMany(g => g.Keys).Where(k => !k.IsBlank).Distinct();

    [TestMethod]
    [CoversNode("solver-recent-keys")]
    public void TheCalculatorPadDoesNotFeedTheStrip()
    {
        // The strip sits beside the navigator and only shows in Latex mode, so filling it with
        // digits pressed on the calculator would be storing something nobody can see.
        var vm = Build();
        vm.Mode = DefinitionMode.Calc;

        vm.PressKeyCommand.Execute(CalcPalette.Main.Keys.First(k => k.Label == "7"));

        Assert.IsFalse(vm.HasRecentKeys);
    }

    [TestMethod]
    [CoversNode("solver-symbol-grid")]
    public void TheKeypadIsTheCalculatorsAndTheNavigatorIsLatexs()
    {
        // Two different things, not two pages of one thing: the keypad is a fixed board of digits
        // and operators, and LaTeX has hundreds of symbols that only a tree can hold.
        var vm = Build();

        vm.Mode = DefinitionMode.Calc;
        Assert.IsTrue(vm.PaletteKeys.Any(k => k.Label == "7"), "the calculator pad belongs to the Calc tab");

        vm.Mode = DefinitionMode.Latex;
        Assert.IsTrue(vm.LatexTiles.Any(t => !t.IsGap), "and the navigator to the Latex one");
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void BackspaceAndClearActOnTheDefinition()
    {
        var vm = Build();
        vm.DefinitionText = "123";

        Press(vm, CalcPalette.BackspaceId);
        Assert.AreEqual("12", vm.DefinitionText);

        Press(vm, CalcPalette.ClearId);
        Assert.AreEqual(string.Empty, vm.DefinitionText);
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void BackspaceOnAnEmptyDefinitionIsHarmless()
    {
        var vm = Build();

        Press(vm, CalcPalette.BackspaceId);

        Assert.AreEqual(string.Empty, vm.DefinitionText);
    }

    // ── AI surface ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("solver-ai-context")]
    public async Task TheContextReportsWhatIsActuallyOnThePage()
    {
        var vm = Build();
        vm.DefinitionText = "4x + 3x";
        Assert.IsTrue(await WaitForChip(vm, "simplify"));

        var context = vm.GetContext();

        StringAssert.Contains(context, "4x + 3x");
        StringAssert.Contains(context, "simplify");
        StringAssert.Contains(context, "Calc");
    }

    [TestMethod]
    [CoversNode("solver-ai-context")]
    public void AnEmptyPageSaysSoRatherThanReportingAnEmptyFormula()
        => StringAssert.Contains(Build().GetContext(), "empty");

    [TestMethod]
    [CoversNode("solver-ai-act")]
    public void TheToolsAreNamespacedAndOnlyTheDestructiveOneNeedsApproval()
    {
        var tools = Build().GetClientTools();

        CollectionAssert.AreEquivalent(
            new[] { "solver_read_state", "solver_set_definition", "solver_run_chip" },
            tools.Select(t => t.Name).ToArray());

        Assert.IsTrue(tools.All(t => t.Name.StartsWith("solver_", StringComparison.Ordinal)),
            "a bare verb would collide with every other page's tools");

        Assert.AreEqual(ToolSafety.RequiresApproval, Tool(tools, "solver_set_definition").Safety,
            "it overwrites what the user typed, which cannot be recovered from here");
        Assert.AreEqual(ToolSafety.SafeOperation, Tool(tools, "solver_read_state").Safety);
    }

    [TestMethod]
    [CoversNode("solver-ai-act-read-state")]
    public async Task ReadStateReportsTheDefinitionAndTheResults()
    {
        var vm = Build();
        vm.DefinitionText = "2+2";
        Assert.IsTrue(await WaitForChip(vm, "="));
        await vm.Chips.First(c => c.Label == "=").RunCommand.ExecuteAsync(null);

        var result = await Tool(vm.GetClientTools(), "solver_read_state")
            .InvokeAsync([], CancellationToken.None);

        Assert.IsTrue(result.Success);
        StringAssert.Contains(result.ModelText, "2+2");
        StringAssert.Contains(result.ModelText, "4");
    }

    [TestMethod]
    [CoversNode("solver-ai-act-set-definition")]
    public async Task SetDefinitionReplacesTheDefinitionAndCanSwitchTab()
    {
        var vm = Build();

        var result = await Tool(vm.GetClientTools(), "solver_set_definition").InvokeAsync(
            new JsonObject { ["definition"] = @"\frac{1}{2}", ["mode"] = "latex" },
            CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(DefinitionMode.Latex, vm.Mode);
        Assert.AreEqual(@"\frac{1}{2}", vm.DefinitionText);
    }

    [TestMethod]
    [CoversNode("solver-ai-act-run-chip")]
    public async Task RunChipRunsAnOfferedChip()
    {
        var vm = Build();
        vm.DefinitionText = "4x + 3x";
        Assert.IsTrue(await WaitForChip(vm, "simplify"));

        var result = await Tool(vm.GetClientTools(), "solver_run_chip").InvokeAsync(
            new JsonObject { ["chip"] = "simplify" }, CancellationToken.None);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, vm.Results.Count);
        StringAssert.Contains(result.ModelText, "7 x");
    }

    [TestMethod]
    [CoversNode("solver-ai-act-run-chip")]
    public async Task RunChipNamesWhatIsAvailableWhenAskedForSomethingThatIsNot()
    {
        var vm = Build();
        vm.DefinitionText = "4x + 3x";
        Assert.IsTrue(await WaitForChip(vm, "simplify"));

        var result = await Tool(vm.GetClientTools(), "solver_run_chip").InvokeAsync(
            new JsonObject { ["chip"] = "integrate by parts" }, CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "simplify",
            "telling the model what it could have asked for is what stops it guessing again");
        Assert.AreEqual(0, vm.Results.Count);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static IClientTool Tool(IReadOnlyList<IClientTool> tools, string name)
        => tools.First(t => t.Name == name);

    private static void Press(SolverViewModel vm, string commandId)
        => vm.PressKeyCommand.Execute(new PaletteKey("x", string.Empty, string.Empty) { CommandId = commandId });

    private static string Labels(SolverViewModel vm)
        => string.Join(", ", vm.Chips.Select(c => c.Label));
}
