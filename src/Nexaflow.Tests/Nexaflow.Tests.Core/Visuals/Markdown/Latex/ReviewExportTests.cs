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
        ("1-fence-in-fence",
         "The parser boxes a scripted fence before measuring the outer one, and picks a smaller bracket "
         + "than we do; ours grows to fit the script. Which bracket is right?",
         l => !l.Contains(@"\|")
              && l.IndexOf(@"\left", StringComparison.Ordinal) is var first and >= 0
              && first < l.IndexOf(@"\right", StringComparison.Ordinal)
              && l.IndexOf(@"\left", first + 1, StringComparison.Ordinal) is var second and >= 0
              && second < l.IndexOf(@"\right", StringComparison.Ordinal),
         @"\left[ \left( a \right)^{2} \right]"),

        ("2-script-on-a-construct-in-a-fence",
         "A fraction or an overline wearing a script, between delimiters. Outside a fence the two agree "
         + "exactly. Every number is identical here too - what differs is which box holds which.",
         l => l.Contains(@"\left(") && l.Contains(@"\frac") && l.Contains("} _ {"),
         @"\left( \frac { f } { g } _ { i } \right)"),

        ("3-double-bar-fence",
         @"\left\| - the double bar. We draw no bar at all rather than guess: stripping the backslash "
         + "draws a single bar and naming it Vert does not agree either. What should it be?",
         l => l.Contains(@"\left\|") || l.Contains(@"\right\|"),
         @"\left\| x \right\|"),

        ("4-row-written-first-in-a-row",
         "The parser splices a styled row into the row it is starting, but only when it is written "
         + "first; put anything before it and it nests, exactly as we do. Identical geometry either way.",
         l => l.TrimStart().StartsWith(@"\mathrm {", StringComparison.Ordinal) && l.Length > 24,
         @"\mathrm { v o l } ( 1 0 )"),

        ("5-script-with-nothing-to-carry-it",
         "A script written where there is nothing to set it on - after a tie, or first in a group. TeX "
         + "sets it on an empty box, so the drawing has a box that nothing in the reading stands for. "
         + "We decline rather than invent one. Should we invent it?",
         l => l.Contains("~ ^ {") || l.Contains("~ _ {"),
         @"F _ { \rho } ~ ^ { \nu }"),
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

        var theirs = WpfTeXFormulaParser.Instance.Parse(latex);
        var settledTheirs = Write(folder, $"{prefix}theirs", theirs, latex);

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

                return "We decline this even with the parked declines lifted, so there is no "
                     + "rendering of ours: it is a construct with no answer chosen yet.\n";
            }

            var settledOurs = Write(folder, $"{prefix}ours", ours, latex);

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
