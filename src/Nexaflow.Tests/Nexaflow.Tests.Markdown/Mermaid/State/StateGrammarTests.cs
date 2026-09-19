using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Mermaid.State;

/// <summary>
/// What a <c>stateDiagram</c> block is read into: the states, the transitions between them, the composite states they are gathered
/// into, the notes written beside them, and the lines that lay it out and style it.
/// </summary>
[TestClass]
[CoversNode("state-diagram")]
public class StateGrammarTests : MermaidGrammarContract
{
    /// <summary>The diagram the documentation opens with.</summary>
    public const string Intro =
        """
        stateDiagram-v2
            [*] --> Still
            Still --> [*]

            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]
        """;

    /// <summary>The documentation's composite state, whose own states are written between braces.</summary>
    public const string Composite =
        """
        stateDiagram-v2
          [*] --> Draft
          Draft --> Submitted : submit
          state Review {
            [*] --> Screening
            Screening --> Decision
          }
          Submitted --> Review
          Review --> Published : approved
          Review --> Draft : rejected
          Published --> [*]
        """;

    /// <summary>The documentation's note, written across several lines and closed by <c>end note</c>.</summary>
    public const string Notes =
        """
        stateDiagram-v2
            State1: The state with a note
            note right of State1
                Important information! You can write
                notes.
            end note
            State1 --> State2
            note left of State2 : This is the note to the left.
        """;

    public override MermaidDiagram Diagram => MermaidDiagram.State;

    protected override IEnumerable<string> DocumentedBlocks =>
    [
        Intro,
        Composite,
        Notes,
        "---\ntitle: Simple sample\n---\n" + Intro,
        "---\nconfig:\n  theme: default\n  look: classic\n  layout: dagre\n---\n" + Composite,
        "stateDiagram\n    [*] --> Still\n    Still --> [*]\n\n    Still --> Moving\n    Moving --> Still\n"
        + "    Moving --> Crash\n    Crash --> [*]",
        "stateDiagram-v2\n    stateId",
        "stateDiagram-v2\n    state \"This is a state description\" as s2",
        "stateDiagram-v2\n    s2 : This is a state description",
        "stateDiagram-v2\n    s1 --> s2",
        "stateDiagram-v2\n    s1 --> s2: A transition",
        "stateDiagram-v2\n    [*] --> s1\n    s1 --> [*]",
        "stateDiagram-v2\n    [*] --> First\n    state First {\n        [*] --> second\n        second --> [*]\n    }\n\n"
        + "    [*] --> NamedComposite\n    NamedComposite: Another Composite\n    state NamedComposite {\n"
        + "        [*] --> namedSimple\n        namedSimple --> [*]\n        namedSimple: Another simple\n    }",
        "stateDiagram-v2\n    [*] --> First\n\n    state First {\n        [*] --> Second\n\n        state Second {\n"
        + "            [*] --> second\n            second --> Third\n\n            state Third {\n"
        + "                [*] --> third\n                third --> [*]\n            }\n        }\n    }",
        "stateDiagram-v2\n    [*] --> First\n    First --> Second\n    First --> Third\n\n    state First {\n"
        + "        [*] --> fir\n        fir --> [*]\n    }\n    state Second {\n        [*] --> sec\n        sec --> [*]\n    }\n"
        + "    state Third {\n        [*] --> thi\n        thi --> [*]\n    }",
        "stateDiagram-v2\n    state if_state <<choice>>\n    [*] --> IsPositive\n    IsPositive --> if_state\n"
        + "    if_state --> False: if n < 0\n    if_state --> True : if n >= 0",
        "stateDiagram-v2\n    state fork_state <<fork>>\n      [*] --> fork_state\n      fork_state --> State2\n"
        + "      fork_state --> State3\n\n      state join_state <<join>>\n      State2 --> join_state\n"
        + "      State3 --> join_state\n      join_state --> State4\n      State4 --> [*]",
        "stateDiagram-v2\n    [*] --> Active\n\n    state Active {\n        [*] --> NumLockOff\n"
        + "        NumLockOff --> NumLockOn : EvNumLockPressed\n        NumLockOn --> NumLockOff : EvNumLockPressed\n        --\n"
        + "        [*] --> CapsLockOff\n        CapsLockOff --> CapsLockOn : EvCapsLockPressed\n"
        + "        CapsLockOn --> CapsLockOff : EvCapsLockPressed\n        --\n        [*] --> ScrollLockOff\n"
        + "        ScrollLockOff --> ScrollLockOn : EvScrollLockPressed\n"
        + "        ScrollLockOn --> ScrollLockOff : EvScrollLockPressed\n    }",
        "stateDiagram\n    direction LR\n    [*] --> A\n    A --> B\n    B --> C\n    state B {\n      direction LR\n"
        + "      a --> b\n    }\n    B --> D",
        "stateDiagram-v2\n    [*] --> Still\n    Still --> [*]\n%% this is a comment\n    Still --> Moving\n"
        + "    Moving --> Still %% another comment\n    Moving --> Crash\n    Crash --> [*]",
        "stateDiagram\n   direction TB\n\n   accTitle: This is the accessible title\n   accDescr: This is an accessible description\n\n"
        + "   classDef notMoving fill:white\n   classDef movement font-style:italic\n"
        + "   classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow\n\n"
        + "   [*]--> Still\n   Still --> [*]\n   Still --> Moving\n   Moving --> Still\n   Moving --> Crash\n   Crash --> [*]\n\n"
        + "   class Still notMoving\n   class Moving, Crash movement\n   class Crash badBadEvent\n   class end badBadEvent",
        "stateDiagram\n   direction TB\n\n   classDef notMoving fill:white\n   classDef movement font-style:italic;\n"
        + "   classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow\n\n"
        + "   [*] --> Still:::notMoving\n   Still --> [*]\n   Still --> Moving:::movement\n   Moving --> Still\n"
        + "   Moving --> Crash:::movement\n   Crash:::badBadEvent --> [*]",
        "stateDiagram\n    classDef yourState font-style:italic,font-weight:bold,fill:white\n\n"
        + "    yswsii: Your state with spaces in it\n    [*] --> yswsii:::yourState\n    [*] --> SomeOtherState\n"
        + "    SomeOtherState --> YetAnotherState\n    yswsii --> YetAnotherState\n    YetAnotherState --> [*]",
    ];

