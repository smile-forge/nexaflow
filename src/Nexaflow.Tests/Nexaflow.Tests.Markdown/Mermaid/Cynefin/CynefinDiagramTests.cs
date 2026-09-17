using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Cynefin;

/// <summary>
/// A <c>cynefin-beta</c> block read into what it describes: which domain each item sits in, where each domain is opened, the
/// movements between them, and what the front matter asks for.
/// </summary>
[TestClass]
[CoversNode("cynefin-ast")]
public class CynefinDiagramTests
{
    [TestMethod]
    public void EachItemSitsInTheDomainOpenedAboveIt()
    {
        var diagram = CynefinDiagram.Read(CynefinGrammarTests.Sense);

        Assert.AreEqual(10, diagram.Items.Count);
        Assert.AreEqual(2, diagram.ItemsIn(CynefinDomain.Complex).Count);
        Assert.AreEqual(4, diagram.ItemsIn(CynefinDomain.Confusion).Count);
        Assert.AreEqual("Investigate root cause", diagram.ItemsIn(CynefinDomain.Complex)[0].Says.Says.Text);
        Assert.AreEqual("Stop the bleeding", diagram.ItemsIn(CynefinDomain.Chaotic).Single().Says.Says.Text);
    }

    [TestMethod]
    public void ADomainOpenedTwiceIsOneDomain_ItsItemsInTheOrderWritten()
    {
        var diagram = CynefinDiagram.Read("cynefin-beta\n  complex\n    \"One\"\n  clear\n    \"Two\"\n  complex\n    \"Three\"");

        Assert.AreSequenceEqual(new[] { "One", "Three" }, diagram.ItemsIn(CynefinDomain.Complex).Select(item => item.Says.Says.Text).ToArray());
        Assert.AreEqual("Two", diagram.ItemsIn(CynefinDomain.Clear).Single().Says.Says.Text);
    }

    [TestMethod]
    public void ADomainIsOpenedWhereItsWordIsWritten()
    {
        const string source = "cynefin-beta\n  complex\n    \"One\"";
        var diagram = CynefinDiagram.Read(source);

        var opened = diagram.Opened(CynefinDomain.Complex)!;

        Assert.AreEqual("complex", source.Substring(opened.Word.Start, opened.Word.Length));
        Assert.AreEqual("  complex\n", source.Substring(opened.Part.Parent!.Start, opened.Part.Parent!.Length), "the line it is opened on");
        Assert.IsNull(diagram.Opened(CynefinDomain.Chaotic), "a domain nothing opens");
    }

    [TestMethod]
    public void AMovementReadsItsEndsAndWhatItSays()
    {
        var move = CynefinDiagram.Read("cynefin-beta\n  chaotic --> complex : \"Stabilised\"").Moves.Single();

        Assert.AreEqual(CynefinDomain.Chaotic, move.From);
        Assert.AreEqual(CynefinDomain.Complex, move.To);
        Assert.AreEqual("Stabilised", move.Label!.Says.Text);
    }

    [TestMethod]
    public void AMovementStillToSayWhereItGoesGoesNowhere()
    {
        var move = CynefinDiagram.Read("cynefin-beta\n  chaotic --> ").Moves.Single();

        Assert.AreEqual(CynefinDomain.Chaotic, move.From);
        Assert.IsNull(move.To);
        Assert.IsNull(move.Label);
    }

    [TestMethod]
    public void AnItemInNoDomainIsNowhereToDraw()
    {
        var diagram = CynefinDiagram.Read("cynefin-beta\n  \"Stranded\"\n  complex\n    \"Placed\"");

        Assert.AreEqual("Placed", diagram.Items.Single().Says.Says.Text);
    }

    [TestMethod]
    public void TheFrontMattersOptionsAndDomainColoursAreRead()
    {
        var config = CynefinDiagram.Read(
            "---\nconfig:\n  cynefin:\n    width: 600\n    height: 400\n    padding: 4\n    showDomainDescriptions: true\n"
            + "  themeVariables:\n    cynefin:\n      complexBg: \"#4e79a7\"\n      boundaryColor: \"#888888\"\n---\ncynefin-beta\n  complex").Config;

        Assert.AreEqual(600, config.Width);
        Assert.AreEqual(400, config.Height);
        Assert.AreEqual(4, config.Padding);
        Assert.IsTrue(config.ShowDomainDescriptions);
        Assert.AreEqual("#4e79a7", config.DomainFills[(int)CynefinDomain.Complex]);
        Assert.AreEqual("#888888", config.BoundaryColour);
    }

    [TestMethod]
    public void ADomainColourNamingNoDiagramIsStillTheDomains()
    {
        var config = CynefinDiagram.Read("---\nconfig:\n  themeVariables:\n    clearBg: \"#59a14f\"\n---\ncynefin-beta\n  clear").Config;

        Assert.AreEqual("#59a14f", config.DomainFills[(int)CynefinDomain.Clear]);
    }

    [TestMethod]
    public void ABlockWithNothingWrittenInItHasNothingToDraw()
    {
        Assert.IsTrue(CynefinDiagram.Read("cynefin-beta").Empty);
        Assert.IsFalse(CynefinDiagram.Read("cynefin-beta\n  clear").Empty);
    }

    [TestMethod]
    public void EveryDomainSaysHowItIsWorked() =>
        Assert.AreEqual("probe · sense · respond", CynefinDiagram.Practice(CynefinDomain.Complex));
}
