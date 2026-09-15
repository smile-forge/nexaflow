using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Chemistry;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Chemistry;

/// <summary>
/// The drawing end: that a <c>smiles</c> block reaches the builder through the dispatch every fenced block uses, that
/// a structure is made of atoms and bonds each carrying what was typed for it, that trouble is said on the atom that
/// caused it, and that a block which will not read still draws something.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("smiles-render")]
public class SmilesBuilderTests
{
    [TestMethod]
    public void SmilesIsADiagramLanguage()
    {
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("smiles"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("SMILES"));
        Assert.IsFalse(DiagramRenderer.IsDiagramLanguage("chemistry"), "the keyword opens a block; it does not name the fence");
    }

    [TestMethod]
    public void DispatchesThroughDiagramRenderer_ToContentThatCannotBeEdited() => UiThread.Run(() =>
    {
        var content = (ContentElement)DiagramRenderer.Render("smiles", "chemistry\nCCO \"Ethanol\"", MarkdownPalette.Dark);
        Assert.IsTrue(content.IsReadOnly);

        content.Measure(new Size(600, double.PositiveInfinity));
        Assert.AreEqual(0, content.Diagnostics.Count);
        Assert.IsTrue(content.DesiredSize.Width > 0 && content.DesiredSize.Height > 0);
    });

    [TestMethod]
    public void EveryAtomIsAPieceCarryingWhatWasTypedForIt() => UiThread.Run(() =>
    {
        const string source = "CC(=O)O";
        var laid = Build(source);

        var atoms = Pieces(laid, MoleculePiece.Atom).Select(piece => Text(source, piece.Part)).ToArray();
        CollectionAssert.AreEqual(new[] { "C", "C", "O", "O" }, atoms);
    });

    [TestMethod]
    public void AWrittenBondCarriesItsSymbol_AndOneNobodyTypedCarriesNothing() => UiThread.Run(() =>
    {
        const string source = "C=CC1CC1";
        var laid = Build(source);

        var bonds = Pieces(laid, MoleculePiece.Bond).Select(piece => piece.Part is null ? null : Text(source, piece.Part)).ToList();
        Assert.AreEqual(5, bonds.Count);
        Assert.AreEqual(1, bonds.Count(text => text == "="));
        Assert.AreEqual(1, bonds.Count(text => text == "1"), "a ring's closing bond is the digit that closed it");
        Assert.AreEqual(3, bonds.Count(text => text is null));
    });

    [TestMethod]
    public void TheCaptionIsLaidBeneathItsStructure() => UiThread.Run(() =>
    {
        const string source = "c1ccccc1 \"Benzene\"";
        var laid = Build(source);

        var molecule = Pieces(laid, MoleculePiece.Molecule).Single();
        var caption = Pieces(laid, MoleculePiece.Caption).Single();

        Assert.AreEqual("Benzene", Text(source, caption.Part));
        Assert.IsTrue(caption.Bounds.Top >= molecule.Bounds.Bottom, "the caption is under the structure");
    });

    [TestMethod]
    public void EntriesWrapToTheRoomTheyAreGiven() => UiThread.Run(() =>
    {
        const string source = "c1ccccc1\nc1ccccc1\nc1ccccc1";

        var wide = Pieces(Build(source, room: 2000), MoleculePiece.Entry).Select(piece => piece.Bounds.Top).Distinct().Count();
        var narrow = Pieces(Build(source, room: 120), MoleculePiece.Entry).Select(piece => piece.Bounds.Top).Distinct().Count();

        Assert.AreEqual(1, wide, "three benzenes fit on one row of 2000");
        Assert.AreEqual(3, narrow, "and each takes a row of its own in 120");
    });

    [TestMethod]
    public void HeteroatomsAreDrawnInThePalettesElementColours() => UiThread.Run(() =>
    {
        var laid = Build("CO");
        var oxygen = Pieces(laid, MoleculePiece.Atom).Last();

        var ink = oxygen.Marks.ToArray().OfType<TextMark>().First().Foreground;
        Assert.AreEqual(((SolidColorBrush)MarkdownPalette.Light.Elements["O"]).Color, ((SolidColorBrush)ink!).Color);
    });

    [TestMethod]
    public void ABondAtTheBackOfACage_IsBrokenWhereItPassesBehind() => UiThread.Run(() =>
    {
        // A single bond between two carbons is one stroke; broken behind another bond it is two.
        var cage = Pieces(Build("C1C2CC3CC1CC(C2)C3"), MoleculePiece.Bond).Count(bond => bond.Marks.Length > 1);
        var flat = Pieces(Build("C1CCCCC1"), MoleculePiece.Bond).Count(bond => bond.Marks.Length > 1);

        Assert.IsTrue(cage > 0, "adamantane has a bridge at the back");
        Assert.AreEqual(0, flat, "cyclohexane has nothing to pass behind");
    });

    [TestMethod]
    public void AnAtomWithTooManyBonds_IsWavedUnderAndTheReasonSetBeneath() => UiThread.Run(() =>
    {
        const string source = "C(C)(C)(C)(C)C";
        var laid = Build(source);

        var trouble = laid.Trouble.Single();
        Assert.AreEqual(0, trouble.Start);
        Assert.AreEqual(1, trouble.Length, "said of the carbon that has them, not of the block");
        StringAssert.Contains(trouble.Message, "five bonds");

        Assert.AreEqual(1, Pieces(laid, MoleculePiece.Trouble).Count(), "the reason is drawn as well as hovered");
        Assert.AreEqual(6, Pieces(laid, MoleculePiece.Atom).Count(), "and the molecule still draws");
    });

    [TestMethod]
    public void AStringWithNoAtomThatReads_IsShownStruckThrough() => UiThread.Run(() =>
    {
        var laid = Build("[Xx");

        Assert.AreEqual(1, Pieces(laid, MoleculePiece.StandIn).Count());
        Assert.AreEqual(0, Pieces(laid, MoleculePiece.Molecule).Count());
        Assert.IsTrue(laid.Trouble.Count > 0);
    });

    [TestMethod]
    public void ABlockOfNothingButTheKeyword_IsShownAsItsSource() => UiThread.Run(() =>
    {
        Assert.IsTrue(Build("chemistry").ShowsSource);
    });

    private static Laid Build(string source, double room = double.PositiveInfinity) =>
        SmilesBuilder.Build(source, MarkdownPalette.Light, 1.0, room);

    private static IEnumerable<Piece> Pieces(Laid laid, string kind) =>
        laid.Root.SelfAndDescendants().Where(piece => piece.Kind == kind).ToList();

    private static string Text(string source, ISourcePart? part) =>
        part is null ? "" : source.Substring(part.Start, part.Length);
}
