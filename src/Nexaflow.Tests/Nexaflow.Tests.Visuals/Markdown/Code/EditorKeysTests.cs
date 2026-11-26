using System.Linq;
using System.Windows.Input;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// The keys a text editor has that move and take out by words — and the view of the document a page holds, standing where the engine
/// showing it stands and moving it when the page asks.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("content-editor")]
public class EditorKeysTests
{
    [TestMethod]
    public void CtrlRight_GoesAWordAlong_AndCtrlLeftBack() => UiThread.Run(() =>
    {
        var engine = Typed("int count = total;", caret: 0);

        engine.Input(new ContentKey(Key.Right, ModifierKeys.Control));
        Assert.AreEqual(4, engine.State.Caret);

        engine.Input(new ContentKey(Key.Right, ModifierKeys.Control));
        engine.Input(new ContentKey(Key.Left, ModifierKeys.Control));
        Assert.AreEqual(4, engine.State.Caret);
    });

    [TestMethod]
    public void CtrlShiftRight_PicksOutAWordAtATime() => UiThread.Run(() =>
    {
        var engine = Typed("alpha beta gamma", caret: 0);

        engine.Input(new ContentKey(Key.Right, ModifierKeys.Control | ModifierKeys.Shift));
        engine.Input(new ContentKey(Key.Right, ModifierKeys.Control | ModifierKeys.Shift));

        Assert.AreEqual("alpha beta ", engine.State.SelectedText);
    });

    [TestMethod]
    public void CtrlBackspace_TakesOutTheWordBefore_AndCtrlDeleteTheOneAfter() => UiThread.Run(() =>
    {
        var engine = Typed("alpha beta gamma", caret: 11);

        engine.Input(new ContentKey(Key.Back, ModifierKeys.Control));
        Assert.AreEqual("alpha gamma", engine.State.Document.ToString());

        engine.Input(new ContentKey(Key.Delete, ModifierKeys.Control));
        Assert.AreEqual("alpha ", engine.State.Document.ToString());

        engine.Undo();
        Assert.AreEqual("alpha gamma", engine.State.Document.ToString(), "each was a step of its own to take back");
    });

    [TestMethod]
    public void CtrlB_InText_WritesNoMarks() => UiThread.Run(() =>
    {
        var engine = Typed("plain words", caret: 5);

        engine.Input(new ContentKey(Key.B, ModifierKeys.Control));

        Assert.AreEqual("plain words", engine.State.Document.ToString(), "text has no way to say strong, so nothing is written");
    });

    [TestMethod]
    public void CtrlB_InMarkdown_MakesWhatIsPickedOutStrong() => UiThread.Run(() =>
    {
        var engine = Typed("Some words here.", caret: 0, language: null);
        engine.Apply(engine.State.Select(5, 5), notify: false);

        engine.Input(new ContentKey(Key.B, ModifierKeys.Control));

        Assert.AreEqual("Some **words** here.", engine.State.Document.ToString());
    });

    [TestMethod]
    public void TheViewOfTheDocument_StandsWhereTheEngineDoes() => UiThread.Run(() =>
    {
        var engine = Typed("one two three", caret: 0);
        var editor = engine.Editor;
        var moved = 0;
        editor.CaretMoved += (_, _) => moved++;

        engine.Input(new ContentKey(Key.Right, ModifierKeys.Control));

        Assert.AreEqual(4, editor.Caret);
        Assert.AreEqual(1, moved);
    });

    [TestMethod]
    public void WhatAPageAsksOfTheView_IsMadeByTheEngine() => UiThread.Run(() =>
    {
        var engine = Typed("one two three", caret: 0);

        engine.Editor.Select(4, 3);
        Assert.AreEqual("two", engine.State.SelectedText);

        engine.Editor.ReplaceSelection("2");
        Assert.AreEqual("one 2 three", engine.State.Document.ToString());

        engine.Undo();
        Assert.AreEqual("one two three", engine.State.Document.ToString(), "what the page wrote was one step to take back");
    });

    [TestMethod]
    public void AViewHandedOver_KeepsTheCaretThePagePutThere() => UiThread.Run(() =>
    {
        var document = new EditedDocument("first second");
        var editor = new ContentEditor(document);
        editor.MoveCaretTo(6);

        var engine = Typed("ignored", caret: 0);
        engine.Editor = editor;

        Assert.AreEqual("first second", engine.State.Document.ToString());
        Assert.AreEqual(6, engine.State.Caret);
        Assert.IsTrue(editor.IsShown);
    });

    private static ContentEngine Typed(string text, int caret, string? language = ContentLanguages.AsTyped)
    {
        var engine = new ContentEngine { WrapsLines = language is not null };
        engine.Show(language, WpfStyles.Dark);
        engine.Start(text);
        engine.Fit(400);
        engine.Apply(engine.State.MoveCaretTo(caret), notify: false);

        return engine;
    }
}
