using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.C4;

/// <summary>
/// What a <c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>, <c>C4Dynamic</c> or <c>C4Deployment</c> block is read
/// into: C4-PlantUML's macros, and nothing else — a structural diagram is written in C4's words alone.
/// </summary>
[TestClass]
[CoversNode("c4-macro-syntax")]
public class C4GrammarTests : MermaidGrammarContract
{
    /// <summary>The system context diagram our own documentation shows.</summary>
    public const string Context =
        """
        C4Context
        title System Context diagram for Internet Banking System

        Person(customer, "Personal Banking Customer", "A customer of the bank, with personal accounts.")
        System(banking, "Internet Banking System", "Allows customers to view information about their accounts.")
        System_Ext(mail, "E-mail System", "The internal Microsoft Exchange e-mail system.")
        SystemDb_Ext(mainframe, "Mainframe Banking System", "Stores all of the core banking information.")

        Rel(customer, banking, "Views account balances, makes payments using")
        Rel(banking, mail, "Sends e-mail using", "SMTP")
        Rel_Back(customer, mail, "Sends e-mails to")
        Rel(banking, mainframe, "Gets account information from", "XML/HTTPS")

        SHOW_LEGEND()
        """;

    /// <summary>Containers inside a boundary, with a tag colouring one of them.</summary>
    public const string Containers =
        """
        C4Container
        title Container diagram for the Internet Banking System

        AddElementTag("critical", $bgColor="#c0392b", $fontColor="#ffffff", $legendText="Business critical")

        Person(customer, "Personal Banking Customer", "A customer of the bank.")

        System_Boundary(c1, "Internet Banking", "System") {
          Container(spa, "Single-Page App", "JavaScript, Angular", "Provides banking functionality in the browser.")
          Container(api, "API Application", "Java, Docker", "Provides banking functionality via a JSON/HTTPS API.", $tags="critical")
          ContainerDb(db, "Database", "SQL Database", "Stores user registration information and access logs.")
          ContainerQueue(events, "Event Bus", "Kafka", "Carries domain events between services.")
        }

        System_Ext(mail, "E-mail System", "The internal Microsoft Exchange system.")

        Rel(customer, spa, "Visits bigbank.com/ib using", "HTTPS")
        Rel(spa, api, "Makes API calls to", "JSON/HTTPS")
        Rel(api, db, "Reads from and writes to", "JDBC")
        Rel(api, events, "Publishes to", "Kafka protocol")
        BiRel(api, mail, "Sends and receives e-mail using")
        """;

