namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Catalog of sample markdown documents — one per Mermaid diagram type the renderer supports
/// (pie, flowchart, quadrant chart, sequence diagram, gantt, git graph). Each document showcases
/// several variations of its diagram, so the fixtures double as a human-readable reference.
/// </summary>
internal sealed class MarkdownSamples : ISampleSet
{
    public string SubDirectory => "markdown";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("mermaid-pie.md",       Pie),
        SampleFile.Text("mermaid-flowchart.md", Flowchart),
        SampleFile.Text("mermaid-quadrant.md",  Quadrant),
        SampleFile.Text("mermaid-sequence.md",  Sequence),
        SampleFile.Text("mermaid-gantt.md",     Gantt),
        SampleFile.Text("mermaid-gitgraph.md",  GitGraph),
        SampleFile.Text("mermaid-mindmap.md",   Mindmap),
    ];

    private const string Mindmap =
        """
        # Mermaid — Mindmap

        A `mindmap` is a single-rooted tree whose hierarchy comes from indentation.
        Text delimiters set the node shape: `[square] (rounded) ((circle)) {{hexagon}}`,
        `)cloud(` and `))bang((`.

        ```mermaid
        mindmap
          root((mindmap))
            Origins
              Long history
              ::icon(fa fa-book)
              Popularisation
                British popular psychology author Tony Buzan
            Research
              On effectiveness<br/>and features
              On Automatic creation
                Uses
                  Creative techniques
                  Strategic planning
                  Argument mapping
            Tools
              Pen and paper
              Mermaid
        ```

        With shapes

        ```mermaid
        mindmap
          id1[Root topic]
            id2(Rounded)
            id3((Circle))
            id4{{Hexagon}}
            id5)Cloud(
            id6))Bang((
        ```
        """;

    private const string GitGraph =
        """
        # Mermaid — Git graph

        A `gitGraph` draws a commit history: each branch is a coloured lane, with
        commits, branch-offs, merges and cherry-picks connecting them.

        ```mermaid
        gitGraph
           commit
           commit
           branch develop
           checkout develop
           commit
           commit
           checkout main
           merge develop
           commit
           commit
        ```

        With tags, commit types and a cherry-pick

        ```mermaid
        gitGraph
           commit id: "init"
           commit id: "v1" tag: "v1.0.0"
           branch develop
           commit id: "feat-a"
           commit id: "feat-b" type: HIGHLIGHT
           checkout main
           commit id: "hotfix" type: REVERSE
           merge develop tag: "release"
           branch feature order: 3
           commit id: "exp"
           checkout main
           cherry-pick id: "exp"
        ```
        """;

    private const string Gantt =
        """
        # Mermaid — Gantt chart

        A `gantt` chart schedules tasks on a date axis. Tasks carry an id, a start
        (a date or `after <id>`) and an end (a duration, a date, or `until <id>`);
        tags `done`/`active`/`crit`/`milestone` style the bar.

        ```mermaid
        gantt
            title A Gantt Diagram
            dateFormat YYYY-MM-DD
            section Section
                A task          :a1, 2014-01-01, 30d
                Another task    :after a1, 20d
            section Another
                Task in Another :2014-01-12, 12d
                another task    :24d
        ```

        With states and a milestone

        ```mermaid
        gantt
            title Project schedule
            dateFormat YYYY-MM-DD
            axisFormat %m/%d
            section Design
                Spec      :done,      des1, 2024-01-01, 10d
                Mockups   :active,    des2, after des1, 8d
                Review    :crit,      des3, after des2, 4d
            section Build
                Backend   :           b1,   after des2, 20d
                Frontend  :crit,       b2,   after des3, 18d
                Launch    :milestone, m1,   after b1, 0d
        ```
        """;

    private const string Pie =
        """
        # Mermaid — Pie chart

        A `pie` chart renders as a labelled pie with a legend beside it. Adding
        `showData` prints the raw values alongside the percentages.

        ```mermaid
        pie showData title Browser market share (Q1)
            "Chrome" : 64.5
            "Safari" : 18.2
            "Edge" : 5.1
            "Firefox" : 3.2
            "Other" : 9.0
        ```

        with config

        ```mermaid
        ---
        config:
          pie:
            textPosition: 0.5
          themeVariables:
            pieOuterStrokeWidth: "5px"
        ---
        pie showData
            title Key elements in Product X
            "Calcium" : 42.96
            "Potassium" : 50.05
            "Magnesium" : 10.01
            "Iron" :  5
        ```


        """;

    private const string Flowchart =
        """
        # Mermaid — Flowchart

        `flowchart` / `graph` diagrams are laid out top-down (or `LR`, `RL`, `BT`)
        by a Sugiyama layout. Edge labels and node shapes are supported.

        ```mermaid
        flowchart TD
            A[Start] --> B{Is it working?}
            B -->|Yes| C[Ship it]
            B -->|No| D[Debug]
            D --> B
            C --> E([Done])
        ```

        Symbol variations

        ```mermaid
        flowchart RL
            A@{ shape: manual-file, label: "File Handling"}
            B@{ shape: manual-input, label: "User Input"}
            C@{ shape: docs, label: "Multiple Documents"}
            D@{ shape: procs, label: "Process Automation"}
            E@{ shape: paper-tape, label: "Paper Records"}
        	F@{ shape: tag-doc, label: "Tagged document" }
        	G@{ shape: tag-rect, label: "Tagged process" }
        ```

        Chained links

        ```mermaid
        graph LR
            A[Square Rect] -- Link text --> B((Circle))
            A --> C(Round Rect)
            B --> D{Rhombus}
            C --> D
        ```

        Multidirection arrows

        ```mermaid
        flowchart LR
            A o--o B
            B <--> C
            C x--x D
        ```

        Extra dashes


        ```mermaid
        flowchart TD
            A[Start] --> B{Is it?}
            B -->|Yes| C[OK]
            C --> D[Rethink]
            D --> B
            B ---->|No| E[End]
        ```

        split extra dashes

        ```mermaid
        flowchart TD
            A[Start] --> B{Is it?}
            B -- Yes --> C[OK]
            C --> D[Rethink]
            D --> B
            B -- No ----> E[End]
        ```

        subgraphs

        ```mermaid
        flowchart TB
            c1-->a2
            subgraph one
            a1-->a2
            end
            subgraph two
            b1-->b2
            end
            subgraph three
            c1-->c2
            end
            one --> two
            three --> two
            two --> c2
        ```

        Direction in subgraphs with comment
        ```mermaid
        flowchart LR
          subgraph TOP
          %% this is a comment A -- text --> B{node}
            direction TB
            subgraph B1
                direction RL
                i1 -->f1
            end
            subgraph B2
                direction BT
                i2 -->f2
            end
          end
          A --> TOP --> B
          B1 --> B2
        ```

        New format

        ```mermaid
        flowchart LR
            A[Hard edge] -->|Link text| B(Round edge)
            B --> C{Decision}
            C -->|One| D[Result one]
            C -->|Two| E[Result two]
        ```

        Line styles

        ```mermaid
        flowchart LR
            A e1@==> B
            A e2@--> C
            e1@{ curve: linear }
            e2@{ curve: natural }
        ```


        """;

    private const string Quadrant =
        """
        # Mermaid — Quadrant chart

        A `quadrantChart` plots points in a unit square split into four labelled
        quadrants, with low→high axis captions along each edge.

        ```mermaid
        quadrantChart
            title Reach and engagement of campaigns
            x-axis Low Reach --> High Reach
            y-axis Low Engagement --> High Engagement
            quadrant-1 We should expand
            quadrant-2 Need to promote
            quadrant-3 Re-evaluate
            quadrant-4 May be improved
            Campaign A: [0.3, 0.6]
            Campaign B: [0.45, 0.23]
            Campaign C: [0.57, 0.69]
            Campaign D: [0.78, 0.34]
            Campaign E: [0.40, 0.34]
            Campaign F: [0.35, 0.78]
        ```

        With Styling

        ```mermaid
        quadrantChart
          title Reach and engagement of campaigns
          x-axis Low Reach --> High Reach
          y-axis Low Engagement --> High Engagement
          quadrant-1 We should expand
          quadrant-2 Need to promote
          quadrant-3 Re-evaluate
          quadrant-4 May be improved
          Campaign A: [0.9, 0.0] radius: 12
          Campaign B:::class1: [0.8, 0.1] color: #ff3300, radius: 10
          Campaign C: [0.7, 0.2] radius: 25, color: #00ff33, stroke-color: #10f0f0
          Campaign D: [0.6, 0.3] radius: 15, stroke-color: #00ff0f, stroke-width: 5px ,color: #ff33f0
          Campaign E:::class2: [0.5, 0.4]
          Campaign F:::class3: [0.4, 0.5] color: #0000ff
          classDef class1 color: #109060
          classDef class2 color: #908342, radius : 10, stroke-color: #310085, stroke-width: 10px
          classDef class3 color: #f00fff, radius : 10
        ```


        """;

    private const string Sequence =
        """
        # Mermaid — Sequence diagram

        A `sequenceDiagram` draws participant lifelines with messages flowing
        top-to-bottom. Arrow forms set the line and head: `->>` solid, `-->>`
        dashed, `-)` async, `-x` cross, and a self-message loops back.

        ```mermaid
        sequenceDiagram
            participant A as Alice
            participant J as John
            A->>J: Hello John, how are you?
            J-->>A: Great!
            A-)J: See you later!
            A->>A: thinking it over
        ```

        Actors

        ```mermaid
        sequenceDiagram
            actor Alice
            actor Bob
            Alice->>Bob: Hi Bob
            Bob->>Alice: Hi Alice
        ```

        Boundaries

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "boundary" }
            participant Bob
            Alice->>Bob: Request from boundary
            Bob->>Alice: Response to boundary
        ```

        Control

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "control" }
            participant Bob
            Alice->>Bob: Control request
            Bob->>Alice: Control response
        ```

        Entity

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "entity" }
            participant Bob
            Alice->>Bob: Entity request
            Bob->>Alice: Entity response
        ```

        Database

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "database" }
            participant Bob
            Alice->>Bob: DB query
            Bob->>Alice: DB result
        ```

        Collections

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "collections" }
            participant Bob
            Alice->>Bob: Collections request
            Bob->>Alice: Collections response
        ```

        Queue

        ```mermaid
        sequenceDiagram
            participant Alice@{ "type" : "queue" }
            participant Bob
            Alice->>Bob: Queue message
            Bob->>Alice: Queue response
        ```

        Aliases

        ```mermaid
        sequenceDiagram
            participant API@{ "type": "boundary" } as Public API
            actor DB@{ "type": "database" } as User Database
            participant Svc@{ "type": "control" } as Auth Service
            API->>Svc: Authenticate
            Svc->>DB: Query user
            DB-->>Svc: User data
            Svc-->>API: Token
        ```

        inline alias syntax

        ```mermaid
        sequenceDiagram
            participant API@{ "type": "boundary", "alias": "Public API" }
            participant Auth@{ "type": "control", "alias": "Auth Service" }
            participant DB@{ "type": "database", "alias": "User Database" }
            API->>Auth: Login request
            Auth->>DB: Query user
            DB-->>Auth: User data
            Auth-->>API: Access token
        ```

        alias precedence

        ```mermaid
        sequenceDiagram
            participant API@{ "type": "boundary", "alias": "Internal Name" } as External Name
            participant DB@{ "type": "database", "alias": "Internal DB" } as External DB
            API->>DB: Query
            DB-->>API: Result
        ```

        actor creation

        ```mermaid
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
        ```

        Grouping

        ```mermaid
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
            A->>B: Hello Bob, how is Charley?
            B->>C: Hello Charley, how are you?
        ```

        Central Connections

        ```mermaid
        sequenceDiagram
            participant Alice
            participant John
            Alice->>()John: Hello John
            Alice()->>John: How are you?
            John()->>()Alice: Great!
        ```

        Activations

        ```mermaid
        sequenceDiagram
            Alice->>John: Hello John, how are you?
            activate John
            John-->>Alice: Great!
            deactivate John
        ```

        Nested Activations

        ```mermaid
        sequenceDiagram
            Alice->>+John: Hello John, how are you?
            Alice->>+John: John, can you hear me?
            John-->>-Alice: Hi Alice, I can hear you!
            John-->>-Alice: I feel great!
        ```

        Notes

        ```mermaid
        sequenceDiagram
            participant John
            Note right of John: Text in note
        ```

        Spanning Notes

        ```mermaid
        sequenceDiagram
            participant Alice as Alice<br/>Johnson
            Alice->John: Hello John,<br/>how are you?
            Note over Alice,John: A typical interaction<br/>But now in two lines
        ```

        loops

        ```mermaid
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
        ```

        Parrallel actions

        ```mermaid
        sequenceDiagram
            par Alice to Bob
                Alice->>Bob: Go help John
            and Alice to John
                Alice->>John: I want this done today
                par John to Charlie
                    John->>Charlie: Can we do this today?
                and John to Diana
                    John->>Diana: Can you help us today?
                end
            end
        ```

        Break

        ```mermaid
        sequenceDiagram
            Consumer-->API: Book something
            API-->BookingService: Start booking process
            break when the booking process fails
                API-->Consumer: show failure
            end
            API-->BillingService: Start billing process
        ```

        Background hilighting with comments and entity codes

        ```mermaid
        sequenceDiagram
            participant Alice
            participant John

            rect rgb(191, 223, 255)
        	%% this is a comment
            note right of Alice: Alice calls John.
            Alice->>+John: Hello John, how are you?
            rect rgb(200, 150, 255)
            Alice->>+John: John, can you hear me?
            John-->>-Alice: Hi Alice, I can hear you!
            end
            John-->>-Alice: I feel #9829; great!
            end
            Alice ->>+ John: Did you want to go to the game tonight?
            John -->>- Alice: Yeah! See you there.
        ```

        Sequence Numbers

        ```mermaid
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
        ```


        """;
}
