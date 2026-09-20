using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.C4;

/// <summary>
/// What a <c>C4Sequence</c> block is read into: the macros C4-PlantUML writes, and the sequence diagram's own lines written
/// among them.
/// </summary>
[TestClass]
[CoversNode("c4-macro-syntax")]
public class C4SequenceGrammarTests : MermaidGrammarContract
{
    /// <summary>The sign-in sequence our own documentation shows.</summary>
    public const string Intro =
        """
        C4Sequence
        title Sign-in sequence

        SHOW_INDEX()
        SHOW_FOOT_BOXES(false)

        Person(customer, "Banking Customer")
        Container(spa, "Single-Page App", "Angular")
        Boundary(b, "API Application", "Container")
        Component(signin, "Sign In Controller", "Spring MVC")
        ComponentDb(users, "User Store", "Spring Bean")
        Boundary_End()

        Rel(customer, spa, "Submits credentials", "HTTPS")
        Rel(spa, signin, "POST /signin", "JSON/HTTPS")
        alt credentials valid
        Rel(signin, users, "Looks the user up", "JDBC")
        Rel_Back(signin, users, "Returns the record")
        else rejected
        Rel_Back(spa, signin, "401 Unauthorized")
        end
        Rel_Back(customer, spa, "Shows the dashboard")
        """;

    /// <summary>Every kind of element, and the styling that names them.</summary>
    public const string Styled =
        """
        C4Sequence
        Person(p, "A person", "Somebody outside")
        Person_Ext(q, "Another", "Somebody else's")
        System(s, "A system")
        SystemDb(sd, "A store")
        SystemQueue(sq, "A queue")
        Container(c, "A container", "Java", "What it does")
        ContainerDb(cd, "Its store", "SQL")
        Component(cm, "A component", "Spring MVC")
        UpdateElementStyle("person", $bgColor="#08427b", $fontColor="#ffffff")
        AddElementTag("v1", $bgColor="#1168bd", $legendText="Version one")
        Rel(p, s, "Uses", "HTTPS")
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.C4Sequence;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Styled,
        "C4Sequence\nPerson(customer, \"Banking Customer\", \"A customer of the bank\", $tags=\"v1\")\n"
        + "Container(web, \"Web Application\", \"Java, Spring MVC\", \"Delivers the SPA\")\n"
        + "Rel(customer, web, \"Visits\", \"HTTPS\", $index=Index())",
        "C4Sequence\nBoundary(b, \"The bank\", \"Enterprise\") {\n  System(core, \"Core banking\")\n}\n"
        + "Person(customer, \"Customer\")\nRel(customer, core, \"Banks with\")",
        "C4Sequence\nSHOW_LEGEND()\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\")",
        "C4Sequence\nHIDE_STEREOTYPE()\nSHOW_ELEMENT_DESCRIPTIONS()\nPerson(a, \"A\", \"Somebody\")\nRel(a, a, \"Thinks\")",
        "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nBiRel(a, b, \"Talks to\")\nRelIndex(7, a, b, \"Then this\")",
        "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nAddRelTag(\"async\", $lineStyle=DashedLine(), $legendText=\"Asynchronous\")\n"
        + "Rel(a, b, \"Sends\", $tags=\"async\")\nUpdateRelStyle(a, b, $textColor=\"#ff0000\", $lineColor=\"#ff0000\")",
        "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nnote over a,b: they meet\nactivate b\nRel(a, b, \"Asks\")\ndeactivate b",
        "---\ntitle: Signing in\n---\nC4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\")",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own", Intro),
        ("every kind of element, and the styling", Styled),

        ("a macro with nothing in its brackets", "C4Sequence\nSHOW_INDEX()"),
        ("an element named and nothing else", "C4Sequence\nPerson(a)\nRel(a, a, \"Thinks\")"),
        ("a label holding a comma", "C4Sequence\nContainer(c, \"Web\", \"C#, ASP.NET Core\")\nRel(c, c, \"Runs\")"),
        ("an argument given by name", "C4Sequence\nPerson(a, $label=\"Alice\", $descr=\"Somebody\")\nRel(a, a, \"Thinks\")"),
        ("a number worked out by a call", "C4Sequence\nSHOW_INDEX()\nPerson(a, \"A\")\nRel(a, a, \"One\", $index=Index())\n"
                                          + "Rel(a, a, \"Again\", $index=LastIndex())\nRel(a, a, \"Then\", $index=SetIndex(9))"),
        ("the number moved without drawing", "C4Sequence\nSHOW_INDEX()\nincrement(2)\nSetIndex(5)\nPerson(a, \"A\")\nRel(a, a, \"x\")"),
        ("a boundary opened with a brace", "C4Sequence\nBoundary(b, \"The bank\") {\n  System(s, \"Core\")\n}"),
        ("a boundary closed by its own macro", "C4Sequence\nBoundary(b, \"The bank\")\nSystem(s, \"Core\")\nBoundary_End()"),
        ("a deployment node", "C4Sequence\nDeployment_Node(n, \"A rack\", \"Ubuntu\")\nContainer(c, \"An app\", \"Java\")\n}"),
        ("boundaries nested", "C4Sequence\nBoundary(a, \"Outer\") {\n  Boundary(b, \"Inner\") {\n    System(s, \"Core\")\n  }\n}"),
        ("a frame round the macros", "C4Sequence\nPerson(a, \"A\")\nloop every day\nRel(a, a, \"Thinks\")\nend"),
        ("a note among them", "C4Sequence\nPerson(a, \"A\")\nNote right of a: quietly"),
        ("a relationship the other way round", "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel_Back(a, b, \"Answers\")"),
        ("one both ways at once", "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nBiRel(a, b, \"Talks to\")"),
        ("the layout hints that mean nothing here", "C4Sequence\nPerson(a, \"A\")\nSystem(b, \"B\")\nLay_D(a, b)\nRel(a, b, \"x\")"),
        ("a comment written PlantUML's way", "C4Sequence\n' this is a comment\nPerson(a, \"A\")\nRel(a, a, \"x\")"),
        ("a comment written Mermaid's way", "C4Sequence\nPerson(a, \"A\") %% and on\nRel(a, a, \"x\")"),
        ("the wrappers a pasted diagram brings", "C4Sequence\n@startuml\n!include C4_Sequence.puml\nPerson(a, \"A\")\n@enduml"),
        ("a label with a line break in it", "C4Sequence\nPerson(a, \"Alice<br/>Liddell\")\nRel(a, a, \"x\")"),
        ("an entity code in a label", "C4Sequence\nPerson(a, \"A\")\nRel(a, a, \"I #9829; it\")"),
        ("the front matter's own sizes",
         "---\nconfig:\n  c4:\n    wrap: true\n    width: 200\n---\nC4Sequence\nPerson(a, \"A\")\nRel(a, a, \"x\")"),

        ("a macro whose brackets never close", "C4Sequence\nPerson(a, \"A\""),
        ("a macro nobody knows", "C4Sequence\nWhatIsThis(a, \"A\")"),
        ("a relationship with nothing either side", "C4Sequence\nRel(, , \"x\")"),
        ("a new relationship, as writing one starts it", "C4Sequence\nRel(, , \"\")"),
        ("a brace closing nothing", "C4Sequence\nPerson(a, \"A\")\n}"),
        ("a boundary nothing closes", "C4Sequence\nBoundary(b, \"The bank\")\nSystem(s, \"Core\")"),
        ("something after the keyword", "C4Sequence LR\nPerson(a, \"A\")"),
        ("nothing anybody means to write", "C4Sequence\n??? !!!"),
        ("nothing at all", "C4Sequence"),
    ];
}
