using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Sequence;

/// <summary>
/// What a sequence diagram lets be written into a participant's name (<see cref="SequenceParser.Rewrite"/>): a
/// participant is named once and reached by that name from every message and note after it.
/// </summary>
[TestClass]
[CoversNode("sequence-diagram")]
public class SequenceTranspileTests
{
    private const string Diagram = "sequenceDiagram\n  participant Alice\n  Alice->>John: Hello\n";

    [TestMethod]
    public void ACharacterThatWouldEndANameCannotGoIntoOne()
    {
        Assert.IsNull(Rewritten(":"), "a colon would start the message instead");
        Assert.IsNull(Rewritten(","), "a comma would make it the first of two names");
        Assert.IsNull(Rewritten(">"), "and an angle bracket could finish an arrow out of the middle of it");
    }

    [TestMethod]
    public void AndWhatANameCanHoldGoesIn()
    {
        Assert.AreEqual("x", Written("x"));
        Assert.AreEqual("-", Written("-"), "a dash is a letter of a name here, where a state diagram ends one on it");
    }

    private static ContentPart Named() =>
        ContentReading.Of(MermaidParser.Parse(Diagram)).Root.SelfAndDescendants()
                      .First(part => part.Kind == Kinds.Words && part.Role == SequenceRoles.Id && part.Text == "Alice");

    private static ContentChange? Rewritten(string text)
    {
        var part = Named();
        return SequenceParser.Rewrite(new ContentChange([ContentWrite.Words(part, part.End, 0, text)], part.End + text.Length));
    }

    private static string Written(string text)
    {
        var change = Rewritten(text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }
}
