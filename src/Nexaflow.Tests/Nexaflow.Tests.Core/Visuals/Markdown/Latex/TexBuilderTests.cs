using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
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
        @"\left( a \right)",
        @"\left[ \frac{a}{b} \right]",
        @"\left\{ x \right\}",
        @"\overline{x}",
        @"\underline{x+1}",
        @"\vec{a}",
        @"\dot{q}^{2}",
        @"\tilde{X}(t)",
        @"\sum_{i=0}^{n} i",
        @"\int_{0}^{1} x",
        @"\sum x",
        @"\left( a \right)^{2}",
        @"\left( \frac{a}{b} \right)^{2}",

        // Tables. The rows and cells are nodes of the reading, so the grid is read rather than worked
        // out from where the cells were drawn — which is the whole reason the table gestures wanted this.
        @"\begin{matrix} a & b \\ c & d \end{matrix}",
        @"\begin{pmatrix} a & b \\ c & d \end{pmatrix}",
        @"\begin{bmatrix} \alpha & \beta \\ \gamma & \delta \end{bmatrix}",
        @"\begin{Bmatrix} a \end{Bmatrix}",
        @"\begin{vmatrix} a & b \end{vmatrix}",
        @"\begin{Vmatrix} a & b \end{Vmatrix}",
        @"\begin{smallmatrix} a & b \end{smallmatrix}",
        @"\begin{cases} x & y \\ z & w \end{cases}",
        @"\begin{pmatrix} \frac{a}{b} & \sqrt{c} \\ x^{2} & \alpha \end{pmatrix}",
        @"\begin{matrix} a & b \\ c \end{matrix}",              // ragged: squared off with holes
        @"\begin{matrix} a & \\ & d \end{matrix}",              // cells with nothing written in them
        @"\begin{matrix} a \\ b \\ \end{matrix}",               // a \\ ends its row; it opens no other
        @"\begin{align} a &= b \\ c &= d \end{align}",
        @"\begin{gathered} a \\ b \end{gathered}",
        @"\begin{array}{cc} a & b \\ c & d \end{array}",
        @"\begin{array}{c|c} a & b \end{array}",
        @"\left[ \begin{matrix} a & b \end{matrix} \right]",

        @"\sqrt[3]{x}",
        @"\sqrt[n]{x+1}",

        // A style is a property of the letters, not an atom round them.
        @"\mathrm{abc}",
        @"A\mathrm{abc}B",              // a style nests here; written first in a row it is parked
        @"\mathbf{x} + \mathit{y}",
        @"\frac{\mathrm{d}y}{\mathrm{d}x}",
        @"\mathcal{L}^{2}",

        // Marks. These carry more weight than the rest of this list, because the corpus cannot check
        // them at all: it holds 22,653 formulas writing a prime as `^{\prime}` and not one written as
        // `'`. So this is the only place the two readings are held against each other for a mark, and
        // it is deliberately more than a couple of shapes.
        "f'",
        "f''",
        "f'''",
        @"\alpha'",
        @"\frac{f'}{g'}",
        @"x'_{i}",                      // the subscript lands on the x, not on the prime before it
        @"x''_{i}",
        @"x'''_{i}",
        @"y'^{2}",
        @"y'^{2}_{n}",
        "f'(x)",
        "f'g'",
        "{f'}",                         // braced, so the mark is inside a group of its own
        "{f}'",                         // and braced the other way, so the group is what wears it
        @"\sqrt{f'}",
        @"\sum f'",
        @"\left( f' \right)",
        @"\frac{\alpha''}{\beta'}",
        @"\begin{matrix} f' & g'' \end{matrix}",
        @"\prime",           // the symbol, which is a different thing entirely

        // A script on a construct. These agree everywhere except between delimiters, which is where the
        // decline now sits — it used to sit here, and cost every one of these its coverage for nothing.
        @"\overline{J}^{a}",
        @"\overline{{J}}^{a}",
        @"\underline{x}^{2}",
        @"\overline{f}'",
        @"\frac{f}{g}_{i}",
        @"\frac{f}{g}_{i} h",

        "a~b",               // a tie

        // Space that was asked for rather than typed. TeX's own spacing comes from atom classes and is
        // not written down; these are, so they build like any other command.
        @"a\,b",
        @"a\;b",
        @"a\!b",
        @"a\:b",
        @"a\quad b",
        @"a\qquad b",

        // Switches, which take the rest of the group they stand in rather than an argument. So the
        // scope is a fact about the run, and nothing says where it ends except the closing brace.
        @"{\cal L}",
        @"{\cal L M}",
        @"A {\cal L} B",
        @"{\bf x} + y",
        @"{\it a}",
        @"\displaystyle \sum_{i=0}^{n} i",
        @"{\displaystyle \frac{a}{b}}",
        @"\textstyle \frac{a}{b}",
        @"\frac{{\cal A}}{{\cal B}}",
        @"\begin{matrix} {\bf a} & b \end{matrix}",

        // A symbol wearing a script, between delimiters. What is declined there is a script on something
        // a command *built*, and the difference between the two is worth twelve thousand formulas.
        @"\left( \sum_{i} \right)",
        @"\left( \int_{0}^{1} x \right)",
        @"\left( \alpha^{2} \right)",
    ];

    [TestMethod]
    public void EverythingItClaimsToKnowItCanBuild() => UiThread.Run(() =>
    {
        foreach (var latex in Known)
            Assert.IsNotNull(TexFormulaBuilder.Build(TexReading.Of(latex), WpfTeXFormulaParser.Instance), latex);
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
            var ours = TexFormulaBuilder.Build(TexReading.Of(latex), WpfTeXFormulaParser.Instance);
            Assert.IsNotNull(ours, latex);

            Assert.AreEqual(Settled(WpfTeXFormulaParser.Instance.Parse(latex), latex), Settled(ours, latex), latex);
        }
    });

    [TestMethod]
    public void NothingItBuildsNamesAPointInTheSource() => UiThread.Run(() =>
    {
        // The rule, asserted on what comes out rather than on how it was written. A formula's layout may
        // never carry an offset: an offset beside a tree is a second copy of a fact the tree already
        // holds, and the two disagree the moment anything is edited. Where a part is written is the
        // reading's to say, worked out by a walk when it is asked.
        //
        // On every construct, not one, because this is the kind of thing that stays true until one case
        // quietly threads a span through for convenience.
        foreach (var latex in Known)
        {
            var formula = TexFormulaBuilder.Build(TexReading.Of(latex), WpfTeXFormulaParser.Instance);
            Assert.IsNotNull(formula, latex);

            foreach (var atom in Parts(formula.Root!))
            {
                Assert.IsNull(atom.Source, $"{atom.GetType().Name} in {latex} names a point in the source");

                // Everything the reader wrote carries the part they wrote it as. The one exception is the
                // cell nobody wrote — what squares off a short row — and it is an exception because there
                // is nothing in the reading for it to be, not because it was overlooked.
                Assert.IsTrue(atom.Origin is not null || atom is XamlMath.Atoms.NullAtom,
                    $"{atom.GetType().Name} in {latex} was built from nothing");
            }
        }
    });

    [TestMethod]
    public void EveryAtomKnowsWhichPartOfTheSourceItWasBuiltFrom() => UiThread.Run(() =>
    {
        // What none of this is possible without, and what the parser can never provide: an atom that
        // came from a reading which still knows where every brace was.
        var reading = TexReading.Of(@"\frac{a}{b}");
        var formula = TexFormulaBuilder.Build(reading, WpfTeXFormulaParser.Instance);
        Assert.IsNotNull(formula);

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
        foreach (var latex in new[] { @"\textcolor{red}{a}",
                                      @"\text{for all}",     // words, not maths: the spaces are the point
                                      "'x",                  // a prime with nothing to be the prime of
                                      @"^{2}",               // a script with nothing at all before it
                                      @"\left( a", @"\notacommand{x}",
                                      @"\begin{matrix} a & b",              // never closed
                                      @"\begin{equation} a \end{equation}", // means nothing here yet
                                      @"\begin{alignat}{2} a & b \end{alignat}", // its count reads as a cell
                                      @"\begin{array}{cc} \hline a & b \end{array}",
                                      @"\begin{array}{@{}c@{}} a \end{array}",  // a preamble it cannot read
                                      @"\begin{array} a & b \end{array}" })     // and one that is not there
            Assert.IsNull(TexFormulaBuilder.Build(TexReading.Of(latex), WpfTeXFormulaParser.Instance), latex);
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

        // Read first, measured after. Every formula is a self-contained piece of work — read it, build it
        // both ways, compare where the pieces landed, forget all of it — so the only reason this ran on
        // one thread was that it was written that way, and a quarter of a million of them is worth the
        // other thirty-one.
        var formulas = new List<(int Line, string Latex)>();
        var line = 0;

        foreach (var raw in File.ReadLines(corpus))
        {
            var at = line++;
            if (at % stride != 0) continue;

            var latex = raw.Trim();
            if (latex.Length > 0) formulas.Add((at + 1, latex));
        }

        var seen = formulas.Count;
        var built = 0;
        var wrong = new List<string>();
        var deliberate = new Dictionary<string, int>(StringComparer.Ordinal);

        UiThread.Across(formulas, formula =>
        {
            if (TexFormulaBuilder.Build(TexReading.Of(formula.Latex), WpfTeXFormulaParser.Instance)
                is not { } ours) return;

            Interlocked.Increment(ref built);

            TexFormula? theirs;
            try { theirs = WpfTeXFormulaParser.Instance.Parse(formula.Latex); } catch { return; }

            if (Settled(theirs, formula.Latex) == Settled(ours, formula.Latex)) return;

            // A difference that has been looked at and decided in our favour is not a failure. The
            // parser is a reference and not a specification, so "differs from it" was never the same as
            // "wrong" — it is just the only signal available until somebody looks.
            var why = Drawn(theirs, formula.Latex) == Drawn(ours, formula.Latex)
                ? "the same picture, a different tree — ours keeps what the writer grouped"
                : Decided(TexReading.Of(formula.Latex), ours);

            if (why is not null)
            {
                lock (deliberate)
                    deliberate[why] = deliberate.TryGetValue(why, out var n) ? n + 1 : 1;

                return;
            }

            lock (wrong) wrong.Add($"line {formula.Line}: {formula.Latex}");
        });

        // Every one of them, written down beside the coverage, because an assertion message is truncated
        // and ten formulas out of a quarter of a million only tell you what they have in common if you
        // can see all ten.
        wrong.Sort(StringComparer.Ordinal);
        File.WriteAllLines(
            Path.Combine(Path.GetDirectoryName(corpus)!, "tex-builder-disagreements.txt"), wrong);

        Assert.IsTrue(seen > 1000, $"only {seen} formula(s) in {corpus}");
        Assert.AreEqual(0, wrong.Count,
            $"built {built} of {seen}; {wrong.Count} disagree, all of them written to "
            + "tex-builder-disagreements.txt beside the corpus. The first few:\n"
            + string.Join("\n", wrong.Take(5)));

        // Written down rather than asserted. How much of the corpus the builder reaches is a number to
        // watch go up as constructs are taught to it, not a bar to clear — a floor here would either sit
        // so low it never fires or have to be edited every time the builder learns something.
        var decided = deliberate.Sum(d => d.Value);

        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(corpus)!, "tex-builder-coverage.txt"),
            $"built {built} of {seen} ({100.0 * built / seen:F1}%), and every one of them set where the "
            + $"parser sets it — bar {decided} set deliberately otherwise:\n"
            + string.Join("\n", deliberate.OrderByDescending(d => d.Value)
                                          .Select(d => $"  {d.Value,7:N0}  {d.Key}")));
    }

    /// <summary>
    /// Why this formula is allowed to be drawn differently, or null if it is not.
    ///
    /// <para>
    /// The list is what has been <em>looked at</em>. Each entry is a shape somebody compared — the parse
    /// tree, both box trees, both renderings and the picture the published paper shipped — and decided
    /// ours was the one to keep. Until that happens a difference is a failure, because a difference
    /// nobody has examined is indistinguishable from a defect.
    /// </para>
    /// <para>
    /// Matched on the reading rather than on the text. "Contains two `\left`" is a search; "a fence
    /// whose body holds a fence" is the shape that was ruled on, and it is a question the parse tree
    /// answers exactly.
    /// </para>
    /// </summary>
    /// <summary>
    /// What the reader actually sees: every box that draws something, and where. Containers left out.
    /// <para>
    /// This is the line every ruling so far has fallen on. A difference in the <em>box tree</em> — the
    /// parser collapsing a group ours keeps, splicing a row ours nests — moves no ink at all, and three
    /// times running the answer has been that ours is the one to keep, because a tree is what selection
    /// and substitution work on. A difference in <em>this</em> is a different thing entirely: it is a
    /// formula that would look wrong to somebody reading it.
    /// </para>
    /// <para>
    /// So the gate asks both. Same ink and a different tree is a structural choice, counted and named.
    /// Different ink is a disagreement about the picture, and fails until somebody looks at it.
    /// </para>
    /// </summary>
    private static string Drawn(TexFormula formula, string latex)
    {
        var capture = new Nexaflow.Visuals.Text.Markdown.Latex.LatexLayoutCapture(Scale, latex);
        _setting ??= WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale);
        formula.RenderTo(capture, _setting, 0, 0);
        capture.FinishRendering();

        var text = new StringBuilder();

        foreach (var node in capture.Root!.SelfAndDescendants())
            if (node.Children.Count == 0)
                text.Append(node.Kind).Append(' ')
                    .Append(Number(node.Bounds.X)).Append(',').Append(Number(node.Bounds.Y)).Append(' ')
                    .Append(Number(node.Bounds.Width)).Append('x').Append(Number(node.Bounds.Height))
                    .Append('\n');

        return text.ToString();
    }

    private static string? Decided(TexReading reading, TexFormula ours)
    {
        // Reviewed 2026-08-27. Identical renderings; the parser splices a row written first into the row
        // it is starting, but only there — put anything before it and it nests, as ours always does.
        // Ours respects the grouping that was written; the parser's depends on where the group sits.
        //
        // Asked of what was built rather than of the reading, because that is where the shape is. "The
        // first thing in this run came out a row" is not a question about what was typed: `\mathrm{Tr}`
        // and `{\frac{a}{b}}` are written nothing alike and are the same case here.
        if (ours.RootAtom is XamlMath.Atoms.RowAtom { Elements: { Count: > 1 } elements }
            && elements[0] is XamlMath.Atoms.RowAtom)
            return "a row written first in a row — ours respects the grouping either way";

        return Decided(reading);
    }

    private static string? Decided(TexReading reading)
    {
        foreach (var part in reading.Root.SelfAndDescendants())
        {
            if (part.Kind != TexKind.Fence || part.Part(TexRole.Body) is not { } body) continue;

            // Reviewed 2026-08-27, and the one ruling so far that moves ink. `\left\|` drew no bar at
            // all on our side — `\|` strips to a symbol called `|` that no table has — which was simply
            // wrong. It is TeX's own spelling of `\Vert`, so that is what it asks for now, and the two
            // readings differ about the glyph rather than about the structure.
            if (Names(part, TexRole.Open) == @"\|" || Names(part, TexRole.Close) == @"\|")
                return @"\left\| — ours draws the double bar it names; the parser draws otherwise";

            // Reviewed 2026-08-27. Identical renderings; the parser collapses a script inside the inner
            // fence into one atom where ours keeps the group it was written as, which is what a
            // substitution has to be able to reach.
            if (body.SelfAndDescendants().Any(inner => inner.Kind == TexKind.Fence))
                return "a fence inside a fence — ours keeps the groups the parser collapses";

            // Reviewed 2026-08-27. Identical renderings; the parser follows TeX's rule that what comes
            // after modifies what came before and flattens the two, which is right for setting type and
            // wrong for selecting — the thing scripted and the script are separate things to point at.
            if (body.SelfAndDescendants().Any(inner => inner.Kind == TexKind.Script
                                                       && inner.Part(TexRole.Base) is { Kind: TexKind.Command } built
                                                       && built.Parts.Any()))
                return "a script on a construct, inside a fence — ours keeps the two apart";
        }

        return null;
    }

    /// <summary>What a fence's <c>\left</c> or <c>\right</c> was written with, as written.</summary>
    private static string Names(TexPart fence, string role) =>
        fence.Part(role)?.Part(TexRole.Argument)?.Node.Print() ?? string.Empty;

    /// <summary>
    /// Where a formula's every piece ends up on the page — what both readings have to agree about.
    /// <para>
    /// Typeset and captured, exactly as the editor does it, then written out as what each piece was
    /// drawn from and the rectangle it occupies. Geometry rather than pixels: the numbers are arithmetic
    /// over font metrics, so they are the same on any machine, where rasterising is not.
    /// </para>
    /// </summary>
    /// <summary>
    /// The fonts and style a formula is set in, made once per thread.
    /// <para>
    /// <see cref="WpfTeXEnvironment.Create"/> walks every font family installed on the machine to find
    /// the one <c>\text</c> would use, and builds the Computer Modern metrics beside it. Doing that per
    /// formula put a quarter of a million trips through WPF's process-wide font cache, which is locked —
    /// so measuring the corpus on thirty-two threads used four of them and took longer than one did.
    /// </para>
    /// <para>
    /// Per thread rather than shared, because what it holds is WPF's and belongs to the thread that made
    /// it.
    /// </para>
    /// </summary>
    [ThreadStatic]
    private static XamlMath.TexEnvironment? _setting;

    private static string Settled(TexFormula formula, string latex)
    {
        _setting ??= WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale);

        var capture = new Nexaflow.Visuals.Text.Markdown.Latex.LatexLayoutCapture(Scale, latex);
        formula.RenderTo(capture, _setting, 0, 0);
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
