using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a radar chart through the editor that hosts it: the label at the end of a spoke and the name in a legend row are
/// the characters written, so a press puts the caret among them and a keystroke changes the chart — an axis renamed is renamed
/// wherever a value names it, and Enter starts another curve with a hole for its name.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("radar-writing")]
public class RadarEditingTests
{
    private const string Skills = "radar-beta\n  axis ui[\"UI\"], api\n  curve alice[\"Alice\"]{ ui: 3, api: 4 }\n  curve bob{2, 5}";

    /// <summary>The chart inside a document, fenced, with the editor handing its keys to it.</summary>
    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test, string diagram = Skills) =>
        MarkdownEditorHarness.Run("Skills:\n\n```mermaid\n" + diagram + "\n```\n", editor =>
        {
            var radar = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(radar, "the diagram did not render as content");

            test(editor, radar!);
        });

    /// <summary>Presses just inside the end of an axis's label or a curve's legend row, where it is drawn.</summary>
    private static void PressPast(DocumentBlock radar, string words)
    {
        var piece = radar.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind is RadarPiece.Label or RadarPiece.Name
                            && piece.Sits().Start == radar.Source.IndexOf(words, radar.Start, System.StringComparison.Ordinal));

        radar.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        radar.EndPointerSelect();
    }

    private static void Press(MarkdownSurface editor, Key key)
    {
        MarkdownEditorHarness.RaiseKey(editor, key);
        MarkdownEditorHarness.Pump();
    }

    private static void Write(MarkdownSurface editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    private static string Trouble(DocumentBlock radar) => string.Join(" | ", radar.Diagnostics.Select(diagnostic => diagnostic.Message));

    [TestMethod]
    public void TypingInAnAxisLabelChangesTheLabel() => UiThread.Run(() =>
        InADocument((editor, radar) =>
        {
            Assert.IsFalse(radar.IsReadOnly, "a radar's labels are written in");

            PressPast(radar, "UI");
            Assert.IsTrue(radar.HasCaret, "a press on a label takes the caret");

            Write(editor, "X");

            StringAssert.Contains(radar.Source, "axis ui[\"UIX\"], api", radar.Source);
            StringAssert.Contains(editor.Markdown, "axis ui[\"UIX\"]", "and so does the document");
        }));

    [TestMethod]
    public void TypingInALegendRowChangesTheCurvesLabel() => UiThread.Run(() =>
        InADocument((editor, radar) =>
        {
            PressPast(radar, "Alice");
            Write(editor, "a");

            StringAssert.Contains(radar.Source, "curve alice[\"Alicea\"]", radar.Source);
            Assert.AreEqual(0, radar.Diagnostics.Count, Trouble(radar));
        }));

    [TestMethod]
    public void DeletingAWholeLabelLeavesAHoleToWriteANewOneIn() => UiThread.Run(() =>
        InADocument((editor, radar) =>
        {
            PressPast(radar, "UI");
            for (var letter = 0; letter < "UI".Length; letter++) Press(editor, Key.Back);

            StringAssert.Contains(radar.Source, "axis ui[\"\"], api", $"the label is gone: {radar.Source}");
            Assert.AreEqual(0, radar.Diagnostics.Count, Trouble(radar));
            Assert.AreEqual(1, radar.Laid.Holes.Count, "a hole stands where it goes");
            Assert.AreEqual(radar.Laid.Holes[0].Sits().Start, radar.Caret);

            Write(editor, "Front");
            StringAssert.Contains(radar.Source, "axis ui[\"Front\"]", radar.Source);
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }

    [TestMethod]
    public void ASpaceTypedIntoABareAxisNamePutsItInQuotes()
    {
        const string source = "radar-beta\n  axis a, b\n  curve x{ a: 1, b: 2 }";
        var words = MermaidStaged.Read(source).SelfAndDescendants().First(part => part.Kind == MermaidKinds.Words && part.Text == "a");
        var writing = RadarEdits.Escaping(words, words.End, " ")!.Value;

        Assert.AreEqual("radar-beta\n  axis \"a \", b\n  curve x{ a: 1, b: 2 }", MermaidStaged.Written(source, writing));
    }
}
