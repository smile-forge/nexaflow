using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Requirement;
using Nexaflow.Markdown.Mermaid.Requirement.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Requirement;

/// What <see cref="RequirementParser"/> reads a <c>requirementDiagram</c> block into: the requirements written in it, the elements that
/// meet them, the fields inside each, what holds between them, and the lines that lay it out and style it. The header is optional,
/// since a <c>requirementDiagram</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("requirement-diagram")]
public class RequirementParserTests : ContentParserContract
{
    /// <summary>The diagram the documentation opens with, one requirement of every kind.</summary>
    public const string Intro =
        """
        requirementDiagram

        requirement test_req {
        id: 1
        text: the test text.
        risk: high
        verifymethod: test
        }

        functionalRequirement test_req2 {
        id: 1.1
        text: the second test text.
        risk: low
        verifymethod: inspection
        }

        performanceRequirement test_req3 {
        id: 1.2
        text: the third test text.
        risk: medium
        verifymethod: demonstration
        }

        interfaceRequirement test_req4 {
        id: 1.2.1
        text: the fourth test text.
        risk: medium
        verifymethod: analysis
        }

        physicalRequirement test_req5 {
        id: 1.2.2
        text: the fifth test text.
        risk: medium
        verifymethod: analysis
        }

        designConstraint test_req6 {
        id: 1.2.3
        text: the sixth test text.
        risk: medium
        verifymethod: analysis
        }

        element test_entity {
        type: simulation
        }

        element test_entity2 {
        type: word doc
        docRef: reqs/test_entity
        }

        element test_entity3 {
        type: "test suite"
        docRef: github.com/all_the_tests
        }

        test_entity - satisfies -> test_req2
        test_req - traces -> test_req2
        test_req - contains -> test_req3
        test_req3 - contains -> test_req4
        test_req4 - derives -> test_req5
        test_req5 - refines -> test_req6
        test_entity3 - verifies -> test_req5
        test_req <- copies - test_entity2
        """;

    /// <summary>One relation of every kind there is, and one written the other way round.</summary>
    public const string Related =
        """
        requirementDiagram
        a - contains -> b
        a - copies -> c
        a - derives -> d
        a - satisfies -> e
        a - verifies -> f
        a - refines -> g
        a - traces -> h
        i <- contains - a
        """;

