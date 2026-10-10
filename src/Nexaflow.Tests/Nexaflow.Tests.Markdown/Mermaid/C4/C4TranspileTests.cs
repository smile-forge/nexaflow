using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.C4;

/// <summary>
/// What a C4 diagram lets be written into an element's name (<see cref="C4Parser.Rewrite"/>): a name stands inside the
/// brackets of a macro, and every relationship after it reaches the element by that name.
///
/// <para>
/// C4 names its elements in <see cref="SequenceRoles.Id"/>, the role a sequence diagram names participants in. The two
/// do not agree on what a name may hold, which is what the last test here is about: a shared role is not a shared rule,
/// and nothing may come to rely on one meaning the other.
/// </para>
/// </summary>
[TestClass]
[CoversNode("c4")]
public class C4TranspileTests
{
    private const string Diagram = "C4Context\n  Person(customer, \"Banking Customer\")\n  Rel(customer, spa, \"Uses\")\n";

    [TestMethod]
    public void NothingThatWouldCloseTheMacroEarlyGoesIntoAName()
    {
        Assert.IsNull(Rewritten("\""), "a quote would open a value where the name still is");
        Assert.IsNull(Rewritten(")"), "a bracket would close the macro in the middle of the name");
        Assert.IsNull(Rewritten(","), "a comma would make the rest of the name the next argument");
        Assert.IsNull(Rewritten(" "), "and a space is not a letter an identifier is written with");
    }

    [TestMethod]
    public void AndTheLettersAnIdentifierIsWrittenWithDo()
    {
        Assert.AreEqual("x", Written("x"));
        Assert.AreEqual("1", Written("1"));
        Assert.AreEqual("_", Written("_"));
        Assert.AreEqual("-", Written("-"));
    }

    [TestMethod]
    public void TheRoleC4SharesWithASequenceDoesNotMeanTheRuleIsShared()
    {
        var part = Named();
        var quote = new ContentChange([ContentWrite.Words(part, part.End, 0, "\"")], part.End + 1);

        Assert.IsNull(C4Parser.Rewrite(quote), "C4 takes only the letters an identifier is written with");
        Assert.IsNotNull(SequenceParser.Rewrite(quote), "a sequence ends a name on its own few stops, and a quote is not one");
    }

    private static ContentPart Named() =>
        ContentReading.Of(MermaidParser.Parse(Diagram)).Root.SelfAndDescendants()
                      .First(part => part.Kind == Kinds.Words && part.Role == SequenceRoles.Id && part.Text == "customer");

    private static ContentChange? Rewritten(string text)
    {
        var part = Named();
        return C4Parser.Rewrite(new ContentChange([ContentWrite.Words(part, part.End, 0, text)], part.End + text.Length));
    }

    private static string Written(string text)
    {
        var change = Rewritten(text);

        Assert.IsNotNull(change, $"'{text}' was refused where it should have been written");
        return change.Writes.Single().Text;
    }
}
