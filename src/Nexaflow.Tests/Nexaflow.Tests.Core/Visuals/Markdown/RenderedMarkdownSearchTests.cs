using System.Collections.Generic;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// The rendered-text search that the markdown editor and the email body share. It matches the VISIBLE words
/// (not the markdown source) and highlights them in place — so these assert whole-word/wildcard semantics
/// hold against rendered text, and that clearing leaves the document as it was.
/// <para>Interactive desktop only — the editor builds its document during a render pass. Run with
/// <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]   // each case spins an off-screen Window; concurrent WPF layout makes the counts flaky
[NoCoverage("exercises the shared rendered-search helper across surfaces, not one product node")]
public class RenderedMarkdownSearchTests
{
    private static TextSearchMatcher Matcher(string query, bool regex = false)
    {
        Assert.IsTrue(TextSearchMatcher.TryCreate(new SearchRequest(query, IsRegex: regex), out var m, out var err), err);
        return m!;
    }

    // Words containing "fig": "figures" (heading), then "config", "fig", "prefigure" (body).
    //   fig   (whole word)  → 1 : the standalone "fig"
    //   fig*  (prefix)      → 2 : "figures", "fig"
    //   *fig* (substring)   → 4 : "figures", "config", "fig", "prefigure"
    private const string Doc = """
        # Heading about figures

        The config value and a fig, but not prefigure.
        """;

    // The editor must be built and driven on an STA thread (UiThread.Run), like the other editor UI tests.
    private static void InEditor(System.Action<InlineMarkdownEditor> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Doc, (editor, _) => test(editor)));

    [TestMethod]
    public void WildcardPrefix_MatchesWholeWordsInRenderedText() => InEditor(editor =>
    {
        // "fig*" is a word starting "fig": the heading's "figures" and the body's "fig" — but NOT the
        // "fig" buried inside "config" or "prefigure".
        var matches = editor.FindInRendered(Matcher("fig*"));

        Assert.AreEqual(2, matches.Count, "figures + fig, not config/prefigure");
        Assert.IsTrue(matches.All(m => m.Preview.Length > 0));
    });

    [TestMethod]
    public void LiteralWord_DoesNotMatchInsideALongerWord() => InEditor(editor =>
    {
        // "fig" as a bare literal is the whole word only — "figure"/"config"/"prefigure" are not it.
        var matches = editor.FindInRendered(Matcher("fig"));
        Assert.AreEqual(1, matches.Count, "only the standalone 'fig'");
    });

    [TestMethod]
    public void Substring_MatchesInsideWords_WhenAskedFor() => InEditor(editor =>
    {
        // "*fig*" is the substring escape hatch: figure, fig, config, prefigure — every word containing it.
        var matches = editor.FindInRendered(Matcher("*fig*"));
        Assert.AreEqual(4, matches.Count);
    });

    [TestMethod]
    public void FirstMatch_IsScrolledIntoView() =>
        UiThread.Run(() =>
        {
            // A tall document whose only match sits well below the fold — navigating to it must scroll.
            var tall = "top of the document\n\n"
                     + string.Concat(System.Linq.Enumerable.Repeat("filler paragraph line\n\n", 200))
                     + "the unmistakable zebra at the end\n";
            MarkdownEditorHarness.Run(tall, (editor, rtb) =>
            {
                Assert.AreEqual(0d, rtb.VerticalOffset, "starts at the top");

                var matches = editor.FindInRendered(Matcher("zebra"));
                rtb.UpdateLayout();

                Assert.AreEqual(1, matches.Count);
                Assert.IsTrue(rtb.VerticalOffset > 0,
                    $"offset={rtb.VerticalOffset} extent={rtb.ExtentHeight} viewport={rtb.ViewportHeight}");
            });
        });

    [TestMethod]
    public void ClearingSearch_RestoresTheDocument() =>
        UiThread.Run(() => MarkdownEditorHarness.Run(Doc, (editor, rtb) =>
        {
            var before = new System.Windows.Documents.TextRange(
                rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;

            editor.FindInRendered(Matcher("fig*"));
            editor.ClearSearch();

            var after = new System.Windows.Documents.TextRange(
                rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;
            Assert.AreEqual(before, after, "clearing the search leaves the rendered text unchanged");
        }));
}
