using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Er;

/// <summary>
/// What an <c>erDiagram</c> block is read into: the entities, the attributes inside them, how many of each entity the other has
/// at either end of a relationship, the subgraphs they are boxed into, and the lines that lay it out and style it.
/// </summary>
[TestClass]
[CoversNode("er-diagram")]
public class ErGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    public const string Intro =
        """
        erDiagram
            CUSTOMER }|..|{ DELIVERY-ADDRESS : has
            CUSTOMER ||--o{ ORDER : places
            CUSTOMER ||--o{ INVOICE : "liable for"
            DELIVERY-ADDRESS ||--o{ ORDER : receives
            INVOICE ||--|{ ORDER : covers
            ORDER ||--|{ ORDER-ITEM : includes
            PRODUCT-CATEGORY ||--|{ PRODUCT : contains
            PRODUCT ||--o{ ORDER-ITEM : "ordered in"
        """;

    /// <summary>The documentation's attributes, with their keys and their comments.</summary>
    public const string Attributed =
        """
        erDiagram
            CAR ||--o{ NAMED-DRIVER : allows
            CAR {
                string registrationNumber PK
                string make
                string model
                string[] parts
            }
            PERSON ||--o{ NAMED-DRIVER : is
            PERSON {
                string driversLicense PK "The license #"
                string(99) firstName "Only 99 characters are allowed"
                string lastName
                string phone UK
                int age
            }
            NAMED-DRIVER {
                string carRegistrationNumber PK, FK
                string driverLicence PK, FK
            }
            MANUFACTURER only one to zero or more CAR : makes
        """;

    /// <summary>The documentation's aliases, which are drawn instead of the names.</summary>
    public const string Aliased =
        """
        erDiagram
            p[Person] {
                string firstName
                string lastName
            }
            a["Customer Account"] {
                string email
            }
            p ||--o| a : has
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Er;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Attributed,
        Aliased,
        "---\ntitle: Order example\n---\n" + Intro,
        "erDiagram\n    direction LR\n    CUSTOMER ||--o{ ORDER : places",
        "erDiagram\n    CUSTOMER ||--o{ ORDER : places\n    style CUSTOMER fill:#f9f,stroke:#333,stroke-width:4px",
        "erDiagram\n    classDef important fill:#f96,stroke:#333,stroke-width:4px\n    CUSTOMER:::important ||--o{ ORDER : places",
        "erDiagram\n    CUSTOMER ||--o{ ORDER : places\n    classDef blue fill:#bbf\n    classDef bold stroke-width:3px\n"
        + "    class CUSTOMER,ORDER blue,bold",
        "erDiagram\n    subgraph sales [\"Sales\"]\n        CUSTOMER ||--o{ ORDER : places\n    end",
        "erDiagram\n    accTitle: What the shop holds\n    accDescr: The customers, their orders and what is in them\n"
        + "    CUSTOMER ||--o{ ORDER : places",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own", Intro),
        ("attributes, keys and comments", Attributed),
        ("aliases", Aliased),

        ("an entity on its own", "erDiagram\n  CUSTOMER"),
        ("an entity with nothing in it", "erDiagram\n  CUSTOMER {\n  }"),
        ("an entity named in quotes", "erDiagram\n  \"Two words\" ||--|| ORDER : places"),
        ("an entity named in another alphabet", "erDiagram\n  Кліент ||--o{ ЗАМОВЛЕННЯ : places"),
        ("an entity given a class", "erDiagram\n  A:::blue ||--|| B : x\n  classDef blue fill:#00f"),
        ("an entity given two classes", "erDiagram\n  A:::blue,bold ||--|| B : x\n  classDef blue fill:#00f\n  classDef bold stroke-width:2px"),
        ("every cardinality there is",
         "erDiagram\n  A |o--o| B : x\n  C ||--|| D : x\n  E }o--o{ F : x\n  G }|--|{ H : x"),
        ("one written in words", "erDiagram\n  A one or zero to zero or many B : x"),
        ("one written optionally to", "erDiagram\n  A many(0) optionally to 1+ B : x"),
        ("one drawn dotted every way", "erDiagram\n  A ||..|| B : x\n  C ||-.|| D : x\n  E ||.-|| F : x"),
        ("one with no space in it", "erDiagram\n  A||--o{B : x"),
        ("one with nothing written on it", "erDiagram\n  A ||--|| B"),
        ("one whose label is in quotes", "erDiagram\n  A ||--|| B : \"what it is\""),
        ("a label holding a semicolon", "erDiagram\n  A ||--|| B : x;"),
        ("a comment closing a line", "erDiagram\n  A ||--|| B : x %% and on"),
        ("an attribute with nothing after its type", "erDiagram\n  A {\n    string\n  }"),
        ("a name written with a star", "erDiagram\n  A {\n    string *id\n  }"),
        ("an attribute still being written", "erDiagram\n  A {\n    string name \n  }"),
        ("a line with nothing on it inside one", "erDiagram\n  A {\n    string name\n\n  }"),
        ("the way it is laid out", "erDiagram\n  direction LR\n  A ||--|| B : x"),
        ("a subgraph", "erDiagram\n  subgraph Sales\n    A ||--|| B : x\n  end"),
        ("a subgraph with a label", "erDiagram\n  subgraph s [\"The sales\"]\n    A ||--|| B : x\n  end"),
        ("a subgraph inside one", "erDiagram\n  subgraph Outer\n    subgraph Inner\n      A\n    end\n    B\n  end"),
        ("a relationship naming a subgraph", "erDiagram\n  subgraph stock\n    PRODUCT\n  end\n  SUPPLIER ||--o{ stock : supplies"),
        ("one naming a subgraph written under it", "erDiagram\n  SUPPLIER ||--o{ stock : supplies\n  subgraph stock\n    PRODUCT\n  end"),
        ("an entity styled on its own", "erDiagram\n  A ||--|| B : x\n  style A fill:#f9f"),
        ("the front matter's own sizes",
         "---\nconfig:\n  er:\n    minEntityWidth: 140\n    entityPadding: 6\n    layoutDirection: LR\n---\n"
         + "erDiagram\n  A ||--|| B : x"),

        ("an entity never closed", "erDiagram\n  A {\n    string name"),
        ("a subgraph never closed", "erDiagram\n  subgraph Sales\n    A"),
        ("a brace closing nothing", "erDiagram\n  A ||--|| B : x\n  }"),
        ("an end closing nothing", "erDiagram\n  A ||--|| B : x\n  end"),
        ("a key nobody knows", "erDiagram\n  A {\n    string name XK\n  }"),
        ("a cardinality nobody writes", "erDiagram\n  A ||--?? B : x"),
        ("a relationship with nothing the far side", "erDiagram\n  A ||--o{ "),
        ("a relationship with nothing either side", "erDiagram\n  ||--o{"),
        ("a new relationship, as writing one starts it", "erDiagram\n  \"\" ||--o{ \"\" : \"\""),
        ("a way nobody lays it out", "erDiagram\n  direction SIDEWAYS"),
        ("an entity nothing writes", "erDiagram\n  A ||--|| B : x\n  style nowhere fill:#f00"),
        ("a class no classDef declares", "erDiagram\n  A ||--|| B : x\n  class A missing"),
        ("something after the keyword", "erDiagram LR\n  A ||--|| B : x"),
        ("nothing anybody means to write", "erDiagram\n  ??? !!!"),
        ("nothing at all", "erDiagram"),
    ];
}
