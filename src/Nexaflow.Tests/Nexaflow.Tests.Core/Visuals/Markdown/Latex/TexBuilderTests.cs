using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using WpfMath.Parsers;
using XamlMath;
using XamlMath.Rendering;
using WpfMath.Rendering;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// The formula built from our own reading, held against the one the typesetter's parser builds.
///
/// <para>
/// This is the point of ingesting the engine. Its parser reads LaTeX and decides what the reading should
/// be set as in one pass, and by the time an atom exists the braces and the spacing are gone — fine for
/// drawing a formula once, no good for editing one. <see cref="TexFormulaBuilder"/> does only the second
/// half, from a reading that kept all of it, and hangs the parse-tree part on every atom it makes.
/// </para>
/// <para>
/// So the boxes will know what they are without anything matching spans afterwards. But only if the two
/// build the <em>same formula</em> — otherwise what renders stops being what the editor thinks it is
/// looking at, which is the disagreement this whole exercise is removing. These say they do.
/// </para>
/// <para>
/// It is deliberately all-or-nothing per formula: a construct the builder does not know yet makes it
/// return nothing and the parser is used for that formula instead. So the corpus reports two numbers —
/// how much it can build, and whether everything it built agrees. The first grows; the second must
/// stay at all of it.
/// </para>
///
/// Needs an STA thread for the parser's brushes. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("maths-typesetting")]
public class TexBuilderTests
{
    private const double Scale = 16;

    /// <summary>What the builder is expected to manage today. It grows as constructs are taught to it.</summary>
    private static readonly string[] Known =
    [
        "a",
        "a+b",
        "a + b",
        "x^2",
        "x_i",
        "x^2_i",
        "x^{2}",
        @"\alpha",
        @"\alpha + \beta",
        "{a}",
        "{a+b}",
        @"\frac{a}{b}",
        @"\frac{a+b}{c}",
        @"\frac{1}{1 + \frac{1}{x}}",
        @"\sqrt{x}",
        @"\sqrt{x+1}",
        @"\frac{\sqrt{a}}{b}",
        "2x^{2} + 3",
        @"\alpha^{\beta}",
    ];

    [TestMethod]
    public void EverythingItClaimsToKnowItCanBuild() => UiThread.Run(() =>
    {
        foreach (var latex in Known)
            Assert.IsNotNull(TexFormulaBuilder.Build(TexReading.Of(latex)), latex);
    });

    [TestMethod]
    public void AndSetsItWhereTheParserSetsIt() => UiThread.Run(() =>
    {
        // Against the geometry, not against the atoms. The two trees are not the same shape and are not
        // meant to be: the parser wraps things in atoms of its own bookkeeping — a lone `a` comes back
        // as a TypedAtom around a CharAtom — and those wrappers exist to carry decisions this builder
        // makes differently or not at all. What has to match is where everything ends up on the page,
        // because that is what the reader sees and what every position in the editor is measured in.
        foreach (var latex in Known)
        {
            var ours = TexFormulaBuilder.Build(TexReading.Of(latex));
            Assert.IsNotNull(ours, latex);

            Assert.AreEqual(Settled(WpfTeXFormulaParser.Instance.Parse(latex), latex), Settled(ours, latex), latex);
        }
    });

    [TestMethod]
    public void EveryAtomKnowsWhichPartOfTheSourceItWasBuiltFrom() => UiThread.Run(() =>
    {
        // What none of this is possible without, and what the parser can never provide: an atom that
        // came from a reading which still knows where every brace was.
        var reading = TexReading.Of(@"\frac{a}{b}");
        var formula = TexFormulaBuilder.Build(reading);
        Assert.IsNotNull(formula);

        foreach (var atom in Parts(formula.Root!))
            Assert.IsNotNull(atom.Origin, $"{atom.GetType().Name} was built from nothing");

        // The numerator's atom is the `a` itself — a group holding one thing is that thing, here as in
        // the parser, because a row of one would put every box inside a box. So the part it names is the
        // letter, and the group that makes it a numerator is what holds that letter.
        var numerator = formula.Root!.Slots[0].Node.Origin!;

        Assert.AreEqual("a", numerator.Node.Print());
        Assert.AreEqual(TexRole.Numerator, numerator.Parent!.Role,
            "and what holds it is what the writer braced");
    });