    protected override IEnumerable<(string What, string Source)> Blocks { get; } =
    [
        ("the documentation's own", Intro),
        ("a composite state", Composite),
        ("notes either way round", Notes),

        ("a state on its own", "stateDiagram-v2\n  alone"),
        ("where it starts and stops", "stateDiagram-v2\n  [*] --> one\n  one --> [*]"),
        ("what is written on a state", "stateDiagram-v2\n  one : What it is doing"),
        ("what is written on it first", "stateDiagram-v2\n  state \"What it is doing\" as one"),
        ("what is written on a transition", "stateDiagram-v2\n  one --> two : and then"),
        ("a transition written hard against its states", "stateDiagram-v2\n  one-->two"),
        ("a composite state named and opened", "stateDiagram-v2\n  state Outer {\n    one --> two\n  }"),
        ("a composite state written on first", "stateDiagram-v2\n  state \"The outer one\" as Outer {\n    one\n  }"),
        ("composite states nested", "stateDiagram-v2\n  state A {\n    state B {\n      one\n    }\n  }"),
        ("a way of its own inside one", "stateDiagram-v2\n  direction LR\n  state A {\n    direction TB\n    one --> two\n  }"),
        ("regions running at the same time", "stateDiagram-v2\n  state A {\n    one\n    --\n    two\n  }"),
        ("a fork and a join", "stateDiagram-v2\n  state f <<fork>>\n  state j <<join>>\n  f --> j"),
        ("a choice", "stateDiagram-v2\n  state c <<choice>>\n  c --> one : if it is"),
        ("a drawing written in brackets", "stateDiagram-v2\n  state f [[fork]]"),
        ("a drawing nobody writes", "stateDiagram-v2\n  state f <<spoon>>"),
        ("a note beside a state", "stateDiagram-v2\n  one\n  note right of one : mind this"),
        ("a note across several lines", "stateDiagram-v2\n  one\n  note left of one\n    mind this\n  end note"),
        ("a note floating with a name", "stateDiagram-v2\n  note \"mind this\" as n1"),
        ("a note on neither side", "stateDiagram-v2\n  one\n  note beside one : mind this"),
        ("a note never closed", "stateDiagram-v2\n  one\n  note left of one\n    mind this"),
        ("an end note closing nothing", "stateDiagram-v2\n  one\n  end note"),
        ("a class given where it is named", "stateDiagram-v2\n  one:::busy --> two\n  classDef busy fill:#fee"),
        ("classes and styles",
         "stateDiagram-v2\n  one --> two\n  classDef busy fill:#fee,stroke:#c66\n  class one,two busy\n  style two fill:#eef"),
        ("a class nothing declares", "stateDiagram-v2\n  one --> two\n  class one missing"),
        ("a style on a state nobody writes", "stateDiagram-v2\n  one\n  style nowhere fill:#969"),
        ("a state with nothing on it drawn plain", "stateDiagram-v2\n  hide empty description\n  one --> two"),
        ("how wide to draw it", "stateDiagram-v2\n  scale 350 width\n  one --> two"),
        ("where pressing a state leads", "stateDiagram-v2\n  one --> two\n  click one \"https://example.com\" \"Go there\""),
        ("where pressing it leads, said href", "stateDiagram-v2\n  one --> two\n  click one href \"https://example.com\""),
        ("the accessibility lines", "stateDiagram-v2\n  accTitle: How it runs\n  accDescr: It starts and it stops.\n  [*] --> one"),
        ("a comment of its own and one closing a line", "stateDiagram-v2\n  %% what it does\n  one --> two %% and then"),
        ("a brace closing nothing", "stateDiagram-v2\n  one\n  }"),
        ("a composite state never closed", "stateDiagram-v2\n  state A {\n    one"),
        ("a transition with nothing to reach", "stateDiagram-v2\n  one --> "),
        ("a way nobody lays it out", "stateDiagram-v2\n  direction sideways"),
        ("something written after the keyword", "stateDiagram-v2 LR\n  one"),
        ("a line closed with a semicolon", "stateDiagram-v2\n  one --> two;\n  two : and then;"),
        ("nothing written yet", "stateDiagram-v2"),
        ("nothing but the keyword and a space", "stateDiagram-v2 "),
    ];
}
