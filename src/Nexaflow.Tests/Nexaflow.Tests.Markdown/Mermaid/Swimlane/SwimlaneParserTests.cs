using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Markdown.Mermaid.Flowchart.Stages;
using Nexaflow.Markdown.Mermaid.Swimlane;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Swimlane;

/// <summary>
/// What <see cref="SwimlaneParser"/> reads a <c>swimlane-beta</c> block into, which is what a flowchart block is read into: a swimlane
/// is a flowchart whose subgraphs written outside them all are lanes, and Mermaid reads the two alike. These hold that the
/// flowchart's pieces read everything a swimlane writes, that the header is optional, since a <c>swimlane</c> fence already says what
/// the block is, and that what the lanes ask for in the front matter is read.
/// </summary>
[TestClass]
[CoversNode("swimlanes")]
public class SwimlaneParserTests : ContentParserContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    public const string Intro =
        """
        swimlane-beta LR
          subgraph Customer
            Browse[Browse catalogue]
            Pay[Pay]
          end
          subgraph Warehouse
            Pick[Pick items]
            Ship[Ship order]
          end
          subgraph Finance
            Invoice[Raise invoice]
          end
          Browse --> Pay
          Pay --> Pick
          Pick --> Ship
          Pay --> Invoice
        """;

    /// <summary>The documentation's support escalation, whose handoffs are labelled and whose lanes are three deep.</summary>
    public const string Support =
        """
        swimlane-beta LR
          subgraph Customer
            request[Request service]
            receive[Receive update]
          end

          subgraph Support
            triage[Triage request]
            answer[Send answer]
          end

          subgraph Engineering
            investigate[Investigate issue]
            fix[Prepare fix]
          end

          request --> triage
          triage -->|Known issue| answer
          triage -->|Needs code change| investigate
          investigate --> fix --> answer
          answer --> receive
        """;

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => SwimlaneParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Support,
        "---\nconfig:\n  theme: default\n  look: classic\n---\n" + Intro,
        "swimlane-beta",
        "swimlane-beta LR",
        "swimlane-beta\n  subgraph Sales\n    lead[Qualify lead]\n    quote[Prepare quote]\n  end",
        "swimlane-beta LR\n  subgraph sales [Sales team]\n    lead[Qualify lead]\n    quote[Prepare quote]\n  end\n\n"
        + "  subgraph finance [Finance team]\n    review[Review terms]\n    approve[Approve quote]\n  end\n\n"
        + "  lead --> quote --> review --> approve",
        "swimlane-beta LR\n  subgraph Intake\n    start([Start])\n    task[Do work]\n    fix[Fix issues]\n  end\n\n"
        + "  subgraph Review\n    decision{Ready?}\n  end\n\n  subgraph Complete\n    done((Done))\n  end\n\n"
        + "  start --> task --> decision\n  decision -->|Yes| done\n  decision -->|No| fix\n  fix --> task",
        "swimlane-beta LR\n  subgraph Buyer\n    choose[Choose product]\n    pay[Pay invoice]\n  end\n\n"
        + "  subgraph Store\n    reserve[Reserve stock]\n    ship[Ship product]\n  end\n\n"
        + "  choose --> reserve\n  reserve -->|Invoice ready| pay\n  pay --> ship",
        "swimlane-beta LR\n  accTitle: Support escalation\n"
        + "  accDescr: A request starts with the customer, is triaged by support, and may be escalated to engineering.\n\n"
        + "  subgraph Customer\n    request[Open request]\n  end\n\n  subgraph Support\n    triage[Triage]\n  end\n\n"
        + "  subgraph Engineering\n    resolve[Resolve]\n  end\n\n  request --> triage --> resolve",
        "swimlane-beta LR\n  subgraph Customer\n    submit[Submit order]\n    confirm[Confirm delivery]\n  end\n\n"
        + "  subgraph Store\n    check[Check order]\n    pack[Pack items]\n  end\n\n"
        + "  subgraph Carrier\n    collect[Collect package]\n    deliver[Deliver package]\n  end\n\n"
        + "  submit --> check --> pack --> collect --> deliver --> confirm",
        "swimlane-beta LR\n  subgraph Applicant\n    apply[Submit application]\n    sign[Sign agreement]\n  end\n\n"
        + "  subgraph Reviewer\n    screen[Screen application]\n    decide{Approved?}\n  end\n\n"
        + "  subgraph System\n    create[Create account]\n    notify[Send welcome email]\n  end\n\n"
        + "  apply -->|Application received| screen\n  screen --> decide\n"
        + "  decide -->|Approved| create --> notify --> sign\n  decide -->|Needs changes| apply",
        "swimlane-beta TB\n  subgraph Intake\n    collect[Collect request]\n    validate[Validate details]\n  end\n\n"
        + "  subgraph Review\n    review[Review request]\n    decide{Ready?}\n  end\n\n"
        + "  subgraph Delivery\n    schedule[Schedule work]\n    complete[Complete work]\n  end\n\n"
        + "  collect --> validate --> review --> decide\n  decide -->|Yes| schedule --> complete\n  decide -->|No| collect",
        "swimlane-beta LR\n  subgraph ops [Operations]\n    intake[Receive request]\n    plan[Plan work]\n  end\n\n"
        + "  subgraph legal [Legal]\n    review[Review contract]\n  end\n\n  intake --> plan --> review\n\n"
        + "  classDef attention fill:#fff2cc,stroke:#d6a500,color:#111;\n  class review attention;",
        "swimlane-beta LR\n  subgraph Support\n    classify{Can support solve it?}\n    respond[Respond to customer]\n  end\n\n"
        + "  subgraph Product\n    prioritize[Prioritize fix]\n  end\n\n  subgraph Engineering\n    implement[Implement fix]\n  end\n\n"
        + "  classify -->|Yes| respond\n  classify -->|No| prioritize --> implement --> respond",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own", Intro),
        ("labelled handoffs", Support),

        ("every way a swimlane runs", "swimlane-beta TB\n  A --> B"),
        ("laid out top down", "swimlane-beta TD\n  A --> B"),
        ("laid out bottom up", "swimlane-beta BT\n  A --> B"),
        ("laid out right to left", "swimlane-beta RL\n  A --> B"),
        ("no lanes at all, which is a flowchart", "swimlane-beta LR\n  A[Start] --> B[Stop]"),
        ("a lane with nothing in it", "swimlane-beta\n  subgraph Nobody\n  end"),
        ("a lane named and labelled", "swimlane-beta LR\n  subgraph ops [Operations]\n    plan[Plan]\n  end"),
        ("a lane named in words", "swimlane-beta LR\n  subgraph Sales team\n    plan[Plan]\n  end"),
        ("a lane whose label is quoted", "swimlane-beta LR\n  subgraph ops [\"Operations (all of it)\"]\n    plan[Plan]\n  end"),
        ("a subgraph inside a lane", "swimlane-beta TB\n  subgraph Team\n    subgraph Morning\n      a --> b\n    end\n  end"),
        ("a direction inside a lane", "swimlane-beta LR\n  subgraph Team\n    direction TB\n    a --> b\n  end"),
        ("work handed from lane to lane", "swimlane-beta LR\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two"),
        ("a handoff with something written on it",
         "swimlane-beta LR\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one -->|signed off| two"),
        ("a handoff back the way it came",
         "swimlane-beta LR\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two\n  two --> one"),
        ("a lane a link names", "swimlane-beta LR\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  A --> B"),
        ("what cannot be read of it", "swimlane-beta LR\n  subgraph A\n    one\n  end\n  one --> "),
        ("a lane never closed", "swimlane-beta LR\n  subgraph A\n    one --> two"),
        ("an end with no lane open", "swimlane-beta LR\n  one\n  end"),
        ("a lane half written", "swimlane-beta LR\n  subgraph"),
        ("the accessibility lines", "swimlane-beta LR\n  accTitle: How a claim is settled\n  accDescr: The customer claims.\n  a --> b"),
        ("the shapes a lane holds",
         "swimlane-beta TB\n  subgraph Work\n    start([Start])\n    task[Do it]\n    ask{Ready?}\n    done((Done))\n  end"),
        ("a lane styled", "swimlane-beta LR\n  subgraph A\n    one\n  end\n  style A fill:#eef,stroke:#66a"),
        ("a class over what a lane holds",
         "swimlane-beta LR\n  subgraph A\n    one:::warn\n  end\n  classDef warn fill:#fee,stroke:#c66"),
        ("a laid-out direction nobody writes", "swimlane-beta XY\n  a --> b"),
        ("what the lanes ask for",
         "---\nconfig:\n  swimlane:\n    automaticLaneOrdering: true\n    ignoreCrossLaneEdges: false\n---\n"
         + "swimlane-beta LR\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two"),
        ("what the chart asks for",
         "---\ntitle: How an order is filled\nconfig:\n  flowchart:\n    nodeSpacing: 40\n    rankSpacing: 80\n---\n"
         + "swimlane-beta LR\n  subgraph A\n    one\n  end"),
        ("a comment among the lanes", "swimlane-beta LR\n  %% who does what\n  subgraph A\n    one\n  end"),
        ("nothing written yet", "swimlane-beta"),
        ("nothing but the keyword and a space", "swimlane-beta "),
        ("no header, as a swimlane fence holds it", "subgraph A\n  one\nend\nsubgraph B\n  two\nend\none --> two"),
        ("the language's own name as the header", "swimlane LR\n  subgraph A\n    one\n  end"),
    ];

    /// <summary>
    /// The stages a swimlane's language offers the engine (<c>Shipped.Swimlane</c>), named again here — what the engine adds for
    /// every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolveSubgraphs(),
        new ResolveJoins(),
        new ResolveLinks(),
        new ResolveMetadata(),
        new ResolveStyles(),
        new ResolveShapes(),
        new ResolveChart(),
        new WithConfig<SwimlaneConfig>(SwimlaneConfig.Read(MermaidBlock.Of(tree).Config)),
        new WithIcons(),
        .. holes ? [new WithHoles(SwimlaneParser.Holds)] : Array.Empty<IAstStage>(),
        new WithFolds(),
    ];

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = SwimlaneParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceASwimlaneFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("subgraph A\n  one\nend\nsubgraph B\n  two\nend\none --> two");

        Assert.AreEqual(SwimlaneParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, tree.SelfAndDescendants().Count(node => node.Kind == MermaidKinds.Group), "the first line opens a lane, not a header gone wrong");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void TheLanesKeepTheirOwnOptionsAndTheChartKeepsTheRest()
    {
        var read = SwimlaneConfig.Read("config:\n  swimlane:\n    automaticLaneOrdering: true\n"
                                    + "    ignoreCrossLaneEdges: false\n  flowchart:\n    nodeSpacing: 40");

        Assert.IsTrue(read.Ordered, "automaticLaneOrdering asks for the lane order to be worked out");
        Assert.IsFalse(read.Sideways, "ignoreCrossLaneEdges: false asks for a handoff to count like any other link");
        Assert.AreEqual(40, read.Chart.NodeSpacing, 0.01, "and the spacing is the flowchart's own, which a swimlane shares");
    }

    [TestMethod]
    public void WhatTheLanesAskForByDefaultIsWhatMermaidAsksFor()
    {
        var read = SwimlaneConfig.Read(null);

        Assert.IsTrue(read.Sideways, "a handoff goes across rather than on");
        Assert.IsFalse(read.Ordered, "and the lanes are set across in the order they are written");
    }
}
