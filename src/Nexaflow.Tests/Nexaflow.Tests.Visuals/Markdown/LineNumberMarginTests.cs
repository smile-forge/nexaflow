using System.Linq;
using System.Windows.Media;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Each line's number beside the source: one for every line on the page, on its first row however many it wraps onto, in a column as
/// wide as the document's last number.
/// <para>Interactive desktop only — the editor builds its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-source-line-numbers")]
public class LineNumberMarginTests
{
    [TestMethod]
    public void EveryLineOnThePage_HasItsNumber_OnItsFirstRow() => UiThread.Run(() =>
    {
        var numbers = new LineNumberMargin();
        var paragraph = string.Join(' ', Enumerable.Range(0, 80).Select(word => $"word{word}"));

        ContentMarginTests.Shown($"first\n{paragraph}\nthird", surface =>
        {
            var lines = surface.LinesShown();
            var shift = surface.TranslatePoint(default, numbers).Y;
            var drawn = Glyphs(numbers);

            Assert.AreEqual(lines.Count, drawn.Length, $"a number for every line, and none for a wrapped line's later rows: drew {VisualTreeHelper.GetDrawing(numbers)?.Children.Count ?? -1} item(s), {numbers.ActualWidth:0.#} wide");

            for (var at = 0; at < lines.Count; at++)
            {
                var (top, row) = (lines[at].Top + shift, lines[0].Height);
                Assert.IsTrue(drawn[at].Bounds.Top >= top - 0.5 && drawn[at].Bounds.Bottom <= top + row + 0.5,
                              $"the number of line {lines[at].Number} is not on its first row");
            }
        }, surface => surface.LeadingMargins.Add(numbers));
    });

    [TestMethod]
    public void TheColumn_IsAsWideAsTheDocumentsLastNumber_TwoDigitsAtTheLeast() => UiThread.Run(() =>
    {
        double Wide(int lines)
        {
            var numbers = new LineNumberMargin();
            var width = 0.0;

            ContentMarginTests.Shown(string.Join('\n', Enumerable.Range(1, lines)), _ => width = numbers.ActualWidth,
                                     surface => surface.LeadingMargins.Add(numbers));
            return width;
        }

        var (one, ninetyNine, ten000) = (Wide(1), Wide(99), Wide(10_000));

        Assert.AreEqual(one, ninetyNine, 0.5, "the column widened before the document reached three digits");
        Assert.IsTrue(ten000 > ninetyNine, "five-digit numbers stand in a two-digit column");
    });

    /// <summary>What <paramref name="numbers"/> drew, top to bottom: one run of glyphs for each number.</summary>
    private static GlyphRunDrawing[] Glyphs(LineNumberMargin numbers) =>
        [.. (VisualTreeHelper.GetDrawing(numbers)?.Children ?? []).SelectMany(Runs).OrderBy(run => run.Bounds.Top)];

    private static System.Collections.Generic.IEnumerable<GlyphRunDrawing> Runs(Drawing drawing) => drawing switch
    {
        GlyphRunDrawing run => [run],
        DrawingGroup group => group.Children.SelectMany(Runs),
        _ => [],
    };
}
