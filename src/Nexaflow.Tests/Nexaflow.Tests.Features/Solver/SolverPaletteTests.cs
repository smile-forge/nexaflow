using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Features.Solver;
using Nexaflow.Features.Solver.Palette;
using Nexaflow.Features.Solver.Solving;
using Nexaflow.Features.Solver.Views;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Features.Solver;

/// <summary>
/// The palette's content, which is data rather than UI and is therefore checkable without a window.
/// <para>
/// The thing worth guarding is that a key <b>does what its cap says</b>. A palette exists so you do
/// not have to remember a command; a key whose glyph and insertion disagree is worse than no key,
/// because it is wrong in a way that looks right until the answer comes back odd.
/// </para>
/// </summary>
[TestClass]
public class SolverPaletteTests
{
    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void EveryCalculatorPageFillsWholeRows()
    {
        foreach (var page in CalcPalette.Pages)
            Assert.AreEqual(0, page.Keys.Count % CalcPalette.Columns,
                $"the {page.Label} page has a ragged last row ({page.Keys.Count} keys over {CalcPalette.Columns} columns)");
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void EveryKeyEitherTypesSomethingOrDoesSomething()
    {
        foreach (var page in CalcPalette.Pages)
            foreach (var key in page.Keys)
                Assert.IsTrue(key.Insert.Length > 0 || key.IsCommand,
                    $"'{key.Label}' on the {page.Label} page does nothing at all");
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void EveryKeySaysWhatItDoes()
    {
        foreach (var page in CalcPalette.Pages)
            foreach (var key in page.Keys)
                Assert.IsTrue(key.Tooltip.Length > 0, $"'{key.Label}' on the {page.Label} page has no tooltip");
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void TheDigitsAreAllPresentAndTypeThemselves()
    {
        foreach (var d in "0123456789")
        {
            var key = CalcPalette.Main.Keys.FirstOrDefault(k => k.Label == d.ToString());
            Assert.IsNotNull(key, $"the keypad has no '{d}'");
            Assert.AreEqual(d.ToString(), key.Insert);
        }
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void WhatTheCalculatorKeysTypeIsSomethingTheEngineCanRead()
    {
        // A key cap is a promise. Press √ then 9 then ) and the answer should be 3 — which only
        // holds if "sqrt(" is really what the engine calls that function.
        foreach (var (keys, expected) in new (string[], string)[]
        {
            (["√", "9", ")"], "3"),
            (["7", "+", "3"], "10"),
            (["x²"], null!),                              // needs an operand — checked below instead
        })
        {
            if (expected is null) continue;

            var text = string.Concat(keys.Select(label => Key(label).Insert));
            var input = new SolverInput(DefinitionMode.Calc, text);

            Assert.IsTrue(ExpressionParser.TryParse(input, AngleUnit.Radians, out var parsed),
                $"'{text}' (from {string.Join(" ", keys)}) does not parse");
            StringAssert.Contains(ExpressionParser.Plain(parsed.Entity.EvalNumerical()), expected);
        }

        static PaletteKey Key(string label)
            => CalcPalette.Main.Keys.First(k => k.Label == label);
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void TheFunctionKeysNameFunctionsTheEngineHas()
    {
        foreach (var page in CalcPalette.Pages)
            foreach (var key in page.Keys.Where(k => k.Kind == PaletteKeyKind.Function && k.Insert.EndsWith("(")))
            {
                var text = key.Insert + "0.5)";
                var input = new SolverInput(DefinitionMode.Calc, text);
                Assert.IsTrue(ExpressionParser.TryParse(input, AngleUnit.Radians, out _),
                    $"'{key.Label}' inserts '{key.Insert}', which the engine cannot parse");
            }
    }

    [TestMethod]
    [CoversNode("solver-calc-keypad")]
    public void TheConstantKeysAreWorthTheirCaps()
    {
        foreach (var (label, expected) in new[] { ("π", 3.14159), ("e", 2.71828), ("τ", 6.28318), ("√2", 1.41421) })
        {
            var key = CalcPalette.Constants.Keys.First(k => k.Label == label);
            var input = new SolverInput(DefinitionMode.Calc, key.Insert);

            Assert.IsTrue(ExpressionParser.TryParse(input, AngleUnit.Radians, out var parsed), key.Insert);
            var value = double.Parse(
                ExpressionParser.Plain(parsed.Entity.EvalNumerical())[..7],
                System.Globalization.CultureInfo.InvariantCulture);

            Assert.IsTrue(Math.Abs(value - expected) < 0.001, $"'{label}' is {value}, expected about {expected}");
        }
    }

    [TestMethod]
    [CoversNode("solver-symbol-grid")]
    public void EverySymbolKeyInsertsALatexCommand()
    {
        foreach (var group in AllGroups())
            foreach (var key in group.Keys.Where(k => !k.IsBlank))
                Assert.IsTrue(key.Insert.Trim().Length > 0, $"'{key.Label}' in {group.Label} inserts nothing");
    }

    [TestMethod]
    [CoversNode("solver-symbol-grid")]
    public void NoLatexKeyRunsIntoTheNextOne()
    {
        // Two commands run together read as one unknown command; they need a separator.
        foreach (var group in AllGroups())
            foreach (var key in group.Keys.Where(k => k.Insert.StartsWith(@"\") && k.CaretBack == 0
                                                      && k.InsertKind == KeyInsert.Literal))
                Assert.IsTrue(key.Insert.EndsWith(" ") || key.Insert.EndsWith("}") || key.Insert.EndsWith("\n"),
                    $"'{key.Label}' inserts '{key.Insert}' with nothing to separate it from what follows");
    }

    /// <summary>
    /// What TeX accepts only inside an alignment — a table, a matrix, cases — so a key writing one is pressed inside one.
    /// </summary>
    private static readonly string[] AlignmentOnly = ["&", @"\\", @"\hline", @"\hdotsfor"];

    [TestMethod]
    [CoversNode("solver-symbol-grid")]
    public void EveryLatexKeyWritesSomethingTheFormulaSetsAsMaths() => UiThread.Run(() =>
    {
        // A key is pressed into an empty definition and laid out as the editor lays it, holes and all: nothing it writes may
        // be unreadable, read but undrawable, or drop the formula back to its source. Then its empty arguments are filled
        // and the finished formula is laid out read-only, as an answer would be, where nothing at all may be reported.
        var wrong = new List<string>();

        foreach (var group in LatexPalette.LeafGroups())
            foreach (var key in group.Keys.Where(k => !k.IsBlank))
            {
                var written = key.InsertKind == KeyInsert.Wrapping ? key.Insert + key.Close : key.Insert;
                var formula = AlignmentOnly.Any(written.TrimStart().StartsWith)
                    ? "\\begin{array}{cc}\n a & b \\\\\n" + written + "\n\\end{array}"
                    : written;

                var engine = new ContentEngine(WpfMedium.OnThisThread, WpfStyles.Dark);
                var reading = engine.Read("latex", formula, writing: true).Root;
                var writing = engine.Lay("latex", EditState.For(formula), WpfStyles.Dark, double.PositiveInfinity, readOnly: false);

                var said = reading.SelfAndDescendants()
                    .Where(part => part.Trouble is not null && part.Kind != Kinds.Hole)
                    .Select(part => $"{part.Print()}: {part.Trouble}")
                    .Concat(writing.Trouble.Where(trouble => trouble.Severity == LayoutSeverity.Warning)
                                           .Select(trouble => $"{formula.Substring(trouble.Start, trouble.Length)}: {trouble.Message}"))
                    .ToList();
                if (writing.ShowsSource) said.Add("shown as its source");

                var finished = formula.Replace("{}", "{x}").Replace("[]", "[n]").Replace("()", "(x)");
                var answer = new ContentEngine(WpfMedium.OnThisThread, WpfStyles.Dark).Lay("latex", EditState.For(finished), WpfStyles.Dark, double.PositiveInfinity, readOnly: true);
                said.AddRange(answer.Trouble.Select(trouble => $"once filled in as {finished}: {trouble.Message}"));
                if (answer.ShowsSource) said.Add($"once filled in as {finished}: shown as its source");

                if (said.Count > 0) wrong.Add($"{group.Id} '{key.Label}' writes {written} — {string.Join("; ", said)}");
            }

        Assert.AreEqual(0, wrong.Count, "\n" + string.Join("\n", wrong));
    });

    [TestMethod]
    [CoversNode("solver-symbol-grid")]
    public void AStructuralKeyPutsTheCaretInsideItsBraces()
    {
        foreach (var group in AllGroups())
            foreach (var key in group.Keys.Where(k => k.CaretBack > 0))
                Assert.IsTrue(key.CaretBack <= key.Insert.Length,
                    $"'{key.Label}' walks the caret back {key.CaretBack} through an insertion of {key.Insert.Length}");
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void EveryLevelOfTheTreeFitsTheNavigatorExactly()
    {
        // The navigator draws a fixed ring of eight positions and is the only way into the palette.
        // More than eight and the surplus is simply dropped — a category nobody can reach — so the
        // tree is authored to the ring rather than the ring stretched to the tree, which is what the
        // sunburst it replaced did and why every label came out three characters long.
        Assert.AreEqual(OctagonNavigator.MaxNodes, LatexPalette.Categories.Count,
            "the categories are the top ring, so there must be exactly a ring of them");

        foreach (var category in LatexPalette.Categories)
        {
            Assert.IsTrue(category.Children.Count is > 0 and <= OctagonNavigator.MaxNodes,
                $"{category.Label} has {category.Children.Count} groups; the ring holds {OctagonNavigator.MaxNodes}");

            foreach (var group in category.Children)
                Assert.AreEqual(OctagonNavigator.MaxNodes, group.Keys.Count,
                    $"{category.Label} › {group.Label} has {group.Keys.Count} symbols, not a whole ring");
        }
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void TheTreeIsExactlyThreeLevelsDeep()
    {
        // Category, group, symbol. A fourth level would mean four clicks to a glyph, and a group
        // holding both symbols and subgroups would put two different kinds of thing on one ring.
        foreach (var category in LatexPalette.Categories)
        {
            Assert.AreEqual(0, category.Keys.Count,
                $"{category.Label} carries symbols directly, so its ring would be a mixture");

            foreach (var group in category.Children)
                Assert.AreEqual(0, group.Children.Count,
                    $"{category.Label} › {group.Label} has subgroups, which would make the tree four deep");
        }
    }

    [TestMethod]
    [CoversNode("solver-symbol-grid")]
    public void APaddingSlotIsNeverPressable()
    {
        foreach (var key in AllGroups().SelectMany(g => g.Keys).Where(k => k.IsBlank))
        {
            Assert.AreEqual(string.Empty, key.Insert, "a blank that types something is not a blank");
            Assert.IsFalse(key.IsCommand, "a blank that runs a command is not a blank");
        }
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void EveryGroupIsReachableByTheIdTheNavigatorReports()
    {
        foreach (var category in LatexPalette.Categories)
        {
            Assert.AreSame(category, LatexPalette.Find(category.Id), category.Id);
            foreach (var group in category.Children)
                Assert.AreSame(group, LatexPalette.Find(group.Id), group.Id);
        }
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void GroupIdsAreUnique()
    {
        var ids = LatexPalette.Categories
            .SelectMany(c => new[] { c.Id }.Concat(c.Children.Select(g => g.Id)))
            .ToList();

        CollectionAssert.AreEquivalent(ids.Distinct().ToArray(), ids.ToArray(),
            "a duplicate id makes one tile navigate to the other's symbols");
    }

    [TestMethod]
    [CoversNode("solver-symbol-sunburst")]
    public void NoGroupIdContainsTheSymbolSeparator()
    {
        // A symbol position is addressed as "<groupId>#<slot>", so a '#' in a group id would make
        // the two indistinguishable and a category click would try to type something.
        foreach (var category in LatexPalette.Categories)
        {
            StringAssert.DoesNotMatch(category.Id, new System.Text.RegularExpressions.Regex("#"));
            foreach (var group in category.Children)
                StringAssert.DoesNotMatch(group.Id, new System.Text.RegularExpressions.Regex("#"));
        }
    }

    /// <summary>Every group that actually holds symbols, across all eight categories.</summary>
    private static IEnumerable<PaletteGroup> AllGroups() => LatexPalette.LeafGroups();

    [TestMethod]
    [CoversNode("solver-config")]
    public void TheSettingsParseIntoWhatTheSolversAreGiven()
    {
        var config = new SolverConfig { StartMode = "Latex", Angles = "Radians", DecimalPlaces = "10" };

        Assert.AreEqual(DefinitionMode.Latex, config.GetStartMode());
        Assert.AreEqual(AngleUnit.Radians, config.GetAngleUnit());
        Assert.AreEqual(10, config.GetDecimalPlaces());
    }

    [TestMethod]
    [CoversNode("solver-config")]
    public void NonsenseInSettingsFallsBackRatherThanThrowing()
    {
        var config = new SolverConfig { StartMode = "Wingdings", Angles = "Gradians", DecimalPlaces = "banana" };

        Assert.AreEqual(DefinitionMode.Calc, config.GetStartMode());
        Assert.AreEqual(AngleUnit.Degrees, config.GetAngleUnit());
        Assert.AreEqual(6, config.GetDecimalPlaces());
    }

    [TestMethod]
    [CoversNode("solver-config")]
    public void DegreesIsTheDefault()
        => Assert.AreEqual(AngleUnit.Degrees, new SolverConfig().GetAngleUnit(),
            "the calculator is what most people open this for, and sin(45) meaning radians is the rarer intent");

    [TestMethod]
    [CoversNode("solver-config")]
    public void EverySettingsOptionIsOneTheParserAccepts()
    {
        foreach (var mode in SolverConfig.GetModeOptions().Select(o => o.Value))
            Assert.AreEqual(mode, new SolverConfig { StartMode = mode }.GetStartMode().ToString());

        foreach (var places in SolverConfig.GetDecimalOptions())
            Assert.AreEqual(int.Parse(places), new SolverConfig { DecimalPlaces = places }.GetDecimalPlaces());
    }
}
