using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Code;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// Alt with Shift and the arrows, or with a drag, picks out a block of columns in code and text; typing writes on every line of it, and a
/// copy of it is said to be a block. Code past the colouring ceiling is shown as it is written, uncoloured.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("content-column-selection")]
public class ColumnKeysTests
{
    [TestMethod]
    public void AltShiftDown_PicksOutABlock_AndTypingWritesOnEveryLine() => UiThread.Run(() =>
    {
        var engine = Typed("one\ntwo\nsix\n", caret: 0);

        engine.Input(new ContentKey(EditingKey.Down, HeldKeys.Alt | HeldKeys.Shift));
        engine.Input(new ContentKey(EditingKey.Down, HeldKeys.Alt | HeldKeys.Shift));
        engine.Input(new ContentText("//"));

        Assert.AreEqual("//one\n//two\n//six\n", engine.State.Document.ToString());

        engine.Undo();
        Assert.AreEqual("one\ntwo\nsix\n", engine.State.Document.ToString(), "every line written at once is one step to take back");
    });

    [TestMethod]
    public void AltAndADrag_PicksOutTheColumnsDraggedOver() => UiThread.Run(() =>
    {
        var engine = Typed("abcdef\nghijkl\n", caret: 0);
        var first = engine.Laid.Root.CaretRect(1);
        var second = engine.Laid.Root.CaretRect("abcdef\n".Length + 4);

        engine.Input(new ContentPress(new LayoutPoint(first.X, first.Y + 1), 1, HeldKeys.Alt));
        engine.Input(new ContentDrag(new LayoutPoint(second.X, second.Y + 1)));
        engine.Input(new ContentRelease());

        Assert.AreEqual("bcd\nhij", engine.State.SelectedText.ReplaceLineEndings("\n"));
    });

    [TestMethod]
    public void ABlockCopied_IsItsLines_SaidToBeABlock() => UiThread.Run(() =>
    {
        var engine = Typed("abc\ndef\n", caret: 0);
        var copies = new Copies();
        engine.Events = copies;
        engine.Apply(ColumnSelection.Between(engine.State, (0, 1), (1, 2), 4), notify: false);

        Assert.IsTrue(engine.Copied());

        var copy = copies.Made.Single();
        Assert.AreEqual("b\ne", copy.Text.ReplaceLineEndings("\n"));
        Assert.IsTrue(copy.Column);
        Assert.IsTrue(MarkdownClipboard.Payload(copy).Contains(MarkdownClipboard.ColumnFormat));
    });

    [TestMethod]
    public void CodePastTheColouringCeiling_IsReadUncoloured() => UiThread.Run(() =>
    {
        var syntax = ContentEngine.Language("csharp").Syntax!;

        Assert.IsNotNull(syntax(ContentDocument.Of("class A { }")));
        Assert.IsNull(syntax(ContentDocument.Of(new string(' ', CodeSyntax.ColoursUpTo + 1))), "too long to colour: shown as written");
    });

    private static ContentEngine Typed(string text, int caret)
    {
        var engine = new ContentEngine();
        engine.Show(ContentLanguages.AsTyped, WpfStyles.Dark);
        engine.Start(text);
        engine.Fit(400);
        engine.Apply(engine.State.MoveCaretTo(caret), notify: false);

        return engine;
    }

    /// <summary>What a page is told is copied, kept.</summary>
    private sealed class Copies : IContentEvents
    {
        public List<MarkdownClipboard.ContentCopy> Made { get; } = [];

        public Task<bool>? OnCopy(MarkdownClipboard.ContentCopy copy)
        {
            this.Made.Add(copy);
            return Task.FromResult(true);
        }

        public bool OnNavigate(string url) => false;

        public bool OnDoubleClick(ContentSelectionChange change) => false;

        public bool OnPaste() => false;

        public BitmapSource? OnPicture(ContentPart block) => null;

        public bool OnBlockSave(ContentPart block) => false;

        public bool OnPage(bool up) => false;

        public bool OnBeforeChange(EditState from, EditState to) => true;

        public void OnSelect(ContentSelectionChange change)
        {
        }
    }
}
