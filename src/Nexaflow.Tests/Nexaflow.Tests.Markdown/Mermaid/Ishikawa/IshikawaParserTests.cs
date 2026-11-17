using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Ishikawa;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Ishikawa;

/// <summary>
/// What <see cref="IshikawaParser"/> reads an <c>ishikawa</c> block into: every line, the header's too, as what it says to its end.
/// The header is optional, since an <c>ishikawa</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("ishikawa-ast")]
public class IshikawaParserTests : ContentParserContract
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

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => IshikawaParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree);

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
        ("a quote nothing closes", "ishikawa\n  Problem \"one\n  Cause\" two"),
        ("accessibility lines", "ishikawa\n  accTitle: Photos\n  Problem\n  Cause"),
        ("written on Windows", "ishikawa\r\n  Problem  \r\n  Cause\r\n"),
        ("no header, as an ishikawa fence holds it", "Problem\n  Cause\n    Subcause"),
        ("a binding supplying causes", "ishikawa\n  Problem\n  {{Causes}}\n"),
        // Half written.
        ("nothing but the keyword", "ishikawa-beta"),
        ("only the event", "ishikawa-beta\n  Problem"),
    ];

    /// <summary>
    /// The stages an Ishikawa diagram's language offers the engine (<c>Shipped.Ishikawa</c>), named again here — what the engine adds
    /// for every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree) =>
        [new WithConfig<IshikawaConfig>(IshikawaConfig.Read(MermaidBlock.Of(tree).Config))];

    /// <summary>A block parsed and worked over by its stages.</summary>
    internal static ContentNode Staged(string source)
    {
        var tree = IshikawaParser.Parse(source);
        return new AstPipeline(Offered(tree)).Run(tree);
    }

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
        Assert.IsFalse(Staged(source).SelfAndDescendants().Any(node => node.Trouble is not null));
    }

    [TestMethod]
    public void AQuoteRunsNoFurtherThanItsLine()
    {
        // A quote means nothing in an Ishikawa diagram, so it never carries a line on past its end.
        CollectionAssert.AreEqual(new[] { "Problem \"one", "Cause\" two" }, Causes("ishikawa\n  Problem \"one\n  Cause\" two"));
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceAnIshikawaFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("Problem\n  Cause\n    Subcause");

        Assert.AreEqual(IshikawaParser.Language, ((BlockNode)tree).Language);
        CollectionAssert.AreEqual(new[] { "Problem", "Cause", "Subcause" }, Causes("Problem\n  Cause\n    Subcause"), "the first line is the event, not a header gone wrong");
    }

    [TestMethod]
    public void ATitleLineIsACauseLikeAnyOther() =>
        CollectionAssert.AreEqual(new[] { "Problem", "title Cause" }, Causes("ishikawa\n  Problem\n  title Cause"));

    private static List<string> Causes(string source) =>
    [
        .. ContentReading.Of(Staged(source)).Root.SelfAndDescendants()
            .Where(part => part.Kind == IshikawaKinds.Cause)
            .Select(part => part.Words()!.Text),
    ];
}