    /// <summary>Deployment nodes nested three deep.</summary>
    public const string Deployment =
        """
        C4Deployment
        title Deployment diagram for the Internet Banking System — Live

        Deployment_Node(plc, "Big Bank plc", "Big Bank plc data center") {
          Deployment_Node(dn, "bigbank-api***", "Ubuntu 16.04 LTS", "A web server rack") {
            Deployment_Node(apache, "Apache Tomcat", "Apache Tomcat 8.x") {
              Container(api, "API Application", "Java, Docker", "Provides banking functionality via an API.")
            }
          }
          Deployment_Node(bigbankdb, "bigbank-db01", "Oracle 12c") {
            ContainerDb(db, "Database", "Relational database schema", "Stores user registration information.")
          }
        }

        Rel(api, db, "Reads from and writes to", "JDBC")
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.C4;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Context,
        Containers,
        Deployment,
        "C4Component\ntitle Component diagram for the API Application\nLAYOUT_LEFT_RIGHT()\nSHOW_PERSON_OUTLINE()\n"
        + "Person(customer, \"Banking Customer\")\nContainer_Boundary(api, \"API Application\") {\n"
        + "  Component(signin, \"Sign In Controller\", \"MVC Rest Controller\", \"Allows users to sign in.\")\n"
        + "  ComponentDb(store, \"User Store\", \"Spring Bean\", \"Reads user records from the database.\")\n}\n"
        + "Rel(customer, signin, \"Submits credentials to\", \"JSON/HTTPS\")\nRel(signin, store, \"Uses\")",
        "C4Dynamic\nPerson(customer, \"Banking Customer\")\nContainer(spa, \"Single-Page App\", \"Angular\")\n"
        + "Rel(customer, spa, \"Submits credentials\", \"HTTPS\", $index=Index())\n"
        + "Rel_Back(customer, spa, \"Sends back a token\", $index=Index())",
        "C4Context\nEnterprise_Boundary(b, \"Big Bank plc\") {\n  Person(customer, \"Customer\")\n  System(core, \"Core banking\")\n}\n"
        + "Rel(customer, core, \"Banks with\")",
        "C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\")\n"
        + "UpdateElementStyle(\"person\", $bgColor=\"#08427b\", $fontColor=\"#ffffff\")\n"
        + "UpdateRelStyle(a, b, $textColor=\"#ff0000\", $lineColor=\"#ff0000\")",
        "---\ntitle: The bank\n---\nC4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Uses\")",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own context diagram", Context),
        ("containers in a boundary, with a tag", Containers),
        ("deployment nodes nested three deep", Deployment),

        ("every kind of element",
         "C4Container\nPerson(p, \"A person\")\nPerson_Ext(q, \"Another\")\nSystem(s, \"A system\")\nSystemDb(sd, \"A store\")\n"
         + "SystemQueue(sq, \"A queue\")\nSystem_Ext(se, \"Somebody else's\")\nContainer(c, \"A container\", \"Java\", \"What it does\")\n"
         + "ContainerDb(cd, \"Its store\", \"SQL\")\nContainerQueue(cq, \"Its queue\", \"Kafka\")\n"
         + "Component(cm, \"A component\", \"Spring MVC\")\nComponentDb(cmd, \"Its store\", \"Spring Bean\")"),
        ("a macro with nothing in its brackets", "C4Context\nSHOW_LEGEND()"),
        ("an element named and nothing else", "C4Context\nPerson(a)\nSystem(b)\nRel(a, b, \"x\")"),
        ("a label holding a comma", "C4Container\nContainer(c, \"Web\", \"C#, ASP.NET Core\")"),
        ("an argument given by name", "C4Context\nPerson(a, $label=\"Alice\", $descr=\"Somebody\")"),
        ("a stereotype the diagram names itself", "C4Context\nSystem(a, \"A\", $type=\"Legacy\")"),
        ("every direction a relationship may be asked to run",
         "C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel_U(a, b, \"Up\")\nRel_Down(a, b, \"Down\")\n"
         + "Rel_L(a, b, \"Left\")\nRel_Right(a, b, \"Right\")"),
        ("a numbered relationship", "C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRelIndex(7, a, b, \"Then this\")"),
        ("a number worked out by a call",
         "C4Dynamic\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"One\", $index=Index())\n"
         + "Rel(a, b, \"Again\", $index=LastIndex())\nRel(a, b, \"Then\", $index=SetIndex(9))"),
        ("the number moved without drawing", "C4Dynamic\nincrement(2)\nSetIndex(5)\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"x\")"),
        ("a boundary closed by its own macro", "C4Context\nBoundary(b, \"The bank\")\nSystem(s, \"Core\")\nBoundary_End()"),
        ("boundaries nested", "C4Context\nBoundary(a, \"Outer\") {\n  Boundary(b, \"Inner\") {\n    System(s, \"Core\")\n  }\n}"),
        ("a deployment node written the short way", "C4Deployment\nNode(n, \"A rack\", \"Ubuntu\") {\n  Container(c, \"An app\", \"Java\")\n}"),
        ("the layout hints that mean nothing here", "C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nLay_D(a, b)\nRel(a, b, \"x\")"),
        ("what the whole diagram is switched to show",
         "C4Context\nSHOW_PERSON_OUTLINE()\nHIDE_STEREOTYPE()\nLAYOUT_LEFT_RIGHT()\nPerson(a, \"A\")"),
        ("a tag and a boundary tag",
         "C4Context\nAddElementTag(\"v1\", $bgColor=\"#1168bd\", $legendText=\"Version one\")\n"
         + "AddBoundaryTag(\"ours\", $bgColor=\"#eeeeee\")\nBoundary(b, \"Ours\", $tags=\"ours\") {\n  System(s, \"S\", $tags=\"v1\")\n}"),
        ("a relationship tag written as a call", "C4Context\nAddRelTag(\"async\", $lineStyle=DashedLine(), $legendText=\"Asynchronous\")\n"
                                                 + "Person(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"Sends\", $tags=\"async\")"),
        ("a comment written PlantUML's way", "C4Context\n' this is a comment\nPerson(a, \"A\")"),
        ("a comment written Mermaid's way", "C4Context\nPerson(a, \"A\") %% and on"),
        ("the wrappers a pasted diagram brings", "C4Context\n@startuml\n!include C4_Context.puml\nPerson(a, \"A\")\n@enduml"),
        ("a label with a line break in it", "C4Context\nPerson(a, \"Alice<br/>Liddell\")"),
        ("an entity code in a label", "C4Context\nPerson(a, \"A\")\nSystem(b, \"B\")\nRel(a, b, \"I #9829; it\")"),
        ("the front matter's own sizes",
         "---\nconfig:\n  c4:\n    wrap: true\n    width: 200\n    c4ShapeMargin: 30\n---\nC4Context\nPerson(a, \"A\")"),

        ("a macro whose brackets never close", "C4Context\nPerson(a, \"A\""),
        ("a macro nobody knows", "C4Context\nWhatIsThis(a, \"A\")"),
        ("a relationship with nothing either side", "C4Context\nRel(, , \"x\")"),
        ("a new relationship, as writing one starts it", "C4Context\nRel(, , \"\")"),
        ("a brace closing nothing", "C4Context\nPerson(a, \"A\")\n}"),
        ("a boundary nothing closes", "C4Context\nBoundary(b, \"The bank\")\nSystem(s, \"Core\")"),
        ("a line of a sequence diagram, which is not this language", "C4Context\nPerson(a, \"A\")\nalt something\nend"),
        ("something after the keyword", "C4Context LR\nPerson(a, \"A\")"),
        ("nothing anybody means to write", "C4Context\n??? !!!"),
        ("nothing at all", "C4Context"),
    ];
}
