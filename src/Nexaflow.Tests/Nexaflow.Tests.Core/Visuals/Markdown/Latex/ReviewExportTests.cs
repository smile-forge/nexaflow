using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media.Imaging;
using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using WpfMath;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;
using XamlMath.Rendering;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// Writes out everything a person needs to arbitrate a parked disagreement: the formula, the reference
/// image the corpus shipped with it, both renderings, both layout trees and the parse tree.
///
/// <para>
/// Not a test. It asserts nothing about the disagreements — deciding which reading is right is the one
/// thing that cannot be automated, which is why they are parked rather than fixed. This is the material
/// for that decision, and it lives here because this is where the machinery to produce it already is.
/// </para>
/// <para>
/// Run it deliberately: it needs <c>NEXAFLOW_LATEX_CORPUS</c> and writes to
/// <c>NEXAFLOW_LATEX_REVIEW</c> (or a folder beside the corpus).
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("a diagnostic, not a test - it produces the material for a human decision")]
public class ReviewExportTests
{
    private const double Scale = 20;

    /// <summary>
    /// What is parked, and a way to find a real corpus formula showing each. The pattern matters more
    /// than the example: a reference image only exists for a formula the corpus actually contains.
    /// </summary>
    private static readonly (string Name, string Question, Func<string, bool> Shows, string Fallback)[] Parked =
    [
        ("1-space-beside-a-prefix-script",
         "A prefix script with a space beside it - a tie in front of it, or a space between it and what "
         + "it is on. A prefix is drawn as an empty box wearing the scripts, followed by the base, so "
         + "the question is which side of that box the gap the writer asked for belongs on. 17 formulas.",
         l => l.Contains("~ ^ {") || l.Contains("~ _ {"),
         @"F _ { \rho } ~ ^ { \nu } G"),

        ("2-script-on-an-assembled-command",
         "What a script means depends on what it lands on. \\underbrace{x}_{d} tucks the d under the "
         + "brace as its label and \\mathop{lim}_{n} sets the n as a limit beneath, rather than either "
         + "being an ordinary subscript beside. The parser reads that while it reads the argument, in "
         + "one pass; our reading has the script nested round the whole command, so the two have to be "
         + "brought together deliberately. Is the label part of the brace, or a script on it? 112 formulas.",
         l => l.Contains(@"\underbrace") && l.Contains("} _ {"),
         @"\underbrace { a + b } _ { n }"),

        ("3-script-written-first-in-a-run",
         "A script with nothing at all before it - ^{(4)}R, {^6 g}, {_B}T. The rule that hands a prefix "
         + "what follows it fires only after something that COULD NOT carry a script, and at the start "
         + "of a run there is nothing there at all, so today these get no base and are not built. Should "
         + "\"nothing before it\" count as \"cannot carry it\"? This one is about the parser rather than "
         + "the drawing, and it is now the largest single family left.",
         l => l.Contains("{ ^ ") || l.Contains("{ _ ") || l.TrimStart().StartsWith('^'),
         @"^ { ( 4 ) } R _ { \mu }"),

        ("4-tie-beside-an-asked-for-space-in-a-style",
         "\\mathrm{\\quad ~} - a tie standing next to a space that was asked for, inside a style. Two "
         + "formulas in the whole corpus. This is what the old \"a tie inside a style\" decline was "
         + "really about; written that broadly it also turned away every \\mathrm{~mod~}, of which "
         + "there are thousands and all of which agree.",
         l => l.Contains(@"\mathrm { \quad ~ }"),
         @"\Gamma _ { \mathrm { \quad ~ } \mu } ^ { \lambda }"),
    ];

