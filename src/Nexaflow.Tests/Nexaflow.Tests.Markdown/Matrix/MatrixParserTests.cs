using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Matrix;

/// <summary>
/// The tree a 2D-code block is read into: what was written prints back as it was written, whatever it was, and a
/// field is its key, its colon and its value.
///
/// <para>
/// The blocks include what nobody means to write — prose, a colon with no key, a carriage return on its own —
/// because a block is read on every keystroke, and most of what it is handed is half-written.
/// </para>
/// </summary>
[TestClass]
[CoversNode("matrix-block-ast")]
public class MatrixParserTests
{
    private static readonly (string What, string Source)[] Blocks =
    [
        ("a url", "type: url\nurl: https://markdown.org/tools/diagrams/qr/"),
        ("windows line endings", "type: text\r\ntext: hello\r\n"),
        ("comments and blank lines", "# a guest network\n\ntype: wifi\n  ssid: Guest  \n\n# the end"),
        ("a colon in the value", "type: text\ntext: a: b: c"),
        ("space round the colon", "type   :   text\ntext:"),
        ("tabs", "\ttype:\ttext\t\n"),
        ("prose", "just some prose"),
        ("a colon with no key", ": value"),
        ("a carriage return on its own", "type: text\rtext: x"),
        ("nothing at all", ""),
        ("only space", "  \n \n"),
    ];

    [TestMethod]
    public void EveryBlockReadsBackAsItWasWritten()
    {
        foreach (var (what, source) in Blocks)
            Assert.AreEqual(source, MatrixParser.Parse(source).Print(), what);
    }

    [TestMethod]
    public void EveryPrefixOfEveryBlockReadsBackToo()
    {
        foreach (var (what, source) in Blocks)
            for (var length = 0; length <= source.Length; length++)
            {
                var typed = source[..length];
                Assert.AreEqual(typed, MatrixParser.Parse(typed).Print(), $"{what}: after {length} character(s)");
            }
    }

    [TestMethod]
    public void TheParserOnlyEverCopies()
    {
        foreach (var (what, source) in Blocks)
            foreach (var place in MatrixParser.Parse(source).Placed())
            {
                if (!place.Node.IsLeaf) continue;

                Assert.IsTrue(place.End <= source.Length,
                    $"{what}: {place.Node.Kind} claims {place.Start}+{place.Node.Width} of {source.Length}");

                Assert.AreEqual(source.Substring(place.Start, place.Node.Width), place.Node.Text,
                    $"{what}: {place.Node.Kind} at {place.Start} is not what the source says");
            }
    }

    [TestMethod]
    public void EveryLineIsALineOfTheBlock()
    {
        var tree = MatrixParser.Parse("type: text\n\n# a note\nprose");

        Assert.AreEqual(MatrixKinds.Block, tree.Kind);
        Assert.AreEqual(4, tree.Children.Count);
        Assert.IsTrue(tree.Children.All(line => line.Kind == MatrixKinds.Line));
    }

    [TestMethod]
    public void AFieldIsItsKeyItsColonAndItsValue()
    {
        // The space either side belongs to the line, so the key and the value are only what they say.
        var field = Fields("  url :  https://markdown.org  ").Single();

        Assert.AreEqual("url", field.Part(Roles.Name)?.Text);
        Assert.AreEqual(":", field.Part(Roles.Separator)?.Text);
        Assert.AreEqual("https://markdown.org", field.Part(MatrixRoles.Value)?.Text);
    }

    [TestMethod]
    public void TheFirstColonEndsTheKey()
    {
        // Which is what lets a URL, or a time of day, sit in a value without quoting.
        var field = Fields("url: https://markdown.org:8080/a").Single();

        Assert.AreEqual("url", field.Part(Roles.Name)?.Text);
        Assert.AreEqual("https://markdown.org:8080/a", field.Part(MatrixRoles.Value)?.Text);
    }

    [TestMethod]
    public void AFieldWithNothingAfterItsColonHasNoValue()
    {
        var field = Fields("text:   ").Single();

        Assert.AreEqual("text", field.Part(Roles.Name)?.Text);
        Assert.IsNull(field.Part(MatrixRoles.Value));
    }

    [TestMethod]
    public void ALineThatIsNotAFieldIsHeldWithTheReason()
    {
        var tree = MatrixParser.Parse("type: text\njust some prose\ntext: x");

        var held = tree.SelfAndDescendants().Single(node => node.Trouble is not null);
        Assert.AreEqual(Kinds.Verbatim, held.Kind);
        Assert.AreEqual("just some prose", held.Text);
        StringAssert.Contains(held.Trouble, "not a `key: value` line");

        Assert.AreEqual(2, Fields(tree).Count(), "and the lines either side of it still read");
    }

    [TestMethod]
    public void AColonWithNoKeyIsNotAField()
    {
        var tree = MatrixParser.Parse(": value");

        Assert.AreEqual(0, Fields(tree).Count());
        Assert.IsNotNull(tree.SelfAndDescendants().Single(node => node.Trouble is not null));
    }

    [TestMethod]
    public void CommentsAndBlankLinesSayNothing()
    {
        var tree = MatrixParser.Parse("# type: text\n\n   \n  # text: x");

        Assert.AreEqual(0, Fields(tree).Count(), "a comment that looks like a field is still a comment");
        Assert.IsFalse(tree.SelfAndDescendants().Any(node => node.Trouble is not null));
    }

    private static IEnumerable<ContentNode> Fields(string source) => Fields(MatrixParser.Parse(source));

    private static IEnumerable<ContentNode> Fields(ContentNode tree) =>
        tree.SelfAndDescendants().Where(node => node.Kind == MatrixKinds.Field);
}
