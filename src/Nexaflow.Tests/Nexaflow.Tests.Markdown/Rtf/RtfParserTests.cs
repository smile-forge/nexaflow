using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Markdown.Rtf;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Rtf;

/// <summary>
/// The tree an RTF document is read into: it prints back as exactly what was written, whatever that was, and it is the
/// tree a document is drawn from — paragraphs of words, tables of cells, links and pictures — with what the formatting
/// comes to hung under the words it applies to.
///
/// <para>
/// The documents include what nobody means to write — a brace closing nothing, a group never closed, half a hex escape —
/// because RTF arrives from every mail client there is, and a reader of it is told to read past what it does not know.
/// </para>
/// </summary>
[TestClass]
[CoversNode("rtf-reading")]
public class RtfParserTests
{
    private const string Header = @"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss\fcharset0 Arial;}{\f1\froman\fcharset2 Symbol;}}" +
                                  @"{\colortbl ;\red255\green0\blue0;\red0\green0\blue255;}" + "\r\n";

    private static readonly (string What, string Source)[] Documents =
    [
        ("nothing at all", ""),
        ("a paragraph", @"{\rtf1\ansi Hello world\par}"),
        ("formatting", Header + @"\pard\f0\fs24 Plain {\b bold} \i italic\i0  {\cf1 red}\par}"),
        ("codes", @"{\rtf1\ansi\ansicpg1252\uc1 caf\'e9 \u8212? dash\par}"),
        ("a double-byte character broken over a line", @"{\rtf1\ansi\ansicpg932{\fonttbl{\f0\fcharset128 MS Mincho;}}\f0 \'82" + "\r\n" + @"\'a0\par}"),
        ("a table", @"{\rtf1\ansi\trowd\cellx1000\cellx2000\pard\intbl A\cell B\cell\row\pard After\par}"),
        ("merged cells", @"{\rtf1\ansi\trowd\clmgf\cellx1000\clmrg\cellx2000\cellx3000\pard\intbl wide\cell\cell C\cell\row}"),
        ("a nested table", @"{\rtf1\ansi\trowd\cellx3000\pard\intbl outer\par\itap2 inner\nestcell{\*\nesttableprops\trowd\cellx1000\nestrow}{\nonesttables\par}\itap1 after\cell\row}"),
        ("a link", Header + @"See {\field{\*\fldinst{HYPERLINK ""https://example.com""}}{\fldrslt{\ul\cf2 Example}}} now.\par}"),
        ("a picture", @"{\rtf1\ansi Before {\pict\pngblip\picwgoal1440\pichgoal720 89504e47" + "\r\n" + @"0d0a1a0a} after\par}"),
        ("hidden text and skipped groups", @"{\rtf1\ansi{\*\unknown stuff}{\info{\title T}}Shown{\v hidden}\par}"),
        ("breaks and tabs", @"{\rtf1\ansi a\line b\tab c" + "\t" + @"d\par}"),
        ("a symbol font", Header + @"{\f1\'b7}\tab item\par}"),
        ("a brace closing nothing", @"{\rtf1 a}}b"),
        ("a group never closed", @"{\rtf1{\b bold"),
        ("half a hex escape", @"{\rtf1 x\'4"),
        ("a backslash at the end", @"{\rtf1 x\"),
        ("a backslash ending a line", "{\\rtf1 one\\\r\ntwo}"),
        ("raw eight-bit text", "{\\rtf1\\ansi caf\u00E9\\par}"),
        ("a \\bin", @"{\rtf1{\pict\pngblip\bin4 }{\\}after}"),
    ];

    [TestMethod]
    public void EveryDocumentReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in Documents)
            Assert.AreEqual(source, RtfParser.Parse(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryDocumentReadsBackToo()
    {
        foreach (var (what, source) in Documents)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, RtfParser.Parse(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void EveryPieceSaysWhereItWasRead()
    {
        foreach (var (what, source) in Documents)
        {
            var faults = AstOracle.Faults(source, RtfParser.Parse(source)).ToList();

            Assert.AreEqual(0, faults.Count, $"{what}\n{string.Join("\n", faults)}");
        }
    }

    [TestMethod]
    public void AReversedTreeStillPrintsWhatWasWritten()
    {
        foreach (var (what, source) in Documents)
            Assert.AreEqual(source, AstOracle.Reversed(RtfParser.Parse(source)).Print(), what);
    }

    [TestMethod]
    public void TheDocumentIsItsParagraphs_AndWhatIsBetweenThemIsTrivia()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi one\par two\par}");

        Assert.AreEqual(RtfParser.Language, ((BlockNode)tree).Language);
        CollectionAssert.AreEqual(new[] { "one", "two" }, Paragraphs(tree).Select(Drawn).ToArray());
        Assert.IsTrue(tree.Children.Where(block => block.Kind != MarkdownKinds.Paragraph).All(block => block.Role == Roles.Trivia));
    }

    [TestMethod]
    public void AParagraphWithNothingInItIsABlankLine()
    {
        var tree = RtfParser.Parse(@"{\rtf1 one\par\par two\par}");

        CollectionAssert.AreEqual(new[] { "one", "", "two" }, Paragraphs(tree).Select(Drawn).ToArray());
    }

    [TestMethod]
    public void TheMachineryIsNotDrawn_OnlyTheWords()
    {
        var tree = RtfParser.Parse(Header + @"\pard\f0\fs24 Plain {\b bold} \i italic\i0  {\cf1 red}\par}");

        Assert.AreEqual("Plain bold italic red", Drawn(Paragraphs(tree).Single()));
    }

    [TestMethod]
    public void ARunIsSetAsItsFormattingSays()
    {
        var tree = RtfParser.Parse(Header + @"\pard\f0 Plain {\b\fs48 big} {\cf1\i red}\par}");

        var looks = Styled(tree).ToDictionary(run => Drawn(run), run => (TextLook)run.HeldAs(MarkdownRoles.Typeset)!);

        Assert.IsTrue(looks["big"].Bold);
        Assert.AreEqual(2.0, looks["big"].Scale);
        Assert.AreEqual("Arial", looks["big"].Font);
        Assert.IsTrue(looks["red"].Italic);
        Assert.AreEqual(0xFFFF0000u, looks["red"].Ink);
    }

    [TestMethod]
    public void PlainTextIsWrittenWithNoLookHungOnIt()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi plain words\par}");

        Assert.AreEqual(0, Styled(tree).Count());
    }

