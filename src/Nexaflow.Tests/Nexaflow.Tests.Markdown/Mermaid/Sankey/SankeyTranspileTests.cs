using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Sankey;

/// <summary>
/// What a sankey diagram lets be written into it (<see cref="SankeyParser.Rewrite"/>): the reader means words, and the
/// parser says how those words have to be spelled for the diagram to read back as the same diagram.
///
/// <para>
/// A sankey writes a quote in a name by writing it twice, where the rest of Mermaid writes the entity code — so the
/// shared spelling is wrong here, and this is where a sankey parts company with every other diagram. A name written
/// bare is refused a comma or a quote outright, because putting the field in quotes is a change to more than the name.
/// </para>
/// </summary>
[TestClass]
[CoversNode("sankey-writing")]
public class SankeyTranspileTests
{
    private const string Diagram = "sankey-beta\nWind,Grid,42\n\"Grid\",Homes,30\n";

    [TestMethod]
    public void AQuoteInANameInQuotesIsWrittenTwice()
    {
        Assert.AreEqual("\"\"", Written(InQuotes(), "\""),
                        "a sankey says one quote by writing two, and never by the entity code the rest of Mermaid uses");
    }

    [TestMethod]
    public void ACharacterThatWouldEndABareNameCannotGoIntoOne()
    {
        Assert.IsNull(Rewritten(Bare(), ","), "a comma would end the field and start the next one");
        Assert.IsNull(Rewritten(Bare(), "\""), "and a quote in a bare field is the field never closed");
    }

    [TestMethod]
    public void AndWhatABareNameCanHoldGoesIn()
    {
        Assert.AreEqual("x", Written(Bare(), "x"));
        Assert.AreEqual(" ", Written(Bare(), " "), "a sankey name is words, so a space is a letter of one");
        Assert.AreEqual("-", Written(Bare(), "-"));
    }

    [TestMethod]
    public void AndTheDiagramIsGivenThatRuleRatherThanTheSharedOne()
    {
        // What the shipped language writes a sankey through comes from here. A rule the parser has and the diagram is
        // never handed is a rule nothing ever applies.
        var part = Bare();
        var change = new ContentChange([ContentWrite.Words(part, part.End, 0, ",")], part.End + 1);

        Assert.IsNull(MermaidDiagrams.TranspilerFor(MermaidDiagram.Sankey)(change), "a sankey refuses the comma");
        Assert.IsNotNull(MermaidDiagrams.TranspilerFor(MermaidDiagram.Pie)(change), "and one with no rule of its own writes it");
    }

    [TestMethod]
    public void AChangeWritingNothingMeantIsTheChangeItWas()
    {
        var change = ContentChange.Write(0, 0, "anything");

        Assert.AreSame(change, SankeyParser.Rewrite(change), "a write nobody asked to be made safe is left alone");
    }

    /// <summary>A name written as it is, with no quotes round it.</summary>
    private static ContentPart Bare() => WordsOf(SankeyRoles.Source, "Wind");

    /// <summary>The same node's name, written in quotes on the next row.</summary>
    private static ContentPart InQuotes() => WordsOf(SankeyRoles.Source, "Grid");

    /// <summary>The run of words written as <paramref name="said"/> in the role <paramref name="role"/>.</summary>
    private static ContentPart WordsOf(string role, string said) =>
        ContentReading.Of(MermaidParser.Parse(Diagram)).Root.SelfAndDescendants()
                      .First(part => part.Kind == Kinds.Words && part.Role == role && part.Text == said);

    /// <summary>What <paramref name="text"/> is written as into <paramref name="part"/>, or null where it cannot be.</summary>
    private static ContentChange? Rewritten(ContentPart part, string text) =>
        SankeyParser.Rewrite(new ContentChange([ContentWrite.Words(part, part.End, 0, text)], part.End + text.Length));

    private static string Written(ContentPart part, string text)
    {
        var change = Rewritten(part, text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }
}
