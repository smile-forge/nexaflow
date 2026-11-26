using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Code;

namespace Nexaflow.Tests.Visuals.Editor;

/// <summary>
/// The colours a code file writes, barred in their own colour: found among what the grammar coloured on the stretch drawn, and nowhere
/// else — a name that is no colour grows no bar, and content no grammar read is asked nothing.
/// </summary>
[TestClass]
[CoversNode("vtext-color-swatch")]
public class ColourSwatchMarksTests
{
    [TestMethod]
    public void AColourTheSourceWrites_IsBarredInThatColour()
    {
        const string css = ".a { color: #ff0000; border: solid; }";
        var (document, marks) = Marked("css", css);

        var red = marks.Single(mark => document.Slice(mark.Start, mark.Length) == "#ff0000");
        Assert.AreEqual(ContentMarkKind.Underbar, red.Kind);
        Assert.AreEqual(0xFFFF0000u, red.Colour);

        Assert.IsFalse(marks.Any(mark => mark.Colour is not null && document.Slice(mark.Start, mark.Length) == "solid"),
            "a keyword names no colour");
    }

    [TestMethod]
    public void XamlReadsTheAlphaFirst_AsCssReadsItLast()
    {
        const string xaml = "<Border Background=\"#80FF0000\"/>";
        var (document, marks) = Marked("xaml", xaml);

        var half = marks.Single(mark => mark.Colour is not null);
        StringAssert.Contains(document.Slice(half.Start, half.Length), "#80FF0000");
        Assert.AreEqual(0x80FF0000u, half.Colour, "#AARRGGBB, as WPF reads it");
    }

    [TestMethod]
    public void OnlyTheStretchDrawn_IsMarked()
    {
        const string css = ".a { color: #ff0000; }\n.b { color: #00ff00; }";
        var (document, _) = Marked("css", css);
        var syntax = CodeSyntax.Of("css", document);
        syntax.Find(0, css.Length);

        var marks = new List<ContentMark>();
        var second = css.IndexOf(".b", System.StringComparison.Ordinal);
        new ColourSwatchMarks("css").Mark(new ContentMarkRequest(document, second, css.Length) { Syntax = syntax }, marks);

        Assert.AreEqual(1, marks.Count(mark => mark.Colour is not null));
        Assert.AreEqual(0xFF00FF00u, marks.Single(mark => mark.Colour is not null).Colour);
    }

    [TestMethod]
    public void ContentNoGrammarRead_IsAskedNothing()
    {
        var marks = new List<ContentMark>();
        new ColourSwatchMarks("css").Mark(new ContentMarkRequest(ContentDocument.Of("a { color: red; }"), 0, 17), marks);

        Assert.AreEqual(0, marks.Count);
    }

    private static (ContentDocument Document, List<ContentMark> Marks) Marked(string grammar, string source)
    {
        var document = ContentDocument.Of(source);
        var syntax = CodeSyntax.Of(grammar, document);
        Assert.IsNotNull(syntax.Find(0, source.Length), "the grammar read it");

        var marks = new List<ContentMark>();
        new ColourSwatchMarks(grammar).Mark(new ContentMarkRequest(document, 0, source.Length) { Syntax = syntax }, marks);

        return (document, marks);
    }
}