    /// <summary>The documentation's styled diagram.</summary>
    public const string Styled =
        """
        requirementDiagram
            requirement "test_req" {
                id: 1
                text: the test text.
                risk: high
                verifymethod: test
            }
            element test_entity {
                type: simulation
            }
            test_entity - satisfies -> "test_req"
            style test_req fill:#ffa,stroke:#f66,stroke-width:2px,color:#000
            classDef important font-weight:bold
            class test_entity important
        """;

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => RequirementParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);


    protected override IEnumerable<string> DocumentedBlocks =>
    [
        "requirementDiagram\nelement customer portal {\n  type: web\n}\nrequirement r1 {\n  id: 1\n}\ncustomer portal - satisfies -> r1",
        "requirementDiagram\nrequirement r1:::important,urgent {\n  id: 1\n}\nclassDef important fill:#f96\nclassDef urgent stroke:#f00",
        "requirementDiagram\n# what the portal must do\nrequirement r {\n  text: \"line one\nline two\"\n}",
        "requirementDiagram\nrequirement r { id: 1 }",
        Intro,
        Related,
        Styled,
        "---\ntitle: The requirements\n---\n" + Related,
        "requirementDiagram\n    direction LR\n    a - satisfies -> b",
        "requirementDiagram\n    requirement \"**Bold** requirement\" {\n        id: 1\n"
        + "        text: \"the *test* text.\"\n        risk: high\n        verifymethod: test\n    }",
        "requirementDiagram\n    requirement A {\n        id: 1\n    }\n    A:::urgent\n    classDef urgent fill:#f96",
        "requirementDiagram\n    accTitle: What we need\n    accDescr: The requirements and what meets them\n"
        + "    a - satisfies -> b",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own", Intro),
        ("one relation of each kind", Related),
        ("the documentation's styling", Styled),

        ("a requirement with nothing in it", "requirementDiagram\n  requirement A {\n  }"),
        ("an element", "requirementDiagram\n  element E {\n    type: simulation\n    docref: reqs/e\n  }"),
        ("a name in quotes", "requirementDiagram\n  requirement \"two words\" {\n    id: 1\n  }\n  \"two words\" - traces -> b"),
        ("a name given a class", "requirementDiagram\n  requirement A:::blue {\n    id: 1\n  }\n  classDef blue fill:#00f"),
        ("a value in quotes", "requirementDiagram\n  requirement A {\n    text: \"what it: asks for\"\n  }"),
        ("a value with a comment after it", "requirementDiagram\n  requirement A {\n    id: 1 %% and on\n  }"),
        ("a field with nothing after the colon", "requirementDiagram\n  requirement A {\n    id: \n  }"),
        ("a line with nothing on it inside one", "requirementDiagram\n  requirement A {\n    id: 1\n\n  }"),
        ("a relation the other way round", "requirementDiagram\n  b <- satisfies - a"),
        ("a relation with no space in it", "requirementDiagram\n  a-satisfies->b"),
        ("a relation closed with a semicolon", "requirementDiagram\n  a - satisfies -> b;"),
        ("a comment closing a line", "requirementDiagram\n  a - satisfies -> b %% and on"),
        ("the way it is laid out", "requirementDiagram\n  direction LR\n  a - satisfies -> b"),
        ("a requirement styled on its own", "requirementDiagram\n  requirement A {\n    id: 1\n  }\n  style A fill:#f9f"),
        ("several taking one class",
         "requirementDiagram\n  a - traces -> b\n  classDef blue fill:#00f\n  class a,b blue"),
        ("the front matter's own sizes",
         "---\nconfig:\n  requirement:\n    rect_min_width: 140\n    rect_padding: 6\n---\n"
         + "requirementDiagram\n  requirement A {\n    id: 1\n  }"),

        ("a requirement never closed", "requirementDiagram\n  requirement A {\n    id: 1"),
        ("a requirement with no brace", "requirementDiagram\n  requirement A"),
        ("a requirement with no name", "requirementDiagram\n  requirement "),
        ("a brace closing nothing", "requirementDiagram\n  a - traces -> b\n  }"),
        ("a name never closed", "requirementDiagram\n  requirement \"A {\n    id: 1\n  }"),
        ("a field nobody knows", "requirementDiagram\n  requirement A {\n    colour: red\n  }"),
        ("a field with no colon", "requirementDiagram\n  requirement A {\n    id 1\n  }"),
        ("a risk nobody runs", "requirementDiagram\n  requirement A {\n    risk: enormous\n  }"),
        ("a verification nobody makes", "requirementDiagram\n  requirement A {\n    verifymethod: hoping\n  }"),
        ("a relation nobody means", "requirementDiagram\n  a - swallows -> b"),
        ("a relation with nothing the far side", "requirementDiagram\n  a - satisfies -> "),
        ("a relation with nothing on it", "requirementDiagram\n  a - -> b"),
        ("a relation with nothing either side", "requirementDiagram\n  - satisfies ->"),
        ("a new relation, as writing one starts it", "requirementDiagram\n  \"\" - satisfies -> \"\""),
        ("a way nobody lays it out", "requirementDiagram\n  direction SIDEWAYS"),
        ("a requirement nothing writes", "requirementDiagram\n  a - traces -> b\n  style nowhere fill:#f00"),
        ("a class no classDef declares", "requirementDiagram\n  a - traces -> b\n  class a missing"),
        ("something after the keyword", "requirementDiagram LR\n  a - traces -> b"),
        ("nothing anybody means to write", "requirementDiagram\n  ??? !!!"),
        ("nothing at all", "requirementDiagram"),
        // A requirement diagram fence, and a string Mermaid's lexer reads across a line ending.
        ("no header, as a requirementDiagram fence holds it", "requirement first {\n  id: 1\n}\nelement car {\n  type: thing\n}\ncar - satisfies -> first"),
        ("a name in quotes across two lines", "requirementDiagram\n  element \"the\ncar\" {\n    type: thing\n  }\n  first - satisfies -> second"),
        ("a binding supplying lines", "requirementDiagram\n  a - satisfies -> b\n  {{More}}\n"),
    ];

    /// <summary>
    /// The stages a requirement diagram's language offers the engine (<c>Shipped.Requirement</c>), named again here — what the engine
    /// adds for every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolveStyles(),
        new WithConfig<RequirementConfig>(RequirementConfig.Read(MermaidBlock.Of(tree).Config)),
        .. holes ? [new WithHoles(RequirementParser.Holds)] : Array.Empty<IAstStage>(),
        new WithFolds(),
    ];
    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = RequirementParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }
    [TestMethod]
    public void ANameInQuotesRunsToTheQuoteClosingIt_AndTheFieldsUnderItAreStillItsOwn()
    {
        var tree = Staged("requirementDiagram\n  element \"the\ncar\" {\n    type: thing\n  }\n  first - satisfies -> second");

        Assert.AreEqual(1, tree.SelfAndDescendants().Count(node => node.Kind == RequirementKinds.Block), "the element opens its fields though its name took two rows");
        Assert.AreEqual(1, tree.SelfAndDescendants().Count(node => node.Kind == RequirementKinds.Relation), "and the line after the brace closing them is read");
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceARequirementDiagramFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("requirement first {\n  id: 1\n}\nelement car {\n  type: thing\n}\ncar - satisfies -> first");

        Assert.AreEqual(RequirementParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, tree.SelfAndDescendants().Count(node => node.Kind == RequirementKinds.Block), "the first line opens a requirement, not a header gone wrong");
        Assert.AreEqual(1, tree.SelfAndDescendants().Count(node => node.Kind == RequirementKinds.Relation));
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }
}
