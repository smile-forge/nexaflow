using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.State;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.State;

/// <summary>
/// What a state diagram lets be written into it (<see cref="StateParser.Rewrite"/>): the reader means words, and the
/// parser says how those words have to be spelled for the diagram to read back as the same diagram.
///
/// <para>
/// A state is named by its id, and a transition, a <c>class</c>, a <c>style</c> and a <c>note</c> line all reach it by
/// that name, so a character that would end a name is refused rather than written. A dash ends one here, which is where
/// a state parts company with a chart: <c>my-state</c> is one name in a chart and two things in a state diagram.
/// Everything else a state diagram writes, Mermaid spells.
/// </para>
/// </summary>
[TestClass]
[CoversNode("state-diagram")]
public class StateTranspileTests
{
    private const string Diagram =
        "stateDiagram-v2\n  [*] --> Still\n  Still --> Moving\n  Still : Sitting about\n  class Still busy\n";

    [TestMethod]
    public void ACharacterThatWouldEndANameCannotGoIntoOne()
    {
        Assert.IsNull(Rewritten(Named(), " "), "a space would end the name and start the next thing written");
        Assert.IsNull(Rewritten(Named(), ":"), "a colon would start what is written on the state instead");
        Assert.IsNull(Rewritten(Named(), ">"), "an angle bracket could finish a transition out of the middle of it");
        Assert.IsNull(Rewritten(Named(), "-"), "and a dash ends a name here, where a chart holds it as a letter of one");
    }

    [TestMethod]
    public void AndWhatANameCanHoldGoesIn()
    {
        Assert.AreEqual("x", Written(Named(), "x"));
        Assert.AreEqual("1", Written(Named(), "1"));
        Assert.AreEqual("_", Written(Named(), "_"));
    }

    [TestMethod]
    public void AClassIsNamedByTheSameRuleAStateIs()
    {
        // A class line reaches a state by its name and gives it a class by another; both are written the one way.
        Assert.IsNull(Rewritten(Classed(), " "), "a space would end the class name");
        Assert.AreEqual("x", Written(Classed(), "x"));
    }

    [TestMethod]
    public void AndTheDiagramIsGivenThatRuleRatherThanTheSharedOne()
    {
        // What the shipped language writes a state diagram through comes from here. A rule the parser has and the diagram
        // is never handed is a rule nothing ever applies.
        var part = Named();
        var change = new ContentChange([ContentWrite.Words(part, part.End, 0, " ")], part.End + 1);

        Assert.IsNull(MermaidDiagrams.TranspilerFor(MermaidDiagram.State)(change), "a state diagram refuses the space");
        Assert.IsNotNull(MermaidDiagrams.TranspilerFor(MermaidDiagram.Pie)(change), "and one with no rule of its own writes it");
    }

    [TestMethod]
    public void AChangeWritingNothingMeantIsTheChangeItWas()
    {
        var change = ContentChange.Write(0, 0, "anything");

        Assert.AreSame(change, StateParser.Rewrite(change), "a write nobody asked to be made safe is left alone");
    }

    /// <summary>What the state written twice is called.</summary>
    private static ContentPart Named() => WordsOf(StateRoles.Id, "Still");

    /// <summary>The class a class line gives it.</summary>
    private static ContentPart Classed() => WordsOf(StateRoles.Class, "busy");

    /// <summary>The run of words written as <paramref name="said"/> in the role <paramref name="role"/>.</summary>
    private static ContentPart WordsOf(string role, string said) =>
        ContentReading.Of(MermaidParser.Parse(Diagram)).Root.SelfAndDescendants()
                      .First(part => part.Kind == Kinds.Words && part.Role == role && part.Text == said);

    /// <summary>What <paramref name="text"/> is written as into <paramref name="part"/>, or null where it cannot be.</summary>
    private static ContentChange? Rewritten(ContentPart part, string text) =>
        StateParser.Rewrite(new ContentChange([ContentWrite.Words(part, part.End, 0, text)], part.End + text.Length));

    private static string Written(ContentPart part, string text)
    {
        var change = Rewritten(part, text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }
}
