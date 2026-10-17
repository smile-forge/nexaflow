using Nexaflow.AiHost.Contracts;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Tools;

/// <summary>What Unlimited-OCR writes read into a page's blocks as it writes it, and the regions of text that make a
/// picture a page.</summary>
[TestClass]
[CoversNode("page-reading")]
public class PageReadingTests
{
    private const string Reply =
        "<|det|>title [114, 431, 340, 447]<|/det|>3.1. Long-horizon Parsing\n" +
        "<|det|>text [113, 456, 885, 520]<|/det|>We divide each value by \\( \\sqrt{d_k} \\).\n" +
        "<|det|>equation [355, 585, 825, 620]<|/det|>\\[\nx = 1 \\tag {1}\n\\]\n" +
        "<|det|>image [123, 98, 862, 269]<|/det|>\n" +
        "<|det|>image_caption [113, 276, 885, 374]<|/det|>Figure 2 | The model.\n" +
        "<|det|>table [55, 64, 946, 589]<|/det|><table><tr><td>a</td></tr></table>\n" +
        "<|det|>page_footnote [171, 883, 825, 914]<|/det|>\\( ^{4} \\) A note.\n" +
        "<|det|>page_number [494, 936, 505, 948]<|/det|>4";

    [TestMethod]
    public void AReply_IsReadIntoItsBlocks_OfTheirKinds_WhereTheyStand_SayingWhatTheySay()
    {
        var blocks = PageMarks.Read(Reply);

        CollectionAssert.AreEqual(
            new[] { PageBlockKind.Title, PageBlockKind.Text, PageBlockKind.Equation, PageBlockKind.Figure, PageBlockKind.Caption,
                    PageBlockKind.Table, PageBlockKind.Footnote, PageBlockKind.Margin },
            blocks.Select(b => b.Kind).ToArray());
        Assert.AreEqual(114 / 999.0, blocks[0].Left, 1e-9);
        Assert.AreEqual(447 / 999.0, blocks[0].Bottom, 1e-9);
        Assert.AreEqual("3.1. Long-horizon Parsing", blocks[0].Content);
        Assert.AreEqual("\\[\nx = 1 \\tag {1}\n\\]", blocks[2].Content, "an equation keeps its lines");
        Assert.AreEqual("", blocks[3].Content, "a figure says nothing");
    }

    [TestMethod]
    public void AReplyWrittenInPieces_HandsEachBlockOnOnceTheNextOpens_AndTheLastAtTheEnd()
    {
        var marks = new PageMarks();
        var handed = new List<(int Piece, PageBlock Block)>();
        var pieces = Enumerable.Range(0, (Reply.Length + 6) / 7).Select(i => Reply.Substring(i * 7, Math.Min(7, Reply.Length - i * 7))).ToList();   // marks split across pieces
        for (int i = 0; i < pieces.Count; i++)
            foreach (var block in marks.Take(pieces[i])) handed.Add((i, block));
        foreach (var block in marks.Finish()) handed.Add((pieces.Count, block));

        CollectionAssert.AreEqual(PageMarks.Read(Reply).ToList(), handed.Select(h => h.Block).ToList());
        Assert.AreEqual(pieces.Count, handed[^1].Piece, "the last block waits for the reply's end");
        Assert.IsTrue(handed[0].Piece < pieces.Count / 4, "the first is handed on as soon as the second opens");
    }

    [TestMethod]
    public void AReplyWhoseFirstMarkWasLeftOut_StillHasItsFirstBlock()
    {
        var blocks = PageMarks.Read("image [0, 0, 999, 999]<|/det|>");

        Assert.AreEqual(1, blocks.Count);
        Assert.AreEqual(PageBlockKind.Figure, blocks[0].Kind);
        Assert.AreEqual(1.0, blocks[0].Right, 1e-9);
    }

    [TestMethod]
    public void AKindTheReaderHasNoNameFor_IsProse()
        => Assert.AreEqual(PageBlockKind.Text, PageMarks.Read("<|det|>code [1, 2, 3, 4]<|/det|>x = 1")[0].Kind);

    private static List<TextRegion> Lines(int count, float width, float height)
        => [.. Enumerable.Range(0, count).Select(i => new TextRegion(0.1f, i * 0.02f, width, height, 0.9f))];

    [TestMethod]
    public void Writing_OverAPage_MakesItAPage_AndAFewWords_DoNot()
    {
        Assert.IsTrue(PageReading.IsPage(Lines(40, 0.8f, 0.012f)), "a page of prose: 40 lines");
        Assert.IsTrue(PageReading.IsPage(Lines(30, 0.15f, 0.012f)), "a receipt's lines: 30 short ones, 5 % of it");
        Assert.IsFalse(PageReading.IsPage(Lines(13, 0.05f, 0.02f)), "a neon sign's letters");
        Assert.IsFalse(PageReading.IsPage(Lines(3, 0.3f, 0.03f)), "a street sign");
        Assert.IsFalse(PageReading.IsPage(Lines(20, 0.02f, 0.01f)), "labels on clothes: many, but tiny");
    }
}

