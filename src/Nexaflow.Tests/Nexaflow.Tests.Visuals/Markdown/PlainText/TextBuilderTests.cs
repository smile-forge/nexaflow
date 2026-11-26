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
    public void ALineIsSetAsOneRunOfWhatIsWrittenOnIt_ATabAPieceOfItsOwn()
    {
        var laid = Laying.Lay(TextParser.Language, "one  two\tthree\nfour\n", style: WpfStyles.Dark);
        var words = Words(laid).Where(piece => piece.Words is not null).ToList();

        CollectionAssert.AreEqual(new[] { "one  two", "three", "four" }, words.Select(piece => piece.Words!.Glyphs.Text).ToArray(),
            "its words are the reading's, shaped together as far as a tab, which reaches to its stop as a piece of its own");
        Assert.IsTrue(words.All(piece => piece.Words!.Maps), "it is the source, so the caret goes straight into it");

        var tab = Words(laid).Single(piece => piece.Words is null);
        Assert.AreEqual(("one  two".Length, 1), (tab.Part!.Start, tab.Part.Length), "the tab is a piece standing for its one character");
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
