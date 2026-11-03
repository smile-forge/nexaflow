using System.Linq;
using System.Windows.Input;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// Enter and Tab in code, and in text shown as typed, as a text editor has them: a line broken carries on at its indent, Tab writes a
/// tab or indents the lines picked out — leaving them picked out — and Shift+Tab takes a level of indent off.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("markdown-source-box")]
public class LineIndentingTests
{
    [TestMethod]
    public void Enter_CarriesTheLinesIndentOnToTheNextLine() => UiThread.Run(() =>
    {
        var engine = Typed("- one\n  - two\nthree", caret: "- one\n  - two".Length);

        engine.Input(new ContentKey(Key.Enter));
        engine.Input(new ContentText("x"));

        Assert.AreEqual("- one\n  - two\n  x\nthree", engine.State.Document.ToString());
    });

    [TestMethod]
    public void Enter_InACodeFenceOfADocument_CarriesTheLinesIndentOnToo() => UiThread.Run(() =>
    {
        // A fence in a language a grammar reads is that language's to write in; one in a language nothing reads is the document's own.
        const string document = "Before.\n\n```csharp\n    indented();\n```\n\nAfter.\n";
        var engine = Typed(document, caret: document.IndexOf("indented();", System.StringComparison.Ordinal) + "indented();".Length, language: null);

        engine.Input(new ContentKey(Key.Enter));

        StringAssert.Contains(engine.State.Document.ToString(), "    indented();\n    \n```");
    });

    [TestMethod]
    public void Tab_WritesATabAtTheCaret() => UiThread.Run(() =>
    {
        var engine = Typed("one\ntwo", caret: 4);

        Assert.IsTrue(engine.Input(new ContentKey(Key.Tab)), "Tab was left to the window, to move on with");
        Assert.AreEqual("one\n\ttwo", engine.State.Document.ToString());
    });

    [TestMethod]
    public void TabOverLinesPickedOut_IndentsEachOfThem_AndLeavesThemPickedOut() => UiThread.Run(() =>
    {
        const string text = "one\ntwo\nthree";
        var engine = Typed(text, caret: 0);
        engine.Apply(engine.State.Select(0, "one\ntwo\n".Length), notify: false);

        engine.Input(new ContentKey(Key.Tab));
        Assert.AreEqual("\tone\n\ttwo\nthree", engine.State.Document.ToString(), "a line picked out only to its start was indented");

        engine.Input(new ContentKey(Key.Tab));
        Assert.AreEqual("\t\tone\n\t\ttwo\nthree", engine.State.Document.ToString(), "pressed again, it did not indent the same lines again");

        var picked = engine.State.Selection.Single();
        Assert.AreEqual((0, "\t\tone\n\t\ttwo\n".Length), (picked.Start, picked.End), "the lines indented are not what is picked out");
    });

    [TestMethod]
    public void ShiftTab_TakesALevelOfIndentOff_ATabOrUpToFourSpaces() => UiThread.Run(() =>
    {
        const string text = "\tone\n      two\n  three\nfour";
        var engine = Typed(text, caret: 0);
        engine.Apply(engine.State.Select(0, text.Length), notify: false);

        engine.Input(new ContentKey(Key.Tab, ModifierKeys.Shift));
        Assert.AreEqual("one\n  two\nthree\nfour", engine.State.Document.ToString());

        var bare = Typed("four", caret: 2);
        Assert.IsTrue(bare.Input(new ContentKey(Key.Tab, ModifierKeys.Shift)), "with no indent to take off, Shift+Tab still moved on out of the code");
        Assert.AreEqual("four", bare.State.Document.ToString());
    });

    /// <summary><paramref name="text"/> laid to be written in, as typed unless a language is named, the caret at <paramref name="caret"/>.</summary>
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
