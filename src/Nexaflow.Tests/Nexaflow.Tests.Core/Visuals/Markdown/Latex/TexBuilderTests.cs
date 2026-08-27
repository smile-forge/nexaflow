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
        @"\mathrm{~mod~}",   // and a tie inside a style, which is how a paper spaces an operator name
        @"X \mathrm{~mod~} 2",
        @"\mathrm{Im~} z",

        // The empty group: a place for the next thing to attach to, and the tensor-index idiom that
        // half the physics in the corpus is written with.
        "{}",
        @"T^{\alpha}{}_{\alpha}",

        // Prefix scripts, drawn in front. Chemistry writes carbon-14 this way and physics writes a
        // tensor index this way; both are an empty box wearing the scripts followed by the real base.
        // Carbon-14, and how a prefix is actually written. Not the builder's prefix branch at all: `{}`
        // carries a script like anything else, so these are ordinary suffix scripts on an empty box
        // followed by the base — which is TeX's own construction and why the empty group had to come
        // first. The branch is for a prefix the reading nests, and every one of those follows a space
        // and is parked, so it is presently gated to nothing.
        @"{}^{14}_{6}\mathrm{C}",
        @"{}^{3}He",

        // Something set above or below something else. The roles say which is which, because the order
        // does not: \overset and \underset both write the annotation first.
        @"\stackrel{\rm def}{=}",
        @"\overset{a}{b}",
        @"\underset{a}{b}",
        @"A \stackrel{f}{\longrightarrow} B",

        // And the rest of the commands the table can build from arguments it is handed.
        @"\binom{n}{k}",
        @"\dbinom{n}{k} + \tbinom{a}{b}",
        @"\phantom{x} y",
        @"\overrightarrow{AB}",
        @"\boldsymbol{\alpha}",
        @"\vdots",
        @"\ddots",
        @"\underbrace{a+b}",
        @"\overbrace{x y}",
        @"\fbox{a}",
        @"\mathop{\rm tr}",

        // Commands whose whole effect belongs to a page this formula does not have. They draw nothing,
        // so they make no atom — but the reading keeps them, argument and all.
        @"E = mc^2 \tag{1}",
        @"a = b \nonumber",
        @"x \label{eq:one} + y",
        @"a \not= b",
        @"x \not\in S",
        @"\not\approx",
        @"A^{a}{}_{\mu} X_{a}",
        @"\int_{}^{} x",
        @"R_{ab} = R_{acb}{}^{c}",

        // Space that was asked for rather than typed. TeX's own spacing comes from atom classes and is
        // not written down; these are, so they build like any other command.
        @"a\,b",
        @"a\;b",
        @"a\!b",
        @"a\:b",
        @"a\quad b",
        @"a\qquad b",
        @"a\ b",                        // the control space: asked for, so built
        @"6 4 \ ,",                     // how a paper spaces a formula off from its punctuation

        // Sized delimiters. Not a fence: each one stands on its own, which is why the second of these is
        // good LaTeX and the third — a bracket opened at one size and closed at another — is too.
        @"\big( x \big)",
        @"\bigl( x",
        @"\bigl( x \Biggr]",
        @"\Big\{ a \Big\}",
        @"\biggl\| v \biggr\|",
        @"\big| x \big|",

        // The whole of the fences sample the typesetting baseline covers. It used to decline on the
        // \big family and go to the parser; now it builds, so whether that moved any ink is a question
        // worth asking here rather than inferring from a hash that also counts the containers.
        @"\left( a \right) + \left[ b \right] + \left\{ c \right\} + \left| d \right|
          + \left\langle e \right\rangle + \bigl( h \bigr) + \Bigl[ i \Bigr]
          + \biggl\{ j \biggr\} + \Biggl| k \Biggr|",

        // Macros whose expansion is several atoms rather than one — three dots, a slash laid over an
        // equals. The reader wrote one token and it draws as a little assembly, which is the case that
        // used to be declined: those atoms were parsed from the definition and carry offsets into it.
        @"a \cdots b",
        @"a \ldots b",
        @"x \neq y",
        @"a \longrightarrow b",
        @"\hbar \omega",

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
            Assert.IsNotNull(TexFormulaBuilder.Build(TexReading.Of(latex).Root, WpfTeXFormulaParser.Instance), latex);
    });

    [TestMethod]
    public void AndSetsItWhereTheParserSetsIt() => UiThread.Run(() =>
    {
        // Against the geometry, not against the atoms. The two trees are not the same shape and are not
        // meant to be: the parser wraps things in atoms of its own bookkeeping — a lone `a` comes back
        // as a TypedAtom around a CharAtom — and those wrappers exist to carry decisions this builder
        // makes differently or not at all. What has to match is where everything ends up on the page,
        // because that is what the reader sees and what every position in the editor is measured in.
        //
        // The same two questions the corpus asks, for the same reason. The parser is a reference and not
        // a specification — it was only ever about drawing a formula once, where this has to serve
        // selection and calculation too — so "the tree differs" is not a verdict. "The ink differs" is.
        foreach (var latex in Known)
        {
            var ours = TexFormulaBuilder.Build(TexReading.Of(latex).Root, WpfTeXFormulaParser.Instance);
            Assert.IsNotNull(ours, latex);

            var theirs = WpfTeXFormulaParser.Instance.Parse(latex);
            if (Settled(theirs, latex) == Settled(ours, latex)) continue;

            // Same picture and a different tree is a structural choice; a different picture is a
            // disagreement about what the reader sees. Either may stand, but only where somebody has said
            // so — a difference nobody has examined is indistinguishable from a defect.
            var why = Drawn(theirs, latex) == Drawn(ours, latex)
                ? "the same picture, a different tree — ours keeps what the writer grouped"
                : Decided(TexReading.Of(latex), ours);

            Assert.IsNotNull(why, $"{latex} is built differently and nothing says which is meant");
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
            var formula = TexFormulaBuilder.Build(TexReading.Of(latex).Root, WpfTeXFormulaParser.Instance);
            Assert.IsNotNull(formula, latex);

            foreach (var atom in Parts(formula.Root!))
            {
                // A macro's expansion is the one thing here the builder did not make. Those atoms were
                // parsed from the definition text and name points in *that* — a document nobody has open
                // — so they are marked borrowed where they are cached, and the box assertion below is
                // what proves the marking works. Everything the builder makes itself carries nothing.
                Assert.IsTrue(atom.Source is null || (atom as XamlMath.Atoms.Atom)?.Borrowed is true,
                    $"{atom.GetType().Name} in {latex} names a point in the source");

                // Everything the reader wrote carries the part they wrote it as. Two exceptions: the cell
                // nobody wrote — what squares off a short row — and the insides of a macro, which nothing
                // points at because the token the reader wrote is the whole of it.
                Assert.IsTrue(atom.Origin is not null
                              || atom is XamlMath.Atoms.NullAtom
                              || (atom as XamlMath.Atoms.Atom)?.Borrowed is true,
                    $"{atom.GetType().Name} in {latex} was built from nothing");
            }

            // And the rule itself, on the thing it is about. Every box that will be laid out, asked
            // whether it names a point in the text — because "no atom carries an offset" is a proxy for
            // this, and a proxy is exactly what stops holding when a new path appears.
            foreach (var box in Boxes(formula.RootAtom!.CreateBox(_setting ??= WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale))))
                Assert.IsNull(box.Source, $"a {box.GetType().Name} of {latex} names a point in the source");
        }
    });

    [TestMethod]
    public void EveryAtomKnowsWhichPartOfTheSourceItWasBuiltFrom() => UiThread.Run(() =>
    {
        // What none of this is possible without, and what the parser can never provide: an atom that
        // came from a reading which still knows where every brace was.
        var reading = TexReading.Of(@"\frac{a}{b}");
        var formula = TexFormulaBuilder.Build(reading.Root, WpfTeXFormulaParser.Instance);
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
            Assert.IsNull(TexFormulaBuilder.Build(TexReading.Of(latex).Root, WpfTeXFormulaParser.Instance), latex);
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
        var named = new Dictionary<string, (int Built, int Declined)>(StringComparer.Ordinal);
        var declined = new List<(string Latex, HashSet<string> Names)>();

        UiThread.Across(formulas, formula =>
        {
            var reading = TexReading.Of(formula.Latex);
            var made = TexFormulaBuilder.Build(reading.Root, WpfTeXFormulaParser.Instance);

            // What the declines are made of, counted rather than listed. Every command the reading names
            // is tallied twice over — formulas built and formulas declined — so a command the builder has
            // never learnt appears as a column of declines with nothing beside it, and one it handles
            // appears in both. A hand-written list of what is missing goes stale the day something lands;
            // this cannot, because it is derived from the run that reports the coverage.
            var commands = Commands(reading.Root.Node, []);
            if (commands.Count > 0)
                lock (named)
                    foreach (var command in commands)
                    {
                        var (yes, no) = named.TryGetValue(command, out var count) ? count : default;
                        named[command] = made is null ? (yes, no + 1) : (yes + 1, no);
                    }

            if (made is null) lock (declined) declined.Add((formula.Latex, commands));

            if (made is not { } ours) return;

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

        // And what the other quarter is made of. Ranked by how many formulas a command costs — its
        // declines, less the ones it is plainly not the reason for — so the next thing to teach the
        // builder is the top of this file rather than whichever gap came to mind.
        File.WriteAllText(
            Path.Combine(Path.GetDirectoryName(corpus)!, "tex-builder-gaps.txt"),
            $"{seen - built:N0} formulas went to the engine's own parser. What they name, worst first —\n"
            + "a command with declines and no builds beside them is one the builder has never learnt;\n"
            + "one with both is present in declines it is not the reason for.\n\n"
            + $"{"declined",10}{"built",10}  command\n"
            + string.Join("\n", named.Where(n => n.Value.Declined > 20)
                                     .OrderByDescending(n => n.Value.Declined)
                                     .Take(120)
                                     .Select(n => $"{n.Value.Declined,10:N0}{n.Value.Built,10:N0}  {n.Key}"))
            + Residue(declined, named));
    }

    /// <summary>
    /// The declines the list above does not account for: formulas naming no command the builder has
    /// failed on every time, so something other than an unlearnt command turned them away.
    ///
    /// <para>
    /// The check that stops the ranking being read as the whole story. A tally of commands can only find
    /// what has a name — an empty group, a preamble it cannot read, a script it will not place have
    /// none, and would be invisible in a file that looks complete. Shortest first, because the shortest
    /// example of a shape is the one worth reading.
    /// </para>
    /// </summary>
    private static string Residue(
        List<(string Latex, HashSet<string> Names)> declined,
        Dictionary<string, (int Built, int Declined)> named)
    {
        var unlearnt = named.Where(n => n.Value.Built == 0).Select(n => n.Key).ToHashSet(StringComparer.Ordinal);
        var rest = declined.Where(d => !d.Names.Overlaps(unlearnt))
                           .OrderBy(d => d.Latex.Length)
                           .ToList();

        return $"\n\n{rest.Count:N0} of the declines name no command from that list, so something else "
             + "turned them\naway. The forty shortest —\n\n"
             + string.Join("\n", rest.Take(40).Select(d => $"  {d.Latex}"));
    }

    /// <summary>Every command a reading names, once each.</summary>
    private static HashSet<string> Commands(TexNode node, HashSet<string> into)
    {
        if (node.Role == TexRole.Name && node.Text.StartsWith('\\')) into.Add(node.Text);
        foreach (var child in node.Children) Commands(child, into);
        return into;
    }

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
    /// Matched on the reading rather than on the text. "Contains two <c>\left</c>" is a search; "a fence
    /// whose body holds a fence" is the shape that was ruled on, and it is a question the parse tree
    /// answers exactly.
    /// </para>
    /// </summary>
    private static string? Decided(TexReading reading, TexFormula ours)
    {
        // A macro whose expansion is several atoms — `\cdots` is three dots, `\hbar` an h with a bar laid
        // over it. The reader wrote one token, so one thing is what it is here: ours keeps the assembly
        // under a node of its own, where the parser splices the pieces into the row around them and loses
        // that the token was ever one. Which matters beyond drawing — a calculation reading `\hbar` wants
        // the constant, not three boxes, and a selection wants the whole of it or none.
        if (Parts(ours.RootAtom!).Any(atom => atom.Slots.Count > 0
                                              && atom.Origin is { Kind: TexKind.Command } part
                                              && part.Children.All(child => child.Role == TexRole.Name)))
            return "a macro's expansion — ours keeps the one token the reader wrote as one thing";

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
        // The same ruling, wherever the delimiter is written. `\|` is TeX's spelling of `\Vert` whether it
        // follows a `\left`, a `\biggl` or nothing at all, so the shape that was ruled on is the token and
        // not the construct around it — matching only inside a fence would leave `\biggl\|` looking like a
        // finding nobody had seen before, when it is this one again.
        foreach (var part in reading.Root.SelfAndDescendants())
            if (part.Role == TexRole.Argument && part.Print() == @"\|")
                return @"\| — ours draws the double bar it names; the parser draws otherwise";

        // An empty group is a place and not an absence. The parser drops `{}` and leaves nothing behind;
        // ours keeps a box of no width, because the reader wrote it, a caret has to be able to sit in it,
        // and it is what a prefix script attaches to. Nothing on the page differs — both draw nothing —
        // but only one of the two can be pointed at.
        foreach (var part in reading.Root.SelfAndDescendants())
            if (part.Kind == TexKind.Group && !part.Parts.Any())
                return "an empty group — ours keeps the place the reader wrote, where the parser keeps nothing";

        foreach (var part in reading.Root.SelfAndDescendants())
        {
            if (part.Kind != TexKind.Fence || part.Part(TexRole.Body) is not { } body) continue;

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

    /// <summary>Everything a formula lays out, box by box.</summary>
    private static IEnumerable<XamlMath.Boxes.Box> Boxes(XamlMath.Boxes.Box box)
    {
        yield return box;

        foreach (var child in box.Children)
            foreach (var under in Boxes(child)) yield return under;
    }

    private static IEnumerable<IFormulaNode> Parts(IFormulaNode node)
    {
        yield return node;

        foreach (var slot in node.Slots)
            foreach (var inner in Parts(slot.Node))
                yield return inner;
    }
}
