using Nexaflow.Markdown.Binding;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>{{…}}</c> drawn: the value stands where the binding was written, the source is still there underneath, and
/// a press reveals it so the caret has characters to stand between.
///
/// <para>What a path comes to is decided WPF-free in <c>BoundTextTests</c>; this is that, drawn.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid")]
public class DiagramBindingTests
{
    private sealed class Owner
    {
        public string Name { get; set; } = "Platform";
    }

    private const string Src = "graph TD\n  a[\"Owned by {{Name}}\"] --> b[\"Next\"]\n";

    private static ContentElement Drawn(string source, IDataContext? data)
    {
        var element = (ContentElement)DiagramRenderer.Render("mermaid", source, new DiagramRenderOptions
        {
            Palette = StyleFormat.Dark,
            DataContext = data,
        });

        element.Measure(new Size(900, 900));
        element.Arrange(new Rect(0, 0, 900, 900));
        element.UpdateLayout();
        return element;
    }

    private static List<string> Words(ContentElement element) =>
        [.. element.Laid.Root.SelfAndDescendants()
                   .Select(piece => piece.Words?.Glyphs.Text)
                   .OfType<string>()];

    /// <summary>The middle of the first run of words saying <paramref name="says"/>.</summary>
    private static Point Middle(ContentElement element, string says)
    {
        var (_, where) = element.Laid.Tree.Root.Placed()
            .First(at => at.Piece.Words?.Glyphs.Text == says);

        return new Point(where.X + (where.Width / 2), where.Y + (where.Height / 2));
    }

    [TestMethod]
    public void TheValueStandsWhereTheBindingWasWritten() => UiThread.Run(() =>
    {
        var element = Drawn(Src, new ReflectionDataContext(new Owner()));

        CollectionAssert.Contains(Words(element), "Owned by Platform");
        Assert.IsFalse(Words(element).Contains("Owned by {{Name}}"), "the binding is read, not shown");
    });

    [TestMethod]
    public void WithNothingToReadAgainstItIsDrawnAsItWasWritten() => UiThread.Run(() =>
    {
        var element = Drawn(Src, data: null);

        CollectionAssert.Contains(Words(element), "Owned by {{Name}}",
                                  "a document nobody has bound to still reads");
    });

    [TestMethod]
    public void TheSourceIsUntouchedByWhatIsDrawnOverIt() => UiThread.Run(() =>
    {
        var element = Drawn(Src, new ReflectionDataContext(new Owner()));

        Assert.AreEqual(Src, ((Nexaflow.Visuals.Text.Editing.IEditableBlock)element).Source,
                        "what is drawn is a reading of the source, never a rewrite of it");
    });

    [TestMethod]
    public void PressingABindingRevealsWhatWasWritten() => UiThread.Run(() =>
    {
        var element = Drawn(Src, new ReflectionDataContext(new Owner()));

        element.BeginPointerSelect(Middle(element, "Owned by Platform"));
        element.UpdateLayout();

        CollectionAssert.Contains(Words(element), "Owned by {{Name}}",
                                  "the caret has to stand between characters somebody can see");
    });

    [TestMethod]
    public void RefreshingDrawsWhatTheObjectNowSays() => UiThread.Run(() =>
    {
        var owner = new Owner();
        var element = Drawn(Src, new ReflectionDataContext(owner));

        owner.Name = "Runtime";
        element.Refresh();
        element.UpdateLayout();

        CollectionAssert.Contains(Words(element), "Owned by Runtime",
                                  "nothing watches the object — the host says when it has moved on");
    });
}