    [TestMethod]
    public void ARunOfCodesIsDrawnAsWhatItDecodesTo()
    {
        Assert.AreEqual("caf\u00E9 \u2014 dash", Drawn(Paragraphs(RtfParser.Parse(@"{\rtf1\ansi\ansicpg1252\uc1 caf\'e9 \u8212? dash\par}")).Single()));
    }

    [TestMethod]
    public void TheCharacterStandingInForAUnicodeEscapeIsReadPast()
    {
        Assert.AreEqual("a\u2014b", Drawn(Paragraphs(RtfParser.Parse(@"{\rtf1\uc2 a\u8212\'97\'97b\par}")).Single()));
    }

    [TestMethod]
    public void TwoBytesOfADoubleByteCharacterAreOneCharacter_EvenAcrossALine()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi\ansicpg932{\fonttbl{\f0\fcharset128 MS Mincho;}}\f0 \'82" + "\r\n" + @"\'a0\par}");

        Assert.AreEqual("\u3042", Drawn(Paragraphs(tree).Single()));
    }

    [TestMethod]
    public void RawEightBitTextIsReadInTheDocumentsCodePage()
    {
        Assert.AreEqual("caf\u00E9", Drawn(Paragraphs(RtfParser.Parse("{\\rtf1\\ansi caf\u00E9\\par}")).Single()));
    }

    [TestMethod]
    public void ASymbolFontsBytesAreItsGlyphs()
    {
        Assert.AreEqual("\uF0B7\titem", Drawn(Paragraphs(RtfParser.Parse(Header + @"{\f1\'b7}\tab item\par}")).Single()));
    }

    [TestMethod]
    public void HiddenTextAndUnknownDestinationsAreNotDrawn()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi{\*\unknown stuff}{\info{\title T}}Shown{\v hidden}\par}");

        Assert.AreEqual("Shown", Drawn(Paragraphs(tree).Single()));
    }

    [TestMethod]
    public void ALineBreakAndATabAreTheirOwnPieces()
    {
        var paragraph = Paragraphs(RtfParser.Parse(@"{\rtf1\ansi a\line b\tab c\par}")).Single();

        Assert.AreEqual("a\nb\tc", Drawn(paragraph));
        Assert.AreEqual(MarkdownRoles.Hard, paragraph.SelfAndDescendants().Single(node => node.Kind == MarkdownKinds.Break).Role);
    }

    [TestMethod]
    public void AParagraphIsSetAsItsFormattingSays()
    {
        var paragraph = Paragraphs(RtfParser.Parse(@"{\rtf1\pard\qc\li720\fi-360\sa240\tx1440 centred\par}")).Single();
        var look = (ParagraphLook)paragraph.HeldAs(MarkdownRoles.Typeset)!;

        Assert.AreEqual(ParagraphAlign.Centre, look.Align);
        Assert.AreEqual(3.0, look.Indent);
        Assert.AreEqual(-1.5, look.FirstLine);
        Assert.AreEqual(1.0, look.After);
        CollectionAssert.AreEqual(new[] { 6.0 }, look.Stops.ToArray());
    }

    [TestMethod]
    public void ATableIsItsRowsOfCells_EachCellItsBlocks()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi\trowd\cellx1000\cellx2000\pard\intbl A\cell B\cell\row\pard After\par}");
        var table = tree.Children.Single(block => block.Kind == MarkdownKinds.Table);
        var cells = table.Children.Single().Children.Where(part => part.Kind == MarkdownKinds.Cell).ToList();

        CollectionAssert.AreEqual(new[] { "A", "B" }, cells.Select(Drawn).ToArray());
        Assert.IsTrue(cells.All(cell => cell.Children.Any(part => part.Held is true)));
        Assert.AreEqual("After", Drawn(Paragraphs(tree).Single()));
    }

    [TestMethod]
    public void CellsMergedAcrossAreOneCellCoveringThem()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi\trowd\clmgf\cellx1000\clmrg\cellx2000\cellx3000\pard\intbl wide\cell\cell C\cell\row}");
        var row = tree.Children.Single(block => block.Kind == MarkdownKinds.Table).Children.Single();
        var cells = row.Children.Where(part => part.Kind == MarkdownKinds.Cell).ToList();

        Assert.AreEqual(2, cells.Count);
        Assert.AreEqual(new MarkdownSpans(2, 1), cells[0].Children.Select(part => part.Held).OfType<MarkdownSpans>().Single());
    }

    [TestMethod]
    public void CellsMergedDownAreOneCellCoveringThem()
    {
        var tree = RtfParser.Parse(@"{\rtf1\trowd\clvmgf\cellx1000\cellx2000\pard\intbl tall\cell x\cell\row" +
                                   @"\trowd\clvmrg\cellx1000\cellx2000\pard\intbl\cell y\cell\row}");
        var rows = tree.Children.Single(block => block.Kind == MarkdownKinds.Table).Children;

        Assert.AreEqual(new MarkdownSpans(1, 2), rows[0].Children[0].Children.Select(part => part.Held).OfType<MarkdownSpans>().Single());
        Assert.AreEqual(1, rows[1].Children.Count(part => part.Kind == MarkdownKinds.Cell));
    }

    [TestMethod]
    public void ANestedTableIsABlockOfTheCellHoldingIt()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi\trowd\cellx3000\pard\intbl outer\par\itap2 inner\nestcell{\*\nesttableprops\trowd\cellx1000\nestrow}{\nonesttables\par}\itap1 after\cell\row}");
        var outer = tree.Children.Single(block => block.Kind == MarkdownKinds.Table);
        var cell = outer.Children.Single().Children.Single(part => part.Kind == MarkdownKinds.Cell);
        var body = cell.Part(Roles.Body)!;

        Assert.AreEqual(1, body.Children.Count(block => block.Kind == MarkdownKinds.Table));
        Assert.AreEqual("inner", Drawn(body.Children.Single(block => block.Kind == MarkdownKinds.Table)));
    }

    [TestMethod]
    public void AHyperlinkFieldIsALinkToWhereItLeads_ItsResultTheWords()
    {
        var tree = RtfParser.Parse(Header + @"See {\field{\*\fldinst{HYPERLINK ""https://example.com"" \\l ""top""}}{\fldrslt{\ul\cf2 Example}}} now.\par}");
        var link = tree.SelfAndDescendants().Single(node => node.Kind == Kinds.Link && node.Role == Roles.Element);

        Assert.AreEqual("https://example.com#top", MarkdownLinks.Goes(link));
        Assert.AreEqual("Example", Drawn(link.Part(Roles.Body)!));
        Assert.AreEqual("See Example now.", Drawn(Paragraphs(tree).Single()));
    }

    [TestMethod]
    public void AFieldThatIsNotALinkShowsItsResult()
    {
        var tree = RtfParser.Parse(@"{\rtf1 Page {\field{\*\fldinst PAGE}{\fldrslt 3}}\par}");

        Assert.IsFalse(tree.SelfAndDescendants().Any(node => node.Kind == Kinds.Link));
        Assert.AreEqual("Page 3", Drawn(Paragraphs(tree).Single()));
    }

    [TestMethod]
    public void APictureIsOnePieceHoldingItsBytes_AtTheSizeItWasAskedFor()
    {
        var tree = RtfParser.Parse(@"{\rtf1\ansi Before {\pict\pngblip\picwgoal1440\pichgoal720 89504e47" + "\r\n" + @"0d0a1a0a} after\par}");
        var image = tree.SelfAndDescendants().Single(node => node.Kind == MarkdownKinds.Image && node.Role == Roles.Element);
        var picture = image.Children.Select(part => part.Held).OfType<EmbeddedPicture>().Single();

        Assert.AreEqual("image/png", picture.MediaType);
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }, picture.Bytes);
        Assert.AreEqual(6.0, picture.Width);
        Assert.AreEqual(3.0, picture.Height);
        Assert.AreEqual("Before  after", Drawn(Paragraphs(tree).Single()));
    }

    [TestMethod]
    public void TheShapePictureIsDrawn_AndTheOldReadersCopyIsNot()
    {
        var tree = RtfParser.Parse(@"{\rtf1{\*\shppict{\pict\pngblip 89}}{\nonshppict{\pict\wmetafile8 01}}\par}");

        Assert.AreEqual("image/png", tree.SelfAndDescendants().Select(node => node.Held).OfType<EmbeddedPicture>().Single().MediaType);
    }

    [TestMethod]
    public void AMetafileCarriesWhatPlayingItNeeds()
    {
        var tree = RtfParser.Parse(@"{\rtf1{\pict\wmetafile8\picw2540\pich1270 0100}\par}");
        var picture = tree.SelfAndDescendants().Select(node => node.Held).OfType<EmbeddedPicture>().Single();

        Assert.AreEqual(new PictureMetafile(8, 2540, 1270), picture.Metafile);
        Assert.AreEqual(6.0, picture.Width!.Value, 1e-9);
    }

    [TestMethod]
    public void ADocumentsWordsComeOutAsPlainText()
    {
        var text = RtfParser.Text(Header + @"\pard One {\b bold}\par\trowd\cellx1000\cellx2000\pard\intbl A\cell B\cell\row\pard caf\'e9\tab end\par}");

        Assert.AreEqual("One bold\nA\tB\ncaf" + (char)0xE9 + "\tend", text);
    }

    // ── Reading the tree ────────────────────────────────────────────────────

    private static IEnumerable<ContentNode> Paragraphs(ContentNode tree) =>
        tree.Children.Where(block => block.Kind == MarkdownKinds.Paragraph);

    private static IEnumerable<ContentNode> Styled(ContentNode tree) =>
        tree.SelfAndDescendants().Where(node => node.Kind == MarkdownKinds.Styled);

    /// <summary>What a reader of a piece sees: its words, what its codes decode to, its tabs and its breaks.</summary>
    private static string Drawn(ContentNode node)
    {
        var drawn = new StringBuilder();
        Walk(node);

        return drawn.ToString();

        void Walk(ContentNode piece)
        {
            if (piece.IsDerived || piece.Role is Roles.Silent or Roles.Name or Roles.Open or Roles.Close or Roles.Trivia) return;

            switch (piece.Kind)
            {
                case MarkdownKinds.Encoded: drawn.Append(piece.HeldAs(MarkdownRoles.Decoded)); return;
                case MarkdownKinds.Tab: drawn.Append('\t'); return;
                case MarkdownKinds.Break: drawn.Append('\n'); return;
                case MarkdownKinds.Image: return;
                case MarkdownKinds.Word: drawn.Append(piece.Text); return;
            }

            foreach (var child in piece.Children) Walk(child);
        }
    }
}
