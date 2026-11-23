using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Markdown.Mermaid.Architecture.Stages;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Pipeline.Stages;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Mermaid.Architecture;

/// <summary>
/// What <see cref="ArchitectureParser"/> reads an <c>architecture-beta</c> block into: the groups, the services and junctions inside them, the edges
/// between them and the sides they leave by, and the <c>align</c> lines sharing a row or a column. The header is optional, since an
/// <c>architecture</c> fence already says what the block is.
/// </summary>
[TestClass]
[CoversNode("architecture-ast")]
public class ArchitectureParserTests : ContentParserContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    public const string Cloud =
        """
        architecture-beta
            group api(cloud)[API]

            service db(database)[Database] in api
            service disk1(disk)[Storage] in api
            service disk2(disk)[Storage] in api
            service server(server)[Server] in api

            db:L -- R:server
            disk1:T -- B:server
            disk2:T -- B:db
        """;

    /// <summary>The documentation's junctions, which are what several edges meet at.</summary>
    public const string Meeting =
        """
        architecture-beta
            service left_disk(disk)[Disk]
            service top_disk(disk)[Disk]
            service bottom_disk(disk)[Disk]
            service top_gateway(internet)[Gateway]
            service bottom_gateway(internet)[Gateway]
            junction junctionCenter
            junction junctionRight

            left_disk:R -- L:junctionCenter
            top_disk:B -- T:junctionCenter
            bottom_disk:T -- B:junctionCenter
            junctionCenter:R -- L:junctionRight
            top_gateway:B -- T:junctionRight
            bottom_gateway:T -- B:junctionRight
        """;

    /// <summary>The documentation's grid: three tiers, each a row, with columns chained down through them.</summary>
    public const string Tiers =
        """
        architecture-beta
            group sources(cloud)[Sources]
                service src_a(server)[Source A] in sources
                service src_b(server)[Source B] in sources
                service src_c(server)[Source C] in sources

            group storage(database)[Storage]
                service db_one(database)[DB One] in storage
                service db_two(database)[DB Two] in storage
                service db_three(database)[DB Three] in storage

            src_a:B --> T:db_one
            src_b:B --> T:db_two
            src_c:B --> T:db_three

            align row src_a src_b src_c
            align row db_one db_two db_three

            align column src_a db_one
            align column src_b db_two
            align column src_c db_three
        """;

    /// <inheritdoc/>
    protected override ContentNode Parse(string? source) => ArchitectureParser.Parse(source);

    /// <inheritdoc/>
    protected override IEnumerable<IAstStage> Stages(ContentNode tree, bool holes) => Offered(tree, holes);

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Cloud,
        "architecture-beta\n  service fn \"λ\"[Lambda]",
        "architecture-beta\n  service db(database)[The \"main\" DB]\n  service api(server)[\"main\" API]",
        "architecture-beta\n  service db(database)[DB]\n  service server(server)[Server]\n  db : R -- L : server",
        Meeting,
        Tiers,
        "architecture-beta\n    group public_api(cloud)[Public API]\n    group private_api(cloud)[Private API] in public_api",
        "architecture-beta\n    service database1(database)[My Database]",
        "architecture-beta\n    group api(cloud)[API]\n    service db1(database)[DB1] in api\n    service db2(database)[DB2] in api\n"
        + "    service db3(database)[DB3] in api\n    service mcp(server)[MCP] in api\n    db1:R --> L:mcp\n    db2:R --> L:mcp\n"
        + "    db3:R --> L:mcp\n    align column db1 db2 db3",
        "architecture-beta\n    service src1(server)[Source 1]\n    service src2(server)[Source 2]\n    service src3(server)[Source 3]\n"
        + "    service proc(server)[Processor]\n    src1:B --> T:proc\n    src2:B --> T:proc\n    src3:B --> T:proc\n    align row src1 src2 src3",
        "architecture-beta\n    group groupOne(cloud)[One]\n    group groupTwo(cloud)[Two]\n    service server[Server] in groupOne\n"
        + "    service subnet[Subnet] in groupTwo\n\n    server{group}:B --> T:subnet{group}",
        "---\nconfig:\n  architecture:\n    randomize: true\n---\narchitecture-beta\n    group api(cloud)[API]\n"
        + "    service db(database)[Database] in api\n    service server(server)[Server] in api\n    db:R --> L:server",
        "---\nconfig:\n  architecture:\n    idealEdgeLengthMultiplier: 3\n---\narchitecture-beta\n    service a(server)[A]\n"
        + "    service b(server)[B]\n    service c(server)[C]\n    a:R --> L:b\n    b:R --> L:c",
        "architecture-beta\n    group api(logos:aws-lambda)[API]\n\n    service db(logos:aws-aurora)[Database] in api\n"
        + "    service disk1(logos:aws-glacier)[Storage] in api\n    service server(logos:aws-ec2)[Server] in api\n    db:L -- R:server",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("a title and the accessibility lines", "architecture-beta\n  title How it runs\n  accTitle: The parts\n  accDescr: And how they meet\n  service a"),
        ("a service with neither icon nor title", "architecture-beta\n  service alone"),
        ("a service with a title and no icon", "architecture-beta\n  service alone[All by itself]"),
        ("a service whose icon is words of its own", "architecture-beta\n  service alone(\"AWS\")[Lambda]"),
        ("a junction in a group", "architecture-beta\n  group api(cloud)[API]\n  junction split in api"),
        ("every head an edge draws", "architecture-beta\n  service a\n  service b\n  service c\n  service d\n  a:R -- L:b\n  b:R --> L:c\n  c:R <--> L:d"),
        ("an edge with a head at its start only", "architecture-beta\n  service a\n  service b\n  a:R <-- L:b"),
        ("an edge saying what it is", "architecture-beta\n  service a\n  service b\n  a:R -[reads]- L:b"),
        ("an edge saying what it is, in quotes", "architecture-beta\n  service a\n  service b\n  a:R -[\"reads and writes\"]- L:b"),
        ("no space round an edge", "architecture-beta\n  service a\n  service b\n  a:R-->L:b"),
        ("an edge bending round a corner", "architecture-beta\n  service a\n  service b\n  a:T -- L:b"),
        ("an edge out of a group", "architecture-beta\n  group one(cloud)[One]\n  service a in one\n  service b\n  a{group}:B --> T:b"),
        ("a group in a group", "architecture-beta\n  group outer(cloud)[Outer]\n  group inner(cloud)[Inner] in outer\n  service a in inner"),
        ("ids with dashes and digits", "architecture-beta\n  service web-01(server)[Web 01]\n  service db-02(database)[DB 02]\n  web-01:R -- L:db-02"),
        ("space round the brackets", "architecture-beta\n  service a (server) [A service] in api\n  group api(cloud)[API]"),
        ("a comment and a blank line", "architecture-beta\n\n  %% the parts\n  service a(server)[A] %% the first"),
        ("written on Windows", "architecture-beta\r\n  group api(cloud)[API]  \r\n  service a in api\r\n"),
        ("a title in single quotes", "architecture-beta\n  service a(server)['A service']"),
        ("quotes a backslash keeps in a title", "architecture-beta\n  service a[\"say \\\"hi\\\"\"]\n  service b['it\\'s']"),
        ("a title across a line ending", "architecture-beta\n  service a(server)[\"Two\nlines\"] in api\n  group api(cloud)[API]"),
        ("a comment written inside a title", "architecture-beta\n  service a[\"50%% up\"]"),
        ("a quote in a title with words of its own", "architecture-beta\n  service a[say \"hi]\n  service b"),
        ("no header, as an architecture fence holds it", "service a(server)[A]\nservice b\na:R -- L:b"),
        ("a binding supplying lines", "architecture-beta\n  service a\n  {{Services}}\n"),
        // Half written.
        ("a service still to be named", "architecture-beta\n  service "),
        ("an icon never closed", "architecture-beta\n  service a(cloud"),
        ("a title still to say anything", "architecture-beta\n  service a[\"\"]"),
        ("an edge still to name where it goes", "architecture-beta\n  service a\n  a:R --> "),
        ("an edge still to say which side it arrives at", "architecture-beta\n  service a\n  service b\n  a:R --> b"),
        ("an align line still to name a second service", "architecture-beta\n  service a\n  align row a"),
        ("a group still to be put anywhere", "architecture-beta\n  group api(cloud)[API] in "),
        ("nothing but the keyword", "architecture-beta"),
        // What nobody means to write.
        ("an id used twice", "architecture-beta\n  service a(server)[A]\n  group a(cloud)[A]"),
        ("something put in a service", "architecture-beta\n  service a\n  service b in a"),
        ("something put in itself", "architecture-beta\n  group api(cloud)[API] in api"),
        ("an edge to nothing", "architecture-beta\n  service a\n  a:R -- L:nowhere"),
        ("an edge naming a group", "architecture-beta\n  group api(cloud)[API]\n  service a in api\n  a:R -- L:api"),
        ("an edge leaving and arriving on the same side", "architecture-beta\n  service a\n  service b\n  a:R -- R:b"),
        ("an align line naming a group", "architecture-beta\n  group api(cloud)[API]\n  service a in api\n  align row a api"),
        ("an align line of one", "architecture-beta\n  service a\n  align row a"),
        ("a side no edge has", "architecture-beta\n  service a\n  service b\n  a:X -- L:b"),
        ("something no line of an architecture is", "architecture-beta\n  ?!"),
    ];

    /// <summary>
    /// The stages an architecture diagram's language offers the engine (<c>Shipped.Architecture</c>), named again here — what the
    /// engine adds for every language, what a host binds and what runs of words are made of, is held where the engine reads a block.
    /// </summary>
    internal static IReadOnlyList<IAstStage> Offered(ContentNode tree, bool holes) =>
    [
        new ResolveArchitecture(),
        new WithConfig<ArchitectureConfig>(ArchitectureConfig.Read(MermaidBlock.Of(tree).Config)),
        new WithIcons(),
        .. holes ? [new WithHoles(ArchitectureParser.Holds)] : Array.Empty<IAstStage>(),
    ];

    /// <summary>A block parsed and worked over by its stages, holes and all where somebody is writing in it.</summary>
    internal static ContentNode Staged(string source, bool holes = false)
    {
        var tree = ArchitectureParser.Parse(source);
        return new AstPipeline(Offered(tree, holes)).Run(tree);
    }

    [TestMethod]
    public void TheHeaderIsOptional_SinceAnArchitectureFenceSaysWhatTheBlockIs()
    {
        var tree = Staged("service a(server)[A]\nservice b\na:R -- L:b");

        Assert.AreEqual(ArchitectureParser.Language, ((BlockNode)tree).Language);
        Assert.AreEqual(2, tree.SelfAndDescendants().Count(node => node.Kind == ArchitectureKinds.Service), "the first line is a service, not a header gone wrong");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void ATitleIsReadInEitherQuote_ABackslashKeepingWhatWouldEndIt()
    {
        var tree = Staged("architecture-beta\n  service a[\"say \\\"hi\\\"\"]\n  service b['it\\'s']");
        var titles = tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Label).ToList();

        CollectionAssert.AreEqual(new[] { "say \"hi\"", "it's" }, titles.Select(Says).ToArray());
        Assert.AreEqual(2, titles[0].SelfAndDescendants().Count(node => node.Kind == ArchitectureKinds.Escape), "each kept quote a piece of its own");
        Assert.AreEqual(0, tree.SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void ATitleRunsAcrossALineEndingToItsClosingQuote_AndTheServiceIsOneLineOfTheTree()
    {
        const string source = "architecture-beta\n  service a(server)[\"Two\nlines\"] in api\n  group api(cloud)[API]";

        var service = Staged(source).SelfAndDescendants().First(node => node.Kind == ArchitectureKinds.Service);
        Assert.AreEqual("Two lines", Says(service.Children.Single(child => child.Kind == MermaidKinds.Label)), "a line ending is drawn as a space");
        Assert.AreEqual(3, ArchitectureParser.Parse(source).Children.Count, "the header, the service across two rows, and the group");
        Assert.AreEqual(0, Staged(source).SelfAndDescendants().Count(node => node.Trouble is not null));
    }

    [TestMethod]
    public void WrittenIntoATitle_WhatWouldEndItIsKept()
    {
        Assert.AreEqual("say #quot;hi#quot; a\\\\b", ArchitectureParser.Escaped("say \"hi\" a\\b", '"'), "a quote as its entity code, as in any diagram, and a backslash by another");
        Assert.AreEqual("it\\'s \"so\"", ArchitectureParser.Escaped("it's \"so\"", '\''), "in single quotes, that quote by a backslash");
    }

    /// <summary>What a title says, each escape and line ending in it as what it stands for.</summary>
    private static string Says(ContentNode title)
    {
        var said = title.Inner(Kinds.Words)!;
        return said.IsLeaf ? said.Text : string.Concat(said.Children.Select(piece => ArchitectureStandIns.Decoded(piece) ?? piece.Text));
    }

    [TestMethod]
    public void WhatIsWrongIsSaid()
    {
        foreach (var (source, said) in new[]
                 {
                     ("architecture-beta\n  service a(server)[A]\n  group a(cloud)[A]", "already the service"),
                     ("architecture-beta\n  service a\n  service b in a", "only a group holds anything"),
                     ("architecture-beta\n  group api(cloud)[API] in api", "inside itself"),
                     ("architecture-beta\n  service a\n  a:R -- L:nowhere", "No service nowhere"),
                     ("architecture-beta\n  group api(cloud)[API]\n  service a in api\n  a:R -- L:api", "an edge joins services"),
                     ("architecture-beta\n  service a\n  service b\n  a:R -- R:b", "same side"),
                     ("architecture-beta\n  service a\n  align row a", "two services or more"),
                     ("architecture-beta\n  service a\n  service b\n  a:X -- L:b", "L, R, T or B"),
                 })
        {
            var trouble = Staged(source).SelfAndDescendants().Select(node => node.Trouble).OfType<string>().ToList();
            Assert.IsTrue(trouble.Any(reason => reason.Contains(said, StringComparison.Ordinal)),
                          $"{source}\nsays {string.Join(" / ", trouble)}, and nothing about '{said}'");
        }
    }
}

