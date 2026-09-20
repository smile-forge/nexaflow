using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.Sequence;

/// <summary>
/// What a <c>sequenceDiagram</c> block is read into: the participants, the messages between them, the notes beside them, the
/// bars on their lifelines, and the boxes and frames drawn round a run of either.
/// </summary>
[TestClass]
[CoversNode("sequence-diagram")]
public class SequenceGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    public const string Intro =
        """
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            John-->>Alice: Great!
        """;

    /// <summary>The documentation's participants declared in the order they are to stand.</summary>
    public const string Standing =
        """
        sequenceDiagram
            participant Alice
            participant Bob
            Bob->>Alice: Hi Alice
            Alice->>Bob: Hi Bob
        """;

    /// <summary>The documentation's aliases, drawn instead of the names.</summary>
    public const string Aliased =
        """
        sequenceDiagram
            participant A as Alice
            participant J as John
            A->>J: Hello John, how are you?
            J->>A: Great!
        """;

    /// <summary>The documentation's participant types, written as metadata against the name.</summary>
    public const string Typed =
        """
        sequenceDiagram
            participant API@{ "type": "boundary" } as Public API
            actor DB@{ "type": "database" } as User Database
            participant Svc@{ "type": "control" } as Auth Service
            API->>Svc: Authenticate
            Svc->>DB: Query user
            DB-->>Svc: User data
            Svc-->>API: Token
        """;

    /// <summary>And the documentation's aliases written inside that metadata instead.</summary>
    public const string Inline =
        """
        sequenceDiagram
            participant API@{ "type": "boundary", "alias": "Public API" }
            participant Auth@{ "type": "control", "alias": "Auth Service" }
            participant DB@{ "type": "database", "alias": "User Database" }
            API->>Auth: Login request
            Auth->>DB: Query user
            DB-->>Auth: User data
            Auth-->>API: Access token
        """;

    /// <summary>The documentation's participants made and ended partway down.</summary>
    public const string Lifetimes =
        """
        sequenceDiagram
            Alice->>Bob: Hello Bob, how are you ?
            Bob->>Alice: Fine, thank you. And you?
            create participant Carl
            Alice->>Carl: Hi Carl!
            create actor D as Donald
            Carl->>D: Hi!
            destroy Carl
            Alice-xCarl: We are too many
            destroy Bob
            Bob->>Alice: I agree
        """;

    /// <summary>The documentation's boxes.</summary>
    public const string Boxed =
        """
        sequenceDiagram
            box Purple Alice & John
            participant A
            participant J
            end
            box Another Group
            participant B
            participant C
            end
            A->>J: Hello John, how are you?
            J->>A: Great!
            A->>B: Hello Bob, how is Charly ?
            B->>C: Hello Charly, how are you?
        """;

    /// <summary>The documentation's frames, nested and divided.</summary>
    public const string Framed =
        """
        sequenceDiagram
            Alice->>Bob: Hello Bob, how are you?
            alt is sick
                Bob->>Alice: Not so good :(
            else is well
                Bob->>Alice: Feeling fresh like a daisy
            end
            opt Extra response
                Bob->>Alice: Thanks for asking
            end
        """;

    /// <summary>The documentation's washes of colour behind a run of messages, one inside another.</summary>
    public const string Washed =
        """
        sequenceDiagram
            participant Alice
            participant John

            rect rgb(191, 223, 255)
            note right of Alice: Alice calls John.
            Alice->>+John: Hello John, how are you?
            rect rgb(200, 150, 255)
            Alice->>+John: John, can you hear me?
            John-->>-Alice: Hi Alice, I can hear you!
            end
            John-->>-Alice: I feel great!
            end
            Alice ->>+ John: Did you want to go to the game tonight?
            John -->>- Alice: Yeah! See you there.
        """;

    /// <summary>The documentation's numbering.</summary>
    public const string Numbered =
        """
        sequenceDiagram
            autonumber
            Alice->>John: Hello John, how are you?
            loop HealthCheck
                John->>John: Fight against hypochondria
            end
            Note right of John: Rational thoughts!
            John-->>Alice: Great!
            John->>Bob: How about you?
            Bob-->>John: Jolly good!
        """;

    /// <summary>The documentation's menus, written one link at a time and several at once.</summary>
    public const string Menued =
        """
        sequenceDiagram
            participant Alice
            participant John
            link Alice: Dashboard @ https://dashboard.contoso.com/alice
            link Alice: Wiki @ https://wiki.contoso.com/alice
            links John: {"Dashboard": "https://dashboard.contoso.com/john", "Wiki": "https://wiki.contoso.com/john"}
            John-->>Alice: Great!
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.Sequence;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Standing,
        Aliased,
        Typed,
        Inline,
        Lifetimes,
        Boxed,
        Framed,
        Washed,
        Numbered,
        Menued,
        "sequenceDiagram\n    actor Alice\n    actor Bob\n    Alice->>Bob: Hi Bob\n    Bob->>Alice: Hi Alice",
        "sequenceDiagram\n    participant Alice@{ \"type\" : \"boundary\" }\n    participant Bob\n"
        + "    Alice->>Bob: Request from boundary",
        "sequenceDiagram\n    participant API@{ \"type\": \"boundary\", \"alias\": \"Internal Name\" } as External Name\n"
        + "    participant DB@{ \"type\": \"database\", \"alias\": \"Internal DB\" } as External DB\n"
        + "    API->>DB: Query\n    DB-->>API: Result",
        "sequenceDiagram\n    Alice->>John: Hello John, how are you?\n    activate John\n    John-->>Alice: Great!\n"
        + "    deactivate John",
        "sequenceDiagram\n    Alice->>+John: Hello John, how are you?\n    Alice->>+John: John, can you hear me?\n"
        + "    John-->>-Alice: Hi Alice, I can hear you!\n    John-->>-Alice: I feel great!",
        "sequenceDiagram\n    participant John\n    Note right of John: Text in note",
        "sequenceDiagram\n    Alice->John: Hello John, how are you?\n    Note over Alice,John: A typical interaction",
        "sequenceDiagram\n    Alice->John: Hello John, how are you?\n    loop Every minute\n        John-->Alice: Great!\n    end",
        "sequenceDiagram\n    par Alice to Bob\n        Alice->>Bob: Hello guys!\n    and Alice to John\n"
        + "        Alice->>John: Hello guys!\n    end\n    Bob-->>Alice: Hi Alice!\n    John-->>Alice: Hi Alice!",
        "sequenceDiagram\n    critical Establish a connection to the DB\n        Service-->DB: connect\n"
        + "    option Network timeout\n        Service-->Service: Log error\n    option Credentials rejected\n"
        + "        Service-->Service: Log different error\n    end",
        "sequenceDiagram\n    Consumer-->API: Book something\n    API-->BookingService: Start booking process\n"
        + "    break when the booking process fails\n        API-->Consumer: show failure\n    end\n"
        + "    API-->BillingService: Start billing process",
        "sequenceDiagram\n    Alice<<->>John: Hello John, how are you?\n    John<<-->>Alice: Great!",
        "sequenceDiagram\n    Alice->>()John: Hello John\n    Alice()->>John: How are you?",
        "sequenceDiagram\n    A->>B: I #9829; you!\n    B->>A: I #9829; you #infin; times more!",
        "sequenceDiagram\n    Alice->>John: Hello John, how are you?\n    %% this is a comment\n    John-->>Alice: Great!",
        "sequenceDiagram\n    accTitle: How a booking is made\n    accDescr: The consumer asks, and the services answer\n"
        + "    Consumer->>API: Book something",
        "---\ntitle: Hello\n---\n" + Intro,
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own", Intro),
        ("participants declared in order", Standing),
        ("aliases", Aliased),
        ("participant types", Typed),
        ("aliases written inline", Inline),
        ("participants made and ended", Lifetimes),
        ("boxes", Boxed),
        ("frames", Framed),
        ("washes of colour", Washed),
        ("numbering", Numbered),
        ("menus", Menued),

        ("a title of its own", "sequenceDiagram\n  title Here is a title\n  Alice->>Bob: Hi"),
        ("every arrow there is",
         "sequenceDiagram\n  A->B: x\n  A-->B: x\n  A->>B: x\n  A-->>B: x\n  A-xB: x\n  A--xB: x\n  A-)B: x\n  A--)B: x"),
        ("both ways at once", "sequenceDiagram\n  A<<->>B: x\n  A<<-->>B: x"),
        ("half an arrow", "sequenceDiagram\n  A-|\\B: x\n  A--|\\B: x\n  A-|/B: x\n  A--|/B: x"),
        ("half an arrow the other way round", "sequenceDiagram\n  A/|-B: x\n  A/|--B: x\n  A\\|-B: x\n  A\\|--B: x"),
        ("a stick for a head", "sequenceDiagram\n  A-\\\\B: x\n  A--\\\\B: x\n  A-//B: x\n  A--//B: x"),
        ("a stick the other way round", "sequenceDiagram\n  A//-B: x\n  A//--B: x\n  A\\\\-B: x\n  A\\\\--B: x"),
        ("a message to the middle of a lifeline", "sequenceDiagram\n  A->>()B: x\n  A()->>B: x"),
        ("a message with space round its arrow", "sequenceDiagram\n  A ->> B: x"),
        ("a message to a participant itself", "sequenceDiagram\n  A->>A: thinking"),
        ("a message with nothing written on it", "sequenceDiagram\n  A->>B"),
        ("a message saying nothing after its colon", "sequenceDiagram\n  A->>B: "),
        ("a name with a hyphen in it", "sequenceDiagram\n  Order-Service->>Store: save"),
        ("a name with a space in it", "sequenceDiagram\n  John Doe->>Store: save"),
        ("a name in another alphabet", "sequenceDiagram\n  Кліент->>Крамниця: buys"),
        ("a bar started and ended", "sequenceDiagram\n  activate A\n  A->>B: x\n  deactivate A"),
        ("a bar started and ended by the message", "sequenceDiagram\n  A->>+B: x\n  B-->>-A: y"),
        ("a note beside one participant", "sequenceDiagram\n  participant A\n  Note left of A: to the left"),
        ("a note over several", "sequenceDiagram\n  Note over A,B,C: all three"),
        ("a note written in lower case", "sequenceDiagram\n  note over A: quietly"),
        ("a box with a colour and a name", "sequenceDiagram\n  box Aqua The shop\n  participant A\n  end"),
        ("a box with a colour written as its parts", "sequenceDiagram\n  box rgba(33,66,99,0.5) The shop\n  participant A\n  end"),
        ("a box with a colour written in hue", "sequenceDiagram\n  box hsl(10, 40%, 90%) The shop\n  participant A\n  end"),
        ("a box that is see-through", "sequenceDiagram\n  box transparent The shop\n  participant A\n  end"),
        ("a box with no colour at all", "sequenceDiagram\n  box The shop\n  participant A\n  end"),
        ("a wash of colour", "sequenceDiagram\n  rect rgb(191, 223, 255)\n  A->>B: x\n  end"),
        ("a frame inside a frame", "sequenceDiagram\n  loop every day\n    alt is it?\n      A->>B: x\n    else is it not?\n      A->>B: y\n    end\n  end"),
        ("numbering from where it says", "sequenceDiagram\n  autonumber 10 10\n  A->>B: x\n  A->>B: y"),
        ("numbering turned off partway", "sequenceDiagram\n  autonumber\n  A->>B: x\n  autonumber off\n  A->>B: y"),
        ("numbering in hundredths", "sequenceDiagram\n  autonumber 1.5 0.25\n  A->>B: x"),
        ("a link under a participant", "sequenceDiagram\n  participant A\n  link A: Dashboard @ https://example.com"),
        ("several links at once", "sequenceDiagram\n  participant A\n  links A: {\"One\": \"https://one\", \"Two\": \"https://two\"}"),
        ("what a participant is said to be", "sequenceDiagram\n  participant A\n  properties A: {\"class\": \"internal-service\"}"),
        ("what is said about one", "sequenceDiagram\n  participant A\n  details A: {\"Comment\": \"Load balancer\"}"),
        ("a comment closing a line", "sequenceDiagram\n  A->>B: x %% and on"),
        ("the front matter's own sizes",
         "---\nconfig:\n  sequence:\n    actorMargin: 80\n    mirrorActors: false\n    showSequenceNumbers: true\n---\n"
         + "sequenceDiagram\n  A->>B: x"),

        ("a frame never closed", "sequenceDiagram\n  alt is it?\n    A->>B: x"),
        ("a box never closed", "sequenceDiagram\n  box The shop\n  participant A"),
        ("an end closing nothing", "sequenceDiagram\n  A->>B: x\n  end"),
        ("an else with no frame open", "sequenceDiagram\n  A->>B: x\n  else nothing"),
        ("a message with nothing the far side", "sequenceDiagram\n  A->>"),
        ("a message with nothing either side", "sequenceDiagram\n  ->>"),
        ("a new message, as writing one starts it", "sequenceDiagram\n  ->>: "),
        ("a participant with no name yet", "sequenceDiagram\n  participant "),
        ("a note with nowhere to sit", "sequenceDiagram\n  Note somewhere A: x"),
        ("metadata never closed", "sequenceDiagram\n  participant A@{ \"type\": \"actor\""),
        ("metadata nobody means to write", "sequenceDiagram\n  participant A@{ !!! }"),
        ("a link with nowhere to lead", "sequenceDiagram\n  link A: Dashboard"),
        ("numbering by no number at all", "sequenceDiagram\n  autonumber x y\n  A->>B: x"),
        ("something after the keyword", "sequenceDiagram LR\n  A->>B: x"),
        ("nothing anybody means to write", "sequenceDiagram\n  ??? !!!"),
        ("nothing at all", "sequenceDiagram"),
    ];
}
