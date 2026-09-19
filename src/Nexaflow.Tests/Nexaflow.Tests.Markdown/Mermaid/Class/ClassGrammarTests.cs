using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Class;

/// <summary>
/// What a <c>classDiagram</c> block is read into: the classes, their members, the relations between them, the namespaces they are
/// boxed into, the notes beside them, and the lines that lay it out and style it.
/// </summary>
[TestClass]
[CoversNode("class-diagram")]
public class ClassGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the documentation closes with.</summary>
    public const string Intro =
        """
        classDiagram
            note "From Duck till Zebra"
            Animal <|-- Duck
            Animal <|-- Fish
            Animal <|-- Zebra
            Animal : +int age
            Animal : +String gender
            Animal: +isMammal()
            Animal: +mate()
            class Duck{
                +String beakColor
                +swim()
                +quack()
            }
            class Fish{
                -int sizeInFeet
                -canEat()
            }
            class Zebra{
                +bool is_wild
                +run()
            }
        """;

    /// <summary>The documentation's namespaces, whose classes are written between braces.</summary>
    public const string Spaces =
        """
        classDiagram
        namespace BaseShapes {
            class Triangle
            class Rectangle {
              double width
              double height
            }
        }
        """;

    /// <summary>The documentation's relations, one of each kind.</summary>
    public const string Related =
        """
        classDiagram
        classA <|-- classB : Inheritance
        classC *-- classD : Composition
        classE o-- classF : Aggregation
        classG --> classH : Association
        classI -- classJ : Link(Solid)
        classK ..> classL : Dependency
        classM ..|> classN : Realization
        classO .. classP : Link(Dashed)
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Class;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Spaces,
        Related,
        "---\ntitle: Animal example\n---\n" + Intro,
        "classDiagram\n    class Animal\n    Vehicle <|-- Car",
        "classDiagram\n    class Animal[\"Animal with a label\"]\n    class `Car Class!`\n    Animal --> `Car Class!`",
        "classDiagram\nclass BankAccount\nBankAccount : +String owner\nBankAccount : +BigDecimal balance\n"
        + "BankAccount : +deposit(amount)\nBankAccount : +withdrawal(amount)",
        "classDiagram\nclass BankAccount{\n    +String owner\n    +BigDecimal balance\n    +deposit(amount) bool\n"
        + "    +withdrawal(amount) int\n}",
        "classDiagram\nclass Square~Shape~{\n    int id\n    List~int~ position\n    setPoints(List~int~ points)\n"
        + "    getPoints() List~int~\n}",
        "classDiagram\n    Animal <|--|> Zebra",
        "classDiagram\n  bar ()-- foo\n  class Class01\n  Class01 --() interfaceName",
        "classDiagram\n    Customer \"1\" --> \"*\" Ticket\n    Student \"1\" --> \"1..*\" Course\n"
        + "    Galaxy --> \"many\" Star : Contains",
        "classDiagram\n  class Shape <<interface>>",
        "classDiagram\nclass Shape\n<<interface>> Shape",
        "classDiagram\nclass Shape{\n    <<interface>>\n    noOfVertices\n    draw()\n}\nclass Color{\n"
        + "    <<enumeration>>\n    RED\n    BLUE\n}",
        "classDiagram\n    namespace Auth[\"Authentication Service\"] {\n        class UserService {\n"
        + "            +login()\n            +logout()\n        }\n    }",
        "classDiagram\n    namespace Company.Engineering.Backend {\n        class Developer {\n"
        + "            +writeCode()\n        }\n    }\n    namespace Company.Engineering.Frontend {\n"
        + "        class Designer {\n            +createMockup()\n        }\n    }",
        "classDiagram\n    namespace Platform {\n        namespace Auth {\n            class UserService {\n"
        + "                +login()\n                +logout()\n            }\n        }\n        class Gateway\n    }\n"
        + "    Gateway --> UserService : delegates",
        "---\nconfig:\n  class:\n    hierarchicalNamespaces: false\n---\nclassDiagram\n"
        + "    namespace Company.Engineering.Backend {\n        class Developer\n    }",
        "classDiagram\n    note \"This is a general note\"\n    note for MyClass \"Class-specific note\"\n    class MyClass{}",
        "classDiagram\n%% This whole line is a comment\nclass Shape{\n    <<interface>>\n    noOfVertices\n}",
        "classDiagram\n  direction RL\n  class Student {\n    -idCard : IdCard\n  }\n  class IdCard{\n    -id : int\n  }\n"
        + "  Student \"1\" --o \"1\" IdCard : carries",
        "classDiagram\nclass Shape\nlink Shape \"https://www.github.com\" \"Tooltip text\"\n"
        + "click Shape href \"https://www.github.com\" \"Tooltip\"",
        "classDiagram\nclass Shape\ncallback Shape \"callbackFunction\" \"Tooltip text\"\n"
        + "click Shape call callbackFunction() \"Tooltip\"",
        "classDiagram\n  class Animal\n  class Mineral\n  style Animal fill:#f9f,stroke:#333,stroke-width:4px\n"
        + "  style Mineral fill:#bbf,stroke:#f66,stroke-width:2px",
        "classDiagram\n    class Animal:::someclass\n    classDef someclass fill:#f96",
        "classDiagram\n    class Animal:::pink\n    class Mineral\n    classDef default fill:#f96,color:red\n"
        + "    classDef pink color:#f9f",
        "classDiagram\n    class Animal:::styleClass {\n        -int size\n    }",
        "---\nconfig:\n  class:\n    hideEmptyMembersBox: true\n---\nclassDiagram\n  class Duck",
        "---\nconfig:\n  theme: default\n  look: classic\n  layout: dagre\n---\nclassDiagram\n  class Customer {\n"
        + "    +String name\n  }",
        "classDiagram\n  accTitle: The animal kingdom\n  accDescr: How the animals are related\n  Animal <|-- Duck",
        "classDiagram\n    cssClass \"Animal,Mineral\" styleClass\n    classDef styleClass fill:#f96\n"
        + "    class Animal\n    class Mineral",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own", Intro),
        ("namespaces", Spaces),
        ("one relation of each kind", Related),

        ("a class on its own", "classDiagram\n  class Alone"),
        ("a class with nothing in it", "classDiagram\n  class Alone{}"),
        ("a class with a label", "classDiagram\n  class Alone[\"Shown instead\"]"),
        ("a class named in backticks", "classDiagram\n  class `Odd Name!`\n  `Odd Name!` --> Other"),
        ("a class with type parameters", "classDiagram\n  class Box~T~"),
        ("a class given a class", "classDiagram\n  class Alone:::blue\n  classDef blue fill:#00f"),
        ("a member after a colon", "classDiagram\n  Alone : +int age"),
        ("a member with nothing after the colon", "classDiagram\n  Alone : "),
        ("members between braces", "classDiagram\n  class Alone {\n    +int age\n    +grow()\n  }"),
        ("members never closed", "classDiagram\n  class Alone {\n    +int age"),
        ("an annotation where it is declared", "classDiagram\n  class Shape <<interface>>"),
        ("an annotation on its own line", "classDiagram\n  class Shape\n  <<interface>> Shape"),
        ("an annotation among the members", "classDiagram\n  class Shape {\n    <<interface>>\n    draw()\n  }"),
        ("a relation with no space in it", "classDiagram\n  A<|--B"),
        ("a relation both ways", "classDiagram\n  A <|--|> B"),
        ("a lollipop either end", "classDiagram\n  bar ()-- foo\n  foo --() baz"),
        ("counts at either end", "classDiagram\n  A \"1\" --> \"*\" B"),
        ("what is written on a relation", "classDiagram\n  A --> B : owns"),
        ("a relation closed with a semicolon", "classDiagram\n  A --> B;"),
        ("a namespace with a label", "classDiagram\n  namespace N[\"The name\"] {\n    class A\n  }"),
        ("a namespace nested in one", "classDiagram\n  namespace A {\n    namespace B {\n      class C\n    }\n  }"),
        ("a namespace never closed", "classDiagram\n  namespace N {\n    class A"),
        ("a brace closing nothing", "classDiagram\n  class A\n  }"),
        ("a note about a class", "classDiagram\n  class A\n  note for A \"What it is\""),
        ("a note about nothing", "classDiagram\n  note \"Read me\"\n  class A"),
        ("the way it is laid out", "classDiagram\n  direction LR\n  A --> B"),
        ("a class taking a class", "classDiagram\n  class A\n  classDef blue fill:#00f\n  cssClass \"A\" blue"),
        ("several classes taking one", "classDiagram\n  class A\n  class B\n  classDef blue fill:#00f\n  cssClass \"A,B\" blue"),
        ("a class styled on its own", "classDiagram\n  class A\n  style A fill:#f9f,stroke:#333"),
        ("where pressing a class leads", "classDiagram\n  class A\n  click A href \"https://example.com\" \"Go there\""),
        ("a link with a target", "classDiagram\n  class A\n  link A \"https://example.com\" \"Go there\" _blank"),
        ("a callback nothing here calls", "classDiagram\n  class A\n  callback A \"theFunction\" \"Tip\""),
        ("a call nothing here calls", "classDiagram\n  class A\n  click A call theFunction() \"Tip\""),
        ("a comment closing a line", "classDiagram\n  A --> B %% and on"),
        ("a member pointing at its own line",
         "classDiagram\n  class A {\n    +draw() @@file:///c:/a.cs#ast=T%3AA\n  }"),

        ("a class with no name", "classDiagram\n  class "),
        ("a relation with nothing the far side", "classDiagram\n  A -->"),
        ("a relation with nothing either side", "classDiagram\n  -->"),
        ("an annotation never closed", "classDiagram\n  <<interface Shape"),
        ("a name never closed", "classDiagram\n  class `Odd"),
        ("a namespace with no brace", "classDiagram\n  namespace N"),
        ("a way nobody lays it out", "classDiagram\n  direction SIDEWAYS"),
        ("a link opening nowhere", "classDiagram\n  class A\n  link A \"https://example.com\" \"Tip\" _sideways"),
        ("a class nothing declares", "classDiagram\n  class A\n  style B fill:#f00"),
        ("a class no classDef declares", "classDiagram\n  class A\n  cssClass \"A\" missing"),
        ("nothing anybody means to write", "classDiagram\n  ??? !!!"),
    ];
}
