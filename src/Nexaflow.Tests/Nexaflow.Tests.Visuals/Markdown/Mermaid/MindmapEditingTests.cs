using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a mindmap through the editor that hosts it: a node's title — on any of its lines where it wraps — is the
/// characters written, so a press puts the caret among them and a keystroke changes the mindmap.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("mindmap-writing")]
public class MindmapEditingTests : MermaidEditing
{
    private const string Plan = "mindmap\n  root((Nexaflow))\n    Markdown\n      id4[Extensions that cover every case anybody could think of writing]";

    /// <inheritdoc/>
    protected override string Source => Plan;

    [TestMethod]
    public void TypingInTheRootsTitleABareOnesAndEachLineOfAWrappedOneChangesThem() => UiThread.Run(() =>
        InADocument((editor, chart) =>
        {
            Assert.IsFalse(chart.IsReadOnly, "a mindmap's words are written in");

            PressPast(chart, "Nexaflow");
            Write(editor, "!");
            PressPast(chart, "Markdown");
            Write(editor, "s");

            // The long title wraps: a press at the end of its last line writes there.
            var lines = chart.Laid.Root.SelfAndDescendants().Where(piece => piece is { Kind: "Title", Words.Maps: true } && piece.Part!.Start > chart.Source.IndexOf("id4[", StringComparison.Ordinal)).ToList();
            Assert.IsTrue(lines.Count > 1, "the long title wraps");
            chart.BeginPointerSelect(new Point(lines[^1].Bounds.Right - 1, lines[^1].Bounds.Y + (lines[^1].Bounds.Height / 2)));
            chart.EndPointerSelect();
            Write(editor, "?");

            StringAssert.Contains(chart.Source, "root((Nexaflow!))", chart.Source);
            StringAssert.Contains(chart.Source, "    Markdowns\n", chart.Source);
            StringAssert.Contains(chart.Source, "of writing?]", chart.Source);
            Assert.AreEqual(0, chart.Diagnostics.Count, string.Join(" | ", chart.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }));
}



