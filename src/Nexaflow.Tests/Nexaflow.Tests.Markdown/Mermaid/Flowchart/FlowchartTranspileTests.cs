using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a chart lets be written into it (<see cref="FlowchartParser.Rewrite"/>): the reader means words, and the parser says
/// how those words have to be spelled for the chart to read back as the same chart.
///
/// <para>
/// A chart has one rule of its own. What a node is called is what a link, a <c>class</c>, a <c>style</c> and a <c>click</c>
/// line all name it by, and the characters a name may hold are the whole of what tells one name from the next — so a character
/// that would end a name is refused rather than written, because writing it would leave every line that named the node joining
/// something that is not there. Everything else a chart writes, Mermaid spells.
/// </para>
/// </summary>
[TestClass]
[CoversNode("mermaid-flowchart")]
public class FlowchartTranspileTests
{
    private const string Chart = "flowchart LR\n  a[\"Start\"] --> b\n  a@{ label: \"Begun\" }\n";

    [TestMethod]
    public void ACharacterThatWouldEndANameCannotGoIntoOne()
    {
        Assert.IsNull(Rewritten(Named(), "["), "a bracket would end the name and open a label on it");
        Assert.IsNull(Rewritten(Named(), " "), "a space would end it and start the next thing written");
        Assert.IsNull(Rewritten(Named(), ">"), "and an angle bracket could finish a link out of the middle of it");
    }

    [TestMethod]
    public void AndWhatANameCanHoldGoesIn()
    {
        Assert.AreEqual("x", Written(Named(), "x"));
        Assert.AreEqual("1", Written(Named(), "1"));
        Assert.AreEqual("-", Written(Named(), "-"), "a dash is a letter of a name here: my-node is one name");

        // Mermaid reads a subgraph's own name to the end of the line, so it is written in words, spaces and all.
        var opened = ContentReading.Of(FlowchartParser.Parse("flowchart LR\n  subgraph Sales team\n    a\n  end\n"))
                                   .Root.SelfAndDescendants()
                                   .First(part => part.Kind == Kinds.Words && part.Role == FlowchartRoles.Id);

        Assert.AreEqual(" ", Written(opened, " "), "a space is a letter of a subgraph's name");
        Assert.IsNull(Rewritten(Named(), " "), "and is not a letter of a node's");
    }

    [TestMethod]
    public void AQuoteInALabelIsItsEntityCode_WhereverTheLabelIsWritten()
    {
        // Left bare it would close the label and leave the rest of it as syntax. The same label written in brackets and
        // written by an id@{ … } line is the same label, so it is spelled the same way.
        Assert.AreEqual("#quot;", Written(Words("Start"), "\""), "written in the node's brackets");
        Assert.AreEqual("#quot;", Written(Words("Begun"), "\""), "and written by a line about the node");
    }

    [TestMethod]
    public void AChangeWritingNothingMeantIsTheChangeItWas()
    {
        var change = ContentChange.Write(0, 0, "anything");

        Assert.AreSame(change, FlowchartParser.Rewrite(change), "a write nobody asked to be made safe is left alone");
    }

    /// <summary>The run of words <paramref name="said"/> was written as.</summary>
    private static ContentPart Words(string said)
    {
        var reading = ContentReading.Of(FlowchartParser.Parse(Chart));

        return reading.Root.SelfAndDescendants().First(part => part.Kind == Kinds.Words && part.Text == said);
    }

    /// <summary>What the first node is called.</summary>
    private static ContentPart Named()
    {
        var reading = ContentReading.Of(FlowchartParser.Parse(Chart));

        return reading.Root.SelfAndDescendants().First(part => part.Kind == Kinds.Words && part.Role == FlowchartRoles.Id);
    }

    /// <summary>What <paramref name="text"/> is written as into <paramref name="part"/>, or null where it cannot be.</summary>
    private static ContentChange? Rewritten(ContentPart part, string text) =>
        FlowchartParser.Rewrite(new ContentChange([ContentWrite.Words(part, part.End, 0, text)], part.End + text.Length));

    private static string Written(ContentPart part, string text)
    {
        var change = Rewritten(part, text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }
}