    [TestMethod]
    public void WriteTheMaterialForTheParkedDisagreements()
    {
        var corpus = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_CORPUS");
        if (string.IsNullOrWhiteSpace(corpus) || !File.Exists(corpus))
            Assert.Inconclusive("set NEXAFLOW_LATEX_CORPUS to the corpus formula file");

        var dataset = Path.GetDirectoryName(corpus)!;
        var into = Environment.GetEnvironmentVariable("NEXAFLOW_LATEX_REVIEW")
                   ?? Path.Combine(dataset, "review");

        var formulas = File.ReadAllLines(corpus);
        var images = File.ReadAllLines(Path.Combine(dataset, "corresponding_png_images.txt"));

        Directory.CreateDirectory(into);
        var index = new StringBuilder();
        index.Append("# Parked disagreements, for review\n\n")
             .Append("Each folder holds one. `reference.png` is the rendering the published paper "
                   + "shipped — the third opinion, and the one that outranks both parsers.\n\n");

        UiThread.Run(() =>
        {
            foreach (var (name, question, shows, fallback) in Parked)
            {
                // The shortest one the corpus has that *actually disagrees* — not merely the shortest
                // one matching the pattern. A reference image is only worth having for a formula the two
                // readings draw differently; for one they agree about, the published picture settles
                // nothing and reading it is time spent on a case that was never in question.
                var at = -1;
                var any = -1;
                for (var i = 0; i < formulas.Length; i++)
                {
                    var line = formulas[i].Trim();
                    if (line.Length == 0 || !shows(line)) continue;

                    if (any < 0 || line.Length < formulas[any].Trim().Length) any = i;
                    if (at >= 0 && line.Length >= formulas[at].Trim().Length) continue;
                    if (Differs(line)) at = i;
                }

                // Where we decline outright there is nothing of ours to differ, so nothing would be
                // found — and that is the case where the published picture matters most, because it is
                // the only opinion there is on what should be drawn. Take the shortest match instead.
                if (at < 0) at = any;

                var latex = at >= 0 ? formulas[at].Trim() : fallback;
                var reference = at >= 0 && at < images.Length
                    ? Path.Combine(dataset, "generated_png_images", images[at].Trim())
                    : null;

                var folder = Path.Combine(into, name);
                Directory.CreateDirectory(folder);

                if (reference is not null && File.Exists(reference))
                    File.Copy(reference, Path.Combine(folder, "reference.png"), overwrite: true);

                var found = Both(folder, "", latex);

                // And the same thing with everything else taken away. No reference image for this one —
                // nobody published it — but it is the one to look at first.
                var least = Both(folder, "least-", fallback);

                File.WriteAllText(Path.Combine(folder, "question.md"),
                    $"# {name}\n\n{question}\n\n"
                    + $"## As the corpus writes it{(at >= 0 ? $" (line {at + 1})" : ", not found — hand-written")}\n\n"
                    + $"```\n{latex}\n```\n\n{found}\n"
                    + $"## Stripped to the construct\n\n```\n{fallback}\n```\n\n{least}\n");

                index.Append($"- **{name}** — {question}\n");
            }
        });

        File.WriteAllText(Path.Combine(into, "README.md"), index.ToString());
        Console.WriteLine($"wrote {into}");
    }

    /// <summary>
    /// Whether the two readings draw this differently, with the parked declines lifted so there is
    /// something of ours to compare at all.
    /// </summary>
    private static bool Differs(string latex)
    {
        TexFormulaBuilder.DeclineUnsettled = false;
        try
        {
            if (TexFormulaBuilder.Build(TexReading.Of(latex).Root, WpfTeXFormulaParser.Instance) is not { } ours)
                return false;

            return Settled(WpfTeXFormulaParser.Instance.Parse(latex), latex) != Settled(ours, latex);
        }
        catch
        {
            return false;
        }
        finally
        {
            TexFormulaBuilder.DeclineUnsettled = true;
        }
    }

    /// <summary>Where every piece of a formula lands, and nothing about what it was named from.</summary>
    private static string Settled(TexFormula formula, string latex)
    {
        var capture = new Nexaflow.Visuals.Text.Markdown.Latex.LatexLayoutCapture(Scale, latex);
        formula.RenderTo(capture, WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale), 0, 0);
        capture.FinishRendering();

        var text = new StringBuilder();
        foreach (var node in capture.Root!.SelfAndDescendants())
            text.Append(node.Kind).Append(' ')
                .Append(Number(node.Bounds.X)).Append(',').Append(Number(node.Bounds.Y)).Append(' ')
                .Append(Number(node.Bounds.Width)).Append('x').Append(Number(node.Bounds.Height))
                .Append('\n');

