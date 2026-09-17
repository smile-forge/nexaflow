using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Ishikawa;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Ishikawa;

/// <summary>What an <c>ishikawa</c> block is read into: every line, the header's too, as what it says to its end.</summary>
[TestClass]
[CoversNode("ishikawa-ast")]
public class IshikawaGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the Mermaid documentation opens with.</summary>
    public const string BlurryPhoto =
        """
        ishikawa-beta
            Blurry Photo
            Process
                Out of focus
                Shutter speed too slow
                Protective film not removed
                Beautification filter applied
            User
                Shaky hands
            Equipment
                LENS
                    Inappropriate lens
                    Damaged lens
                    Dirty lens
                SENSOR
                    Damaged sensor
                    Dirty sensor
            Environment
                Subject moved too quickly
                Too dark
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Ishikawa;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        BlurryPhoto,
        "ishikawa-beta\n    Blurry Photo\n        Process\n            Out of focus\n        User\n            Shaky hands\n",
        "ishikawa-beta\nProblem\nCause A\n  Subcause A1\nCause B\n",
        "ishikawa-beta\n    Problem\nCause A\n  Subcause A1\nCause B\n",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the short keyword", "ishikawa\n  Problem\n  Cause"),
        ("the event on the header line", "ishikawa-beta Problem\n  Cause A\n    Subcause"),
        ("a comment and a blank line", "ishikawa\n  Problem\n\n  %% the causes\n  Cause %% not a comment here"),
        ("tabs", "ishikawa\n\tProblem\n\tCause\n\t\tSubcause"),
        ("what looks like syntax", "ishikawa\n  Problem: \"quoted\" [x] {y} --> z\n  title Cause"),
        ("accessibility lines", "ishikawa\n  accTitle: Photos\n  Problem\n  Cause"),
        ("written on Windows", "ishikawa\r\n  Problem  \r\n  Cause\r\n"),
        // Half written.
        ("nothing but the keyword", "ishikawa-beta"),
        ("only the event", "ishikawa-beta\n  Problem"),
    ];

    [TestMethod]
    public void EveryLineOfTheDocumentedDiagramIsACause()
    {
        var causes = Causes(BlurryPhoto);

        Assert.AreEqual(19, causes.Count);
        Assert.AreEqual("Blurry Photo", causes[0]);
        Assert.AreEqual("Too dark", causes[^1]);
    }

    [TestMethod]
    public void ALineIsWhatItSaysToItsEnd_AComplaintAboutNothing()
    {
        const string source = "ishikawa-beta Problem  \n  Cause: \"a\" %% b  ";

        CollectionAssert.AreEqual(new[] { "Problem", "Cause: \"a\" %% b" }, Causes(source));
        Assert.IsFalse(MermaidParser.Read(source).SelfAndDescendants().Any(node => node.Trouble is not null));
    }

    private static List<string> Causes(string source) =>
    [
        .. ContentReading.Of(MermaidParser.Read(source)).Root.SelfAndDescendants()
            .Where(part => part.Kind == IshikawaKinds.Cause)
            .Select(part => part.Words()!.Text),
    ];
}
