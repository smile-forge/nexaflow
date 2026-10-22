using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using System.Windows.Threading;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// Picking out code in a fenced block, as a reader drags through it: the characters between where the drag started and where
/// it is now, across as many tokens and lines as it passes — and a copy is those characters.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("md-code-selection")]
public class CodeFenceSelectionTests
{
    private const string Document = "Before.\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n\nAfter.\n";

    /// <summary>From the <c>x</c> on the first line to just past the <c>2</c> on the second.</summary>
    private const string Dragged = "x = 1;\nvar y = 2";

    [TestMethod]
    public void DraggingThroughCodePicksOutTheCharactersBetween() => UiThread.Run(() =>
    {
        var surface = Shown(Document);

        Sweep(surface, Before(surface, "x = 1"), After(surface, "2;"));

        Assert.AreEqual(Dragged, surface.Shown.Current.SelectedText,
            "the characters dragged over, from one token into the next and one line into the next — not the whole fence");
    });

    [TestMethod]
    public void AndACopyOfThemIsThoseCharacters() => UiThread.Run(() =>
    {
        var surface = Shown(Document);
        var asked = Copies(surface);

        Sweep(surface, Before(surface, "x = 1"), After(surface, "2;"));
        surface.Pressed(Key.C, ModifierKeys.Control);

        Assert.AreEqual(Dragged, asked.Single().Text.ReplaceLineEndings("\n"), "the code picked out, and no fence round it");
    });

    [TestMethod]
    public void AndSoInADocumentThatIsOnlyRead() => UiThread.Run(() =>
    {
        var surface = Shown(Document, readOnly: true);
        var asked = Copies(surface);

        Sweep(surface, Before(surface, "x = 1"), After(surface, "2;"));
        surface.Pressed(Key.C, ModifierKeys.Control);

        Assert.AreEqual(Dragged, surface.Shown.Current.SelectedText, "a reader who may not write may still pick code out");
        Assert.AreEqual(Dragged, asked.Single().Text.ReplaceLineEndings("\n"), "and copy it");
    });

    /// <summary>A press, a move half way and a move the rest of the way, and letting go — as a hand drags.</summary>
    private static void Sweep(MarkdownSurface surface, Point from, Point to)
    {
        var element = surface.Shown;
        element.BeginPointerSelect(from);
        element.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        element.ExtendPointerSelect(to);
        element.EndPointerSelect();
    }

    /// <summary>Just inside the first character of <paramref name="text"/>, where a press means the place before it.</summary>
    private static Point Before(MarkdownSurface surface, string text)
    {
        var box = Character(surface, surface.Markdown.IndexOf(text, StringComparison.Ordinal));
        return new Point(box.Left + 0.5, box.Top + (box.Height / 2));
    }

    /// <summary>Just inside the right edge of the first character of <paramref name="text"/>, where a press means the place after it.</summary>
    private static Point After(MarkdownSurface surface, string text)
    {
        var box = Character(surface, surface.Markdown.IndexOf(text, StringComparison.Ordinal));
        return new Point(box.Right - 0.5, box.Top + (box.Height / 2));
    }

    /// <summary>Where the character at <paramref name="offset"/> is drawn, in the element's own units.</summary>
    private static Rect Character(MarkdownSurface surface, int offset)
    {
        Assert.IsTrue(offset >= 0, "precondition: the text is in the document");

        var box = surface.Shown.Laid.Root.RangeRects(offset, 1).First();
        var zoom = surface.Shown.Zoom;
        return new Rect(box.X * zoom, box.Y * zoom, box.Width * zoom, box.Height * zoom);
    }

    private static List<MarkdownClipboard.ContentCopy> Copies(MarkdownSurface surface)
    {
        var asked = new List<MarkdownClipboard.ContentCopy>();
        surface.Copying += (_, e) => { asked.Add(e.Copy); e.Handled = true; };
        return asked;
    }

    /// <summary>
    /// The document laid out, and laid again once its code's grammar has read it — what a reader has in front of them by the time
    /// they drag. A drag begun on the first laying would be begun on pieces the second one replaces.
    /// </summary>
    private static MarkdownSurface Shown(string markdown, bool readOnly = false)
    {
        var surface = new MarkdownSurface { Markdown = markdown, IsReadOnly = readOnly };

        surface.Measure(new Size(640, 2000));
        surface.Arrange(new Rect(0, 0, 640, 2000));
        surface.UpdateLayout();

        if (surface.Rereading)
        {
            var frame = new DispatcherFrame();
            void Landed(object sender, RoutedEventArgs args) { if (!surface.Rereading) frame.Continue = false; }
            var patience = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Normal, (_, _) => frame.Continue = false, surface.Dispatcher);

            surface.Reread += Landed;
            try { Dispatcher.PushFrame(frame); }
            finally { surface.Reread -= Landed; patience.Stop(); }
        }

        MarkdownEditorHarness.Pump();
        surface.UpdateLayout();

        Assert.IsFalse(surface.Rereading, "precondition: the code's grammar has read it");
        return surface;
    }
}