        return text.ToString();
    }

    /// <summary>
    /// One formula, drawn and described both ways: the parse tree, then each reading's picture and box
    /// tree. Returns a line for the question sheet saying whether the two agree.
    /// </summary>
    private static string Both(string folder, string prefix, string latex)
    {
        File.WriteAllText(Path.Combine(folder, $"{prefix}formula.txt"), latex);
        File.WriteAllText(Path.Combine(folder, $"{prefix}parse-tree.txt"),
            Shape(TexParser.Parse(latex), 0, new StringBuilder()).ToString());

        // The parser refuses some of these outright — a leading `^` is "Every script needs a base" and
        // nothing is drawn at all. Worth saying rather than crashing on: where there is no rival reading
        // there is no disagreement to arbitrate, and the question becomes whether ours should exist.
        string? settledTheirs = null;
        var refused = string.Empty;

        try
        {
            settledTheirs = Write(
                folder, $"{prefix}theirs", WpfTeXFormulaParser.Instance.Parse(latex), latex);
        }
        catch (XamlMath.Exceptions.TexParseException refusal)
        {
            refused = $"**The parser refuses this outright**: *{refusal.Message}* — so there is no "
                    + "rendering of theirs at all, and nothing to hold ours against. The question is not "
                    + "which of two readings is right but whether this should be readable.\n\n";

            File.WriteAllText(Path.Combine(folder, $"{prefix}theirs-REFUSED.txt"), refusal.Message);
        }

        // The whole point of the flag: what we would draw if this were not parked.
        TexFormulaBuilder.DeclineUnsettled = false;
        try
        {
            if (TexFormulaBuilder.Build(TexReading.Of(latex).Root, WpfTeXFormulaParser.Instance) is not { } ours)
            {
                File.WriteAllText(Path.Combine(folder, $"{prefix}ours-MISSING.txt"),
                    "The builder declines this even with the parked declines lifted, so there is no "
                    + "rendering of ours to compare. That is not a disagreement about the picture — it is "
                    + "a construct we have not decided how to draw.");

                return refused
                     + "We decline this even with the parked declines lifted, so there is no "
                     + "rendering of ours: it is a construct with no answer chosen yet.\n";
            }

            var settledOurs = Write(folder, $"{prefix}ours", ours, latex);

            if (settledTheirs is null)
                return refused + "Ours draws it — `ours.png` and `layout-ours.txt` — so there is one "
                     + "reading of this and it is ours. Is it the right one?\n";

            return settledOurs == settledTheirs
                ? "**The two agree exactly here** — every box in the same place. Whatever is parked "
                + "about this shape, this formula is not it.\n"
                : "**These differ.** Compare the two pictures against `reference.png`, and the two box "
                + "trees against each other — in several of these every number is identical and only "
                + "the nesting differs, which is a question about structure rather than about drawing.\n";
        }
        finally
        {
            TexFormulaBuilder.DeclineUnsettled = true;
        }
    }

    /// <summary>The formula as a picture and as the tree of boxes it was laid out into.</summary>
    private static string Write(string folder, string whose, TexFormula formula, string latex)
    {
        var environment = WpfTeXEnvironment.Create(style: TexStyle.Display, scale: Scale);

        var bitmap = formula.RenderToBitmap(environment, Scale);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(folder, $"{whose}.png"))) encoder.Save(file);

        var capture = new Nexaflow.Visuals.Text.Markdown.Latex.LatexLayoutCapture(Scale, latex);
        formula.RenderTo(capture, environment, 0, 0);
        capture.FinishRendering();

        var text = new StringBuilder();
        Layout(capture.Root!, 0, latex, text);
        File.WriteAllText(Path.Combine(folder, $"layout-{whose}.txt"), text.ToString());

        // Where every piece landed, and nothing about what it was named from — the two questions this
        // review keeps having to tell apart.
        var settled = new StringBuilder();
        foreach (var node in capture.Root!.SelfAndDescendants())
            settled.Append(node.Kind).Append(' ')
                .Append(Number(node.Bounds.X)).Append(',').Append(Number(node.Bounds.Y)).Append(' ')
                .Append(Number(node.Bounds.Width)).Append('x').Append(Number(node.Bounds.Height))
                .Append('\n');

        return settled.ToString();
    }

    private static void Layout(ILayoutNode node, int depth, string latex, StringBuilder text)
    {
        var named = node.SourceLength > 0 && node.SourceStart + node.SourceLength <= latex.Length
            ? $"  '{latex.Substring(node.SourceStart, node.SourceLength)}'"
            : "";

        text.Append(new string(' ', depth * 2))
            .Append(node.Kind)
            .Append("  ").Append(Number(node.Bounds.X)).Append(',').Append(Number(node.Bounds.Y))
            .Append("  ").Append(Number(node.Bounds.Width)).Append('x').Append(Number(node.Bounds.Height))
            .Append(node.IsInk ? "  ink" : "")
            .Append(named)
            .Append('\n');

        foreach (var child in node.Children) Layout(child, depth + 1, latex, text);
    }

    private static StringBuilder Shape(TexNode node, int depth, StringBuilder text)
    {
        text.Append(new string(' ', depth * 2))
            .Append(node.Kind)
            .Append(node.Role.Length > 0 ? $"[{node.Role}]" : "")
            .Append(node.Children.Count == 0 ? $"  '{node.Text}'" : "")
            .Append('\n');

        foreach (var child in node.Children) Shape(child, depth + 1, text);
        return text;
    }

    private static string Number(double value) =>
        value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
