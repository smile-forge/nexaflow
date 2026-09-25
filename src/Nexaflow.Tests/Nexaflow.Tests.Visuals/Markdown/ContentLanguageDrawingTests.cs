using System.Collections.Generic;
using System.Linq;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Every language a fence can be written in draws something, and goes on drawing something when what is
/// written in it is wrong.
///
/// <para>
/// <strong>Nothing drawn is never an acceptable answer.</strong> A block that vanished is a block a reader
/// cannot see is missing, cannot find to fix, and cannot put a caret in to repair — and a document being
/// written is wrong most of the time, because half a diagram is what every diagram looks like on the way to
/// being one. So a language either lays the content out or says it cannot, and what is drawn then is the
/// characters somebody typed.
/// </para>
/// <para>
/// A sweep rather than a test per language, because the rule is about the table and not about any one
/// entry: a language added without a way to draw its own trouble should fail here on the day it is added.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-text")]
public class ContentLanguageDrawingTests
{
    /// <summary>One fence per language in the table, written correctly.</summary>
    private static readonly (string Named, string Source)[] Written =
    [
        ("mermaid", "pie\n  \"a\" : 1\n"),
        ("nomnoml", "[Thing|field]\n"),
        ("qr", "text: hello\n"),
        ("aztec", "text: hello\n"),
        ("datamatrix", "text: hello\n"),
        ("pdf417", "text: hello\n"),
        ("smiles", "CCO\n"),
        ("latex", "\\frac{x}{2}\n"),
        ("abc", "X:1\nK:C\nCDEF|\n"),
        ("lilypond", "\\relative c' { c4 d e f }\n"),
        ("scatter", "weight  mpg\n3504  18\n2372  24\n1613  35\n"),
        ("bubble", "weight  mpg  cyl\n3504  18  8\n2372  24  4\n"),
        ("heatmap", "weight  mpg\n3504  18\n2372  24\n"),
        ("density2d", "weight  mpg\n3504  18\n2372  24\n"),
        ("wordcloud", "WPF: 40\nXAML: 25\nMVVM: 12\n"),
        ("barcode", "format: CODE39\nvalue: MARKDOWN-39\n"),
        ("csharp", "var x = 1;\n"),
    ];

    /// <summary>The same fences with something wrong in them — the state a reader spends most of their time in.</summary>
    private static readonly string[] Broken =
    [
        "",
        "   \n",
        "?\n",
        "\u0000\n",
        "format:\nvalue:\n",
        "{{{\n",
        "a\tb\n\t\t\n",
    ];

    [TestMethod]
    public void EveryLanguageDrawsWhatIsWrittenInIt() => UiThread.Run(() =>
    {
        foreach (var (named, source) in Written)
        {
            var laid = Lay(named, source);

            Assert.IsTrue(laid.Size.Height > 0, $"a {named} fence drew nothing at all");
            Assert.IsTrue(Anything(laid), $"a {named} fence drew a box with nothing in it");
        }
    });

    [TestMethod]
    public void AndGoesOnDrawingSomethingWhenItIsWrong() => UiThread.Run(() =>
    {
        var silent = new List<string>();

        foreach (var (named, _) in Written)
            foreach (var broken in Broken)
            {
                var laid = Lay(named, broken);

                if (laid.Size.Height <= 0 || !Anything(laid))
                    silent.Add($"{named}: {Shown(broken)}");
            }

        if (silent.Count > 0)
            Assert.Fail($"drew nothing at all:{System.Environment.NewLine}  " +
                        string.Join(System.Environment.NewLine + "  ", silent));
    });

    [TestMethod]
    public void EveryLanguageInTheTableIsSwept()
    {
        // The sweep is only worth what it covers, so a language added to the table and not to this list
        // fails here rather than quietly going untested.
        foreach (var (named, _) in Written)
            Assert.IsNotNull(ContentLanguages.For(named), $"nothing reads '{named}' any more");

        Assert.IsTrue(Written.Select(entry => ContentLanguages.For(entry.Named)!.GetType()).Distinct().Count() >= 13,
            "the sweep should reach every language in the table, not the same one under many names");
    }

    [TestMethod]
    public void ALanguageThatCannotReadABlockLeavesTheCharactersToBeDrawn() => UiThread.Run(() =>
    {
        // The specific shape of the rule: a barcode block that will not read has no symbol in it, so it lays its source
        // with why — and the document puts the block's source there, fences and all, where the caret can reach it.
        var laid = ContentLanguages.For("barcode")!.Lay(new ContentRequest("format:\nvalue:\n", StyleFormat.Dark));
        Assert.IsTrue(laid?.ShowsSource ?? false, "shown as written");
        Assert.AreNotEqual(0, laid?.Trouble.Count ?? 0, "and it says why");

        var document = Lay("barcode", "format:\nvalue:\n");
        var shown = document.Root.SelfAndDescendants().Single(piece => piece.Kind == LayoutText.SourceKind);
        var text = shown.Marks.ToArray().OfType<TextMark>().Single().Glyphs.Text;

        StringAssert.StartsWith(text, "```barcode", "the block that could not be read is on the page as what was typed, its fences and all");
        var sits = shown.Sits();
        Assert.IsTrue(document.Stops.Count(stop => stop > sits.Start && stop < sits.End) > 1, "and the caret goes straight into it");
        Assert.IsTrue(document.Root.SelfAndDescendants().Any(piece => piece.Kind == SourceShown.Reason), "with why under it");
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    private static Laid Lay(string named, string source) =>
        MarkdownBuilder.Lay($"```{named}\n{source}```\n", StyleFormat.Dark, 480);

    /// <summary>Whether anything at all reached the page: a run of words, or a mark of any kind.</summary>
    private static bool Anything(Laid laid)
    {
        foreach (var piece in laid.Root.SelfAndDescendants())
        {
            if (piece.Words is not null) return true;
            if (piece.Marks.Length > 0) return true;
        }

        return false;
    }

    private static string Shown(string source) =>
        source.Length == 0 ? "(empty)" : source.Replace("\n", "\\n").Replace("\u0000", "\\0");
}
