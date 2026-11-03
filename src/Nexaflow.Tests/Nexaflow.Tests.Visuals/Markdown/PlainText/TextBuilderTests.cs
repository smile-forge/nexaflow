using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.PlainText;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.PlainText;

/// <summary>Plain text laid: line under line, each line one run of what is written on it, kept whole where nothing asks it to wrap.</summary>
[TestClass]
[CoversNode("md-plain-text")]
public class TextBuilderTests
{
    [TestMethod]
    public void ALineIsSetAsOneRunOfWhatIsWrittenOnIt()
    {
        var laid = Laying.Lay(TextParser.Language, "one  two\tthree\nfour\n", style: WpfStyles.Dark);

        CollectionAssert.AreEqual(new[] { "one  two\tthree", "four" }, Words(laid).Select(piece => piece.Words!.Glyphs.Text).ToArray(),
            "its words are the reading's; nothing draws one apart from another, so they are shaped together");
        Assert.IsTrue(Words(laid).All(piece => piece.Words!.Maps), "it is the source, so the caret goes straight into it");
    }

    [TestMethod]
    public void ItsLinesStandOneUnderAnother_AndAreKeptWhole_WhereNothingAsksThemToWrap()
    {
        var line = string.Join(' ', Enumerable.Range(0, 40).Select(word => $"word{word}"));

        var one = Laying.Lay(TextParser.Language, "x", room: 200, style: WpfStyles.Dark);
        var laid = Laying.Lay(TextParser.Language, line + "\nshort\n", room: 200, style: WpfStyles.Dark);

        Assert.IsTrue(laid.Size.Width > 200, "a line kept whole reaches past the room");
        Assert.AreEqual(3 * one.Size.Height, laid.Size.Height, 0.01, "two lines, and the line after the last line end for the caret to stand on");
    }

    /// <summary>Every run of words <paramref name="laid"/> sets.</summary>
    private static List<LayoutPiece> Words(Laid laid) => [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == Kinds.Words)];
}
