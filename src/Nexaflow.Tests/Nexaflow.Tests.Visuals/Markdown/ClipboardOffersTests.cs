using System;
using System.Linq;
using System.Windows;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The clipboard on a right-click: offered by the engine, which does it, wherever the language the gesture landed in allows it —
/// every one of its ways in a language with no edit handler, whose characters are only characters.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-context-menu")]
public class ClipboardOffersTests
{
    [TestMethod]
    public void InALanguageWithNoEditHandlerWhatIsPickedOutIsOfferedToCutCopyAndPaste() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Code:\n\n```csharp\nvar total = 1;\n```\n", editor =>
        {
            var code = MarkdownEditorHarness.Block(editor)!;
            var at = code.Source.IndexOf("total", code.Start, StringComparison.Ordinal);
            code.Select(at, "total".Length);

            var over = code.Laid.Root.SelfAndDescendants().First(piece => piece.Words is { Maps: true } && piece.Sits().Start <= at && at < piece.Sits().End).Bounds.ToWpf();
            var ribbon = editor.Shown.BuildRibbon(new Point(over.X + 2, over.Y + (over.Height / 2))) as DiagramRibbon;

            Assert.IsNotNull(ribbon, "a right-click opens the ribbon");
            CollectionAssert.IsSubsetOf(new[] { LayoutVerbs.Cut, LayoutVerbs.Copy, LayoutVerbs.Paste }, ribbon!.Offers.Select(offer => offer.Verb).ToArray());
        }));
}