    [TestMethod]
    public void WhatItDoesNotKnowItDeclines() => UiThread.Run(() =>
    {
        // Half a formula built each way would mix two readings of the same source, which is the thing
        // being got rid of. Declining is what keeps the fallback honest.
        foreach (var latex in new[] { @"\left( a \right)", @"\begin{matrix} a & b \end{matrix}",
                                      @"\overline{x}", @"\sqrt[3]{x}", @"\textcolor{red}{a}" })
            Assert.IsNull(TexFormulaBuilder.Build(TexReading.Of(latex)), latex);
    });

    [TestMethod]
    public void ARealCorpusSaysHowFarItGetsAndThatItIsRight()
    {
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS");
        if (string.IsNullOrWhiteSpace(corpus) || !File.Exists(corpus))
            Assert.Inconclusive($"set NEXAFLOW_LATEX_CORPUS to a file of formulas (got: {corpus ?? "nothing"})");

        var stride = int.TryParse(Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS_STRIDE"), out var s)
            ? Math.Max(s, 1)
            : 1;

        var line = 0;
        var seen = 0;
        var built = 0;
        var wrong = new List<string>();

        UiThread.Run(() =>
        {
            foreach (var raw in File.ReadLines(corpus))
            {
                if (line++ % stride != 0) continue;

                var latex = raw.Trim();
                if (latex.Length == 0) continue;

                seen++;
                if (TexFormulaBuilder.Build(TexReading.Of(latex)) is not { } ours) continue;

                built++;

                TexFormula? theirs = null;
                try { theirs = WpfTeXFormulaParser.Instance.Parse(latex); } catch { continue; }

                if (Settled(theirs, latex) != Settled(ours, latex) && wrong.Count < 10)
                    wrong.Add($"line {line}: {latex}");
            }
        });

        Assert.IsTrue(seen > 1000, $"only {seen} formula(s) in {corpus}");
        Assert.AreEqual(0, wrong.Count,
            $"built {built} of {seen}, and these disagree:\n" + string.Join("\n", wrong));

        // Written down rather than asserted. How much of the corpus the builder reaches is a number to
        // watch go up as constructs are taught to it, not a bar to clear — a floor here would either sit
        // so low it never fires or have to be edited every time the builder learns something.
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(corpus)!, "tex-builder-coverage.txt"),
            $"built {built} of {seen} ({100.0 * built / seen:F1}%), and every one of them set where the "
            + "parser sets it");
    }

    /// <summary>
    /// Where a formula's every piece ends up on the page — what both readings have to agree about.
    /// <para>
    /// Typeset and captured, exactly as the editor does it, then written out as what each piece was
    /// drawn from and the rectangle it occupies. Geometry rather than pixels: the numbers are arithmetic
    /// over font metrics, so they are the same on any machine, where rasterising is not.
    /// </para>
    /// </summary>
    private static string Settled(TexFormula formula, string latex)
    {
        var capture = new Nexaflow.Visuals.Text.Markdown.Latex.LatexLayoutCapture(Scale, latex);
        formula.RenderTo(capture, WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale), 0, 0);
        capture.FinishRendering();

        Assert.IsNotNull(capture.Root, $"nothing was drawn for {latex}");

        var text = new StringBuilder();

        foreach (var node in capture.Root.SelfAndDescendants())
            text.Append(node.Kind).Append(' ')
                .Append(Number(node.Bounds.X)).Append(',').Append(Number(node.Bounds.Y)).Append(' ')
                .Append(Number(node.Bounds.Width)).Append('x').Append(Number(node.Bounds.Height))
                .Append('\n');

        return text.ToString();
    }

    private static string Number(double value) =>
        value.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<IFormulaNode> Parts(IFormulaNode node)
    {
        yield return node;

        foreach (var slot in node.Slots)
            foreach (var inner in Parts(slot.Node))
                yield return inner;
    }
}
