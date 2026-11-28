using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>What a view of a document folds away: whole lines after the one a fold starts on, held through edits, opened again where a caret lands among them.</summary>
[TestClass]
[CoversNode("code-folding")]
public class ContentFoldsTests
{
    private const string Code = "class C\n{\n    void M()\n    {\n        return;\n    }\n}\n";

    [TestMethod]
    public void AFold_HidesTheLinesAfterTheOneItStartsOn_UpToTheOneItEndsOn()
    {
        var document = ContentDocument.Of(Code);
        var (start, end) = (Code.IndexOf('{'), Code.LastIndexOf('}'));

        Assert.IsFalse(ContentFolds.Hides(document, start, end, Code.IndexOf("class", System.StringComparison.Ordinal)), "the line before it");
        Assert.IsFalse(ContentFolds.Hides(document, start, end, start), "the line it starts on stays");
        Assert.IsTrue(ContentFolds.Hides(document, start, end, Code.IndexOf("return", System.StringComparison.Ordinal)));
        Assert.IsTrue(ContentFolds.Hides(document, start, end, end), "the line it ends on goes with it");
    }

    [TestMethod]
    public void AStretchOnOneLine_HasNothingToFold()
    {
        using var editor = new ContentEditor(new EditedDocument("a { b } c"));

        Assert.IsFalse(editor.Folds.Fold(2, 7));
        Assert.AreEqual(0, editor.Folds.Ranges.Count);
    }

    [TestMethod]
    public void AFold_StaysOverWhatItFolded_ThroughWritesBeforeIt()
    {
        var document = new EditedDocument(Code);
        using var editor = new ContentEditor(document);
        var (start, end) = (Code.IndexOf("    {", System.StringComparison.Ordinal) + 4, Code.IndexOf("    }", System.StringComparison.Ordinal) + 4);
        editor.Folds.Fold(start, end);

        document.Write(ContentChange.Write(0, 0, "// header\n"));

        var (now, to) = editor.Folds.Ranges.Single();
        Assert.AreEqual('{', document.Current[now]);
        Assert.AreEqual('}', document.Current[to]);
    }

    [TestMethod]
    public void ACaretOnAHiddenLine_OpensWhatHidIt()
    {
        var document = new EditedDocument(Code);
        using var editor = new ContentEditor(document);
        editor.Folds.Fold(Code.IndexOf('{'), Code.LastIndexOf('}'));
        var opened = 0;
        editor.Folds.Changed += (_, _) => opened++;

        Assert.IsFalse(editor.Folds.Reveal(Code.IndexOf('{')), "the line a fold starts on is not hidden");
        Assert.IsTrue(editor.Folds.Reveal(Code.IndexOf("return", System.StringComparison.Ordinal)));
        Assert.AreEqual(0, editor.Folds.Ranges.Count);
        Assert.AreEqual(1, opened);
    }

    [TestMethod]
    public void TakingOutEverythingAFoldHeld_LetsItGo()
    {
        var document = new EditedDocument(Code);
        using var editor = new ContentEditor(document);
        var (start, end) = (Code.IndexOf('{'), Code.LastIndexOf('}'));
        editor.Folds.Fold(start, end);

        document.Write(ContentChange.Write(start, end - start + 1, string.Empty));

        Assert.AreEqual(0, editor.Folds.Ranges.Count);
    }

    [TestMethod]
    public void Toggling_FoldsThenOpens_AndFoldingEverything_FoldsWhatIsOffered()
    {
        using var editor = new ContentEditor(new EditedDocument(Code));
        var (start, end) = (Code.IndexOf('{'), Code.LastIndexOf('}'));

        Assert.IsTrue(editor.Folds.Toggle(start, end));
        Assert.IsTrue(editor.Folds.IsFolded(start));
        Assert.IsTrue(editor.Folds.Toggle(start, end));
        Assert.IsFalse(editor.Folds.IsFolded(start));

        var inner = (Code.IndexOf("    {", System.StringComparison.Ordinal) + 4, Code.IndexOf("    }", System.StringComparison.Ordinal) + 4);
        editor.Folds.FoldAll([(start, end), inner]);
        Assert.AreEqual(2, editor.Folds.Ranges.Count);

        editor.Folds.UnfoldAll();
        Assert.AreEqual(0, editor.Folds.Ranges.Count);
    }
}
