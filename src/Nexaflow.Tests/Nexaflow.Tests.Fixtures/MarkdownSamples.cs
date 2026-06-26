namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Catalog of sample markdown documents — one per Mermaid diagram type the renderer supports
/// (pie, flowchart, quadrant chart, sequence diagram, gantt, git graph, mindmap, state diagram,
/// class diagram, requirement diagram, kanban board, XY chart, radar chart, Ishikawa/fishbone, Sankey, ER, Venn), plus an <c>extensions.md</c> exercising the non-diagram Markdig extensions (emphasis extras,
/// abbreviations, alert blocks). Each document showcases several variations, so the fixtures
/// double as a human-readable reference. The <c>mermaid-*</c> naming marks the diagram docs.
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
        SampleFile.Text("mermaid-state.md",       State),
        SampleFile.Text("mermaid-class.md",       Class),
        SampleFile.Text("mermaid-requirement.md", Requirement),
        SampleFile.Text("mermaid-kanban.md",      Kanban),
        SampleFile.Text("mermaid-xychart.md",     XyChart),
        SampleFile.Text("mermaid-radar.md",       Radar),
        SampleFile.Text("mermaid-ishikawa.md",    Ishikawa),
        SampleFile.Text("mermaid-sankey.md",      Sankey),
        SampleFile.Text("mermaid-er.md",          Er),
        SampleFile.Text("mermaid-venn.md",        Venn),
        SampleFile.Text("extensions.md",          Extensions),
    ];

    private const string Venn =
        """
        # Mermaid — Venn diagram

        A `venn-beta` diagram shows overlapping `set` circles. Comma is the only intersection operator —
        `union A,B` is the A∩B region. A `["Label"]` renames a region and `:N` weights its circle area;
        indented `text` lines list items inside the most recent set/union. Front-matter `config: venn:`
        (`width`/`height`/`padding`) and the `venn1…venn8` theme-variable palette are honoured.

        ## Team overlap

        ```mermaid
        venn-beta
          title "Team overlap"
          set Frontend
          set Backend
          union Frontend,Backend["APIs"]
        ```

        ## Labels, sizes, and items

        ```mermaid
        venn-beta
          set A["Frontend"]:20
            text A1["React"]
            text A2["Design Systems"]
          set B["Backend"]:12
            text B1["API"]
          union A,B["Shared"]:3
            text AB1["OpenAPI"]
        ```

        ## Three sets (desirability / feasibility / viability)

        ```mermaid
        venn-beta
          set Desirable
          set Feasible
          set Viable
          union Desirable,Feasible,Viable["Innovation"]
        ```

        ## Styling and a custom palette

        ```mermaid
        ---
        config:
          themeVariables:
            venn1: "#4e79a7"
            venn2: "#e15759"
        ---
        venn-beta
          set A["Alpha"]:20
            text A1["React"]
          set B["Beta"]:12
          union A,B["AB"]:3
          style A fill:#ff6b6b
          style A,B color:#cccccc
        ```
        """;

    private const string Er =
        """
        # Mermaid — Entity Relationship diagram

        An `erDiagram` models entities and their relationships. Cardinality uses crow's-foot notation —
        either the symbol form (`||--o{`, `}o..o{`) or word aliases (`one to zero or more`); `--` is an
        identifying (solid) relationship, `..` a non-identifying (dashed) one. Entities can carry an
        attribute block (`type name [PK|FK|UK] ["comment"]`). Front-matter `config: er:` (`layoutDirection`,
        `fill`, `stroke`, …) is honoured.

        ## Order example with attributes

        ```mermaid
        ---
        title: Order example
        ---
        erDiagram
            CUSTOMER ||--o{ ORDER : places
            CUSTOMER {
                string name
                string custNumber
                string sector
            }
            ORDER ||--|{ LINE-ITEM : contains
            ORDER {
                int orderNumber
                string deliveryAddress
            }
            LINE-ITEM {
                string productCode
                int quantity
                float pricePerUnit
            }
            CUSTOMER }|..|{ DELIVERY-ADDRESS : uses
        ```

        ## Attribute keys, comments, and array types

        ```mermaid
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
        ```

        ## Word-alias cardinality and non-identifying relationships

        ```mermaid
        erDiagram
            CAR 1 to zero or more NAMED-DRIVER : allows
            PERSON many(0) optionally to 0+ NAMED-DRIVER : is
        ```

        ## Entity name aliases

        ```mermaid
        erDiagram
            p[Person] {
                string firstName
                string lastName
            }
            a["Customer Account"] {
                string email
            }
            p ||--o| a : has
        ```

        ## Direction and styling

        ```mermaid
        ---
        config:
          er:
            layoutDirection: LR
        ---
        erDiagram
            CAR:::someclass {
                string registrationNumber
                string make
            }
            PERSON:::someclass {
                string firstName
                int age
            }
            PERSON ||--o{ CAR : drives

            classDef someclass fill:#f96
        ```
        """;

    private const string Sankey =
        """"
        # Mermaid — Sankey diagram

        A `sankey` diagram depicts a flow from one set of values to another. The body is CSV with three
        columns — `source,target,value`, one link per row; nodes are inferred from the names. Fields with
        commas are double-quoted (a literal quote is a doubled `""`). Front-matter `config: sankey:`
        (sizes, `linkColor`, `nodeAlignment`, `showValues`/`prefix`/`suffix`, `nodeWidth`/`nodePadding`,
        `labelStyle`, `nodeColors`) is honoured.

        ## Energy flow

        ```mermaid
        ---
        config:
          sankey:
            showValues: false
        ---
        sankey

        Agricultural 'waste',Bio-conversion,124.729
        Bio-conversion,Liquid,0.597
        Bio-conversion,Losses,26.862
        Bio-conversion,Solid,280.322
        Bio-conversion,Gas,81.144
        Biofuel imports,Liquid,35
        Biomass imports,Solid,35
        Coal imports,Coal,11.606
        Coal reserves,Coal,63.965
        Coal,Solid,75.571
        District heating,Industry,10.639
        District heating,Heating and cooling - commercial,22.505
        District heating,Heating and cooling - homes,46.184
        Electricity grid,Over generation / exports,104.453
        Electricity grid,Heating and cooling - homes,113.726
        Electricity grid,H2 conversion,27.14
        Electricity grid,Industry,342.165
        Electricity grid,Road transport,37.797
        Electricity grid,Agriculture,4.412
        Electricity grid,Heating and cooling - commercial,40.858
        Electricity grid,Losses,56.691
        Electricity grid,Rail transport,7.863
        Electricity grid,Lighting & appliances - commercial,90.008
        Electricity grid,Lighting & appliances - homes,93.494
        Gas imports,Ngas,40.719
        Gas reserves,Ngas,82.233
        Gas,Heating and cooling - commercial,0.129
        Gas,Losses,1.401
        Gas,Thermal generation,151.891
        Gas,Agriculture,2.096
        Gas,Industry,48.58
        Geothermal,Electricity grid,7.013
        H2 conversion,H2,20.897
        H2 conversion,Losses,6.242
        H2,Road transport,20.897
        Hydro,Electricity grid,6.995
        Liquid,Industry,121.066
        Liquid,International shipping,128.69
        Liquid,Road transport,135.835
        Liquid,Domestic aviation,14.458
        Liquid,International aviation,206.267
        Liquid,Agriculture,3.64
        Liquid,National navigation,33.218
        Liquid,Rail transport,4.413
        Marine algae,Bio-conversion,4.375
        Ngas,Gas,122.952
        Nuclear,Thermal generation,839.978
        Oil imports,Oil,504.287
        Oil reserves,Oil,107.703
        Oil,Liquid,611.99
        Other waste,Solid,56.587
        Other waste,Bio-conversion,77.81
        Pumped heat,Heating and cooling - homes,193.026
        Pumped heat,Heating and cooling - commercial,70.672
        Solar PV,Electricity grid,59.901
        Solar Thermal,Heating and cooling - homes,19.263
        Solar,Solar Thermal,19.263
        Solar,Solar PV,59.901
        Solid,Agriculture,0.882
        Solid,Thermal generation,400.12
        Solid,Industry,46.477
        Thermal generation,Electricity grid,525.531
        Thermal generation,Losses,787.129
        Thermal generation,District heating,79.329
        Tidal,Electricity grid,9.452
        UK land based bioenergy,Bio-conversion,182.01
        Wave,Electricity grid,19.013
        Wind,Electricity grid,289.366
        ```

        ## Basic (with values, and a `%%` header comment)

        ```mermaid
        sankey

        %% source,target,value
        Electricity grid,Over generation / exports,104.453
        Electricity grid,Heating and cooling - homes,113.726
        Electricity grid,H2 conversion,27.14
        ```

        ## Quoted names with commas and doubled quotes

        ```mermaid
        sankey

        Pumped heat,"Heating and cooling, homes",193.026
        Pumped heat,"Heating and cooling, ""commercial""",70.672
        ```

        ## Config — node colours, alignment, units

        ```mermaid
        ---
        config:
          sankey:
            showValues: true
            suffix: " TWh"
            nodeAlignment: left
            nodeWidth: 15
            nodePadding: 18
            linkColor: gradient
            nodeColors:
              Electricity grid: "#4e79a7"
              Industry: "#e15759"
              Losses: "#bab0ab"
        ---
        sankey

        Electricity grid,Heating and cooling - homes,113.726
        Electricity grid,Industry,342.165
        Electricity grid,Losses,56.691
        ```
        """";

    private const string Ishikawa =
        """
        # Mermaid — Ishikawa (fishbone) chart

        An `ishikawa-beta` (alias `ishikawa`) diagram does cause-and-effect / root-cause analysis. The
        first line is the effect (the fish head); every later line is a cause, and the fishbone structure
        comes purely from indentation — categories at the shallowest indent, sub-causes nested deeper.

        ## Blurry photo — nested causes

        ```mermaid
        ishikawa-beta
            Blurry Photo
            Process
                Out of focus
                Shutter speed too slow
                Protective film not removed
                Beautification filter applied
            User
                Shaky hands
            Equipment
                LENS
                    Inappropriate lens
                    Damaged lens
                    Dirty lens
                SENSOR
                    Damaged sensor
                    Dirty sensor
            Environment
                Subject moved too quickly
                Too dark
        ```

        ## Slow API response — two-space indentation

        ```mermaid
        ishikawa-beta
        Slow API Response
          Infrastructure
            Underpowered instances
            No CDN
          Code
            N+1 queries
            Unoptimized indexes
            Missing caching
          Process
            No performance budgets
            Reviews skip load testing
        ```
        """;

    private const string Radar =
        """
        # Mermaid — Radar chart

        A `radar-beta` diagram (radar / spider / Kiviat chart) plots one or more `curve` datasets over a
        set of `axis` spokes. Curve values are positional (`{1, 2, 3}`, mapped to the axes in order) or
        keyed (`{ axisId: value }`). Body options `min` / `max` / `ticks` / `graticule` / `showLegend`
        and front-matter `config: radar:` (geometry), `config: themeVariables: radar:` (styling) and the
        `cScale0…N` curve palette are honoured.

        ## Grades — labeled axes and curves, explicit range

        ```mermaid
        ---
        title: "Grades"
        ---
        radar-beta
          axis m["Math"], s["Science"], e["English"]
          axis h["History"], g["Geography"], a["Art"]
          curve a["Alice"]{85, 90, 80, 70, 75, 90}
          curve b["Bob"]{70, 75, 85, 80, 90, 85}

          max 100
          min 0
        ```

        ## Restaurant comparison — polygon graticule

        ```mermaid
        radar-beta
          title Restaurant Comparison
          axis food["Food Quality"], service["Service"], price["Price"]
          axis ambiance["Ambiance"]

          curve a["Restaurant A"]{4, 3, 2, 4}
          curve b["Restaurant B"]{3, 4, 3, 3}
          curve c["Restaurant C"]{2, 3, 4, 2}
          curve d["Restaurant D"]{2, 2, 4, 3}

          graticule polygon
          max 5
        ```

        ## Bare axes, keyed values, and options

        ```mermaid
        radar-beta
          axis axis1, axis2, axis3
          curve id1["Label1"]{1, 2, 3}
          curve id2["Label2"]{4, 5, 6}, id3{7, 8, 9}
          curve id4{ axis3: 30, axis1: 20, axis2: 10 }

          showLegend true
          ticks 5
          graticule circle
          max 30
        ```

        ## Config and theme — scale factor, tension, custom curve colours

        ```mermaid
        ---
        config:
          radar:
            axisScaleFactor: 0.9
            curveTension: 0.1
          themeVariables:
            cScale0: "#FF0000"
            cScale1: "#00FF00"
            cScale2: "#0000FF"
            radar:
              curveOpacity: 0.5
        ---
        radar-beta
          axis A, B, C, D, E
          curve c1{1, 2, 3, 4, 5}
          curve c2{5, 4, 3, 2, 1}
          curve c3{3, 3, 3, 3, 3}
        ```
        """;

    private const string XyChart =
        """
        # Mermaid — XY chart

        An `xychart` (alias `xychart-beta`) plots `bar` and `line` series against a categorical or
        numeric x-axis and a numeric y-axis. Series share the axes; a named series joins the legend.
        Front-matter `config: xyChart:` (layout/flags) and `config: themeVariables: xyChart:` (colours,
        `plotColorPalette`) are honoured.

        ## Basic — bars and a line

        ```mermaid
        xychart-beta
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
            line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
        ```

        ## Horizontal orientation

        ```mermaid
        xychart-beta horizontal
            title "Books read per month"
            x-axis [jan, feb, mar, apr, may, jun]
            y-axis "Books" 0 --> 10
            bar [2, 4, 3, 6, 5, 8]
        ```

        ## Legend — multiple named line series

        ```mermaid
        xychart-beta
          title "An Example Chart"
          x-axis ["90d", "60d", "30d", "7d", "1d", "Current"]
          y-axis "Seconds" 0 --> 198.2
          line "avg" [48.1, 41.5, 45.7, 72.8, 67.7, 59.9]
          line "p50" [38.2, 36.8, 39.7, 54.5, 49.0, 38.4]
          line "p95" [112.2, 75.3, 103.0, 177.0, 180.2, 109.4]
        ```

        ## Simplest possible — one dataset, axes auto-ranged

        ```mermaid
        xychart-beta
            line [+1.3, .6, 2.4, -.34]
        ```

        ## Custom colours via `plotColorPalette` (with `%%` comments)

        ```mermaid
        ---
        config:
          themeVariables:
            xyChart:
              plotColorPalette: '#000000, #0000FF, #00FF00, #FF0000'
        ---
        xychart-beta
        title "Different Colors in xyChart"
        x-axis "categoriesX" ["Category 1", "Category 2", "Category 3", "Category 4"]
        y-axis "valuesY" 0 --> 50
        %% Black line
        line [10, 20, 30, 40]
        %% Blue bar
        bar [20, 30, 25, 35]
        %% Green bar
        bar [15, 25, 20, 30]
        %% Red line
        line [5, 15, 25, 35]
        ```

        ## Data labels inside the bars (`showDataLabel`)

        ```mermaid
        ---
        config:
            xyChart:
                showDataLabel: true
        ---
        xychart-beta
            title "Genres in top 100 book survey of 2025"
            x-axis [comedy, romance, mystery, crime, "non fiction", other]
            y-axis "Number of Books" 0 --> 30
            bar [12, 2, 20, 25, 17, 24]
        ```

        ## Data labels outside the bars (`showDataLabelOutsideBar`)

        ```mermaid
        ---
        config:
            xyChart:
                showDataLabel: true
                showDataLabelOutsideBar: true
        ---
        xychart-beta
            title "Genres in top 100 book survey of 2025"
            x-axis [comedy, romance, mystery, crime, "non fiction", other]
            y-axis "Number of Books" 0 --> 30
            bar [12, 2, 20, 25, 17, 24]
        ```

        ## Per-point labels on a line

        ```mermaid
        xychart-beta
            title "Smallest AI models scoring above 60% on MMLU"
            x-axis "Date" ["Apr 2022", "Feb 2023", "Jul 2023", "Sep 2023", "Apr 2024"]
            y-axis "Parameters (B)" 0 --> 600
            line [540 "PaLM", 65 "LLaMA-65B", 34 "Llama 2 34B", 7 "Mistral 7B", 3.8 "Phi-3-mini"]
        ```

        ## Per-point labels mixed with unlabeled values

        ```mermaid
        xychart-beta
            title "Quarterly Performance"
            x-axis [Q1, Q2, Q3, Q4]
            y-axis "Revenue ($M)" 0 --> 100
            line [25 "Launch", 45, 72, 90 "Target Hit"]
        ```

        ## Combined layout config and theme colour

        ```mermaid
        ---
        config:
            xyChart:
                width: 900
                height: 600
                showDataLabel: true
            themeVariables:
                xyChart:
                    titleColor: "#ff0000"
        ---
        xychart-beta
            title "Sales Revenue"
            x-axis [jan, feb, mar, apr, may, jun, jul, aug, sep, oct, nov, dec]
            y-axis "Revenue (in $)" 4000 --> 11000
            bar [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
            line [5000, 6000, 7500, 8200, 9500, 10500, 11000, 10200, 9200, 8500, 7000, 6000]
        ```
        """;

    private const string Kanban =
        """
        # Mermaid — Kanban board

        A `kanban` board lays out columns (workflow stages) left-to-right, each holding a stack of
        cards. Hierarchy comes from indentation — columns at the shallowest indent, cards beneath.
        A node is `id[Title]`, `[Title]` or bare `Title`; cards may carry a `@{ … }` metadata block
        with `ticket`, `assigned` and `priority` (`Very High` / `High` / `Low` / `Very Low`).

        ```mermaid
        ---
        config:
          kanban:
            ticketBaseUrl: 'https://mermaidchart.atlassian.net/browse/#TICKET#'
        ---
        kanban
          Todo
            [Create Documentation]
            docs[Create Blog about the new diagram]
          [In progress]
            id6[Create renderer so that it works in all cases. We also add some extra text here for testing purposes. And some more just for the extra flare.]
          id9[Ready for deploy]
            id8[Design grammar]@{ assigned: 'knsv' }
          id10[Ready for test]
            id4[Create parsing tests]@{ ticket: MC-2038, assigned: 'K.Sveidqvist', priority: 'High' }
            id66[last item]@{ priority: 'Very Low', assigned: 'knsv' }
          id11[Done]
            id5[define getData]
            id2[Title of diagram is more than 100 chars when user duplicates diagram with 100 char]@{ ticket: MC-2036, priority: 'Very High'}
            id3[Update DB function]@{ ticket: MC-2037, assigned: knsv, priority: 'High' }

          id12[Can't reproduce]
            id3[Weird flickering in Firefox]
        ```

        Minimal board

        ```mermaid
        kanban
          column1[Backlog]
            task1[Investigate flaky test]
            task2[Write design doc]
          column2[Doing]
            task3[Implement parser]
          column3[Done]
        ```
        """;

    private const string Requirement =
        """
        # Mermaid — Requirement diagram

        A `requirementDiagram` (SysML-style) draws requirements and elements as boxes — a
        «type» + name header over a list of fields (`id`, `text`, `risk`, `verifymethod` /
        `type`, `docref`) — joined by labelled relationships: `contains` is a solid line with a
        crosshair (⊕) at the container, the rest (`copies`, `derives`, `satisfies`, `verifies`,
        `refines`, `traces`) are dashed arrows. It reuses the same box + Sugiyama layout as the
        class diagram.

        Full example

        ```mermaid
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
            docref: reqs/test_entity
        }

        element test_entity3 {
            type: "test suite"
            docref: github.com/all_the_tests
        }

        test_entity - satisfies -> test_req2
        test_req - traces -> test_req2
        test_req - contains -> test_req3
        test_req3 - contains -> test_req4
        test_req4 - derives -> test_req5
        test_req5 - refines -> test_req6
        test_entity3 - verifies -> test_req5
        test_entity - copies -> test_entity2
        ```

        Reverse-arrow form, direction and styling

        ```mermaid
        requirementDiagram
            direction LR

            requirement db_req {
                id: 2
                text: store the records durably.
                risk: high
                verifymethod: test
            }
            element database {
                type: postgres
                docref: infra/db
            }

            database <- satisfies - db_req
            classDef important fill:#f9f,stroke:#333,color:#000
            class db_req important
            style database fill:#bbf,stroke:#338
        ```
        """;

    private const string Class =
        """
        # Mermaid — Class diagram

        A `classDiagram` draws UML classes as boxes with name / attribute / method
        compartments, connected by relationships. Each class is laid out by the shared
        Sugiyama engine; relationship operators set the arrowhead (hollow triangle for
        inheritance, filled/hollow diamond for composition/aggregation, …).

        Inheritance with members, a title, and notes (with `<br>`)

        ```mermaid
        ---
        title: Animal example
        ---
        classDiagram
            note "From Duck till Zebra"
            Animal <|-- Duck
            note for Duck "can fly<br>can swim<br>can dive<br>can help in debugging"
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
        ```

        Class body block

        ```mermaid
        classDiagram
            class BankAccount {
                +String owner
                +BigDecimal balance
                +deposit(amount)
                +withdrawal(amount)
            }
        ```

        Members, return types and generics (incl. nested generics)

        ```mermaid
        classDiagram
            class Square~Shape~{
                int id
                List~int~ position
                setPoints(List~int~ points)
                getPoints() List~int~
            }
            Square : -List~string~ messages
            Square : +setMessages(List~string~ messages)
            Square : +getMessages() List~string~
            Square : +getDistanceMatrix() List~List~int~~
        ```

        Visibility, static and abstract classifiers

        ```mermaid
        classDiagram
            class ClassWithMembers {
                +String id
                +int count$
                +publicMethod()
                -privateMethod()
                #protectedMethod()
                ~packagePrivateMethod()
                +staticMethod()$
                +abstractMethod()*
            }
        ```

        Every relationship type

        ```mermaid
        classDiagram
            classA <|-- classB
            classC *-- classD
            classE o-- classF
            classG <-- classH
            classI -- classJ
            classK <.. classL
            classM <|.. classN
            classO .. classP
        ```

        Annotations (stereotypes)

        ```mermaid
        classDiagram
            class Shape {
                <<interface>>
                noOfVertices
                draw()
            }
            class Color {
                <<enumeration>>
                RED
                GREEN
                BLUE
            }
        ```

        Cardinality / multiplicity / two-way relations and labels

        ```mermaid
        classDiagram
            Customer "1" --> "*" Ticket
            Student "1" --o "1..*" Course
            Galaxy --> "many" Star : Contains
            Planets "0..12" --> "*" Star
            Animal <|--|> Zebra
        ```

        Nested (hierarchical) namespaces

        ```mermaid
        classDiagram
            namespace Company.Engineering.Backend {
                class Developer {
                    +writeCode()
                }
            }
            namespace Company.Engineering.Frontend {
                class Designer {
                    +createMockup()
                }
            }
            namespace Company.Engineering {
                class TechLead {
                    +planSprint()
                }
            }
            TechLead --> Developer : leads
            TechLead --> Designer : leads
        ```

        Lollipop interfaces

        ```mermaid
        classDiagram
            class Class01 {
                int amount
                draw()
            }
            Class01 --() bar
            Class02 --() bar
            foo ()-- Class01
        ```

        Notes

        ```mermaid
        classDiagram
            class Duck
            note "A general note about this diagram"
            note for Duck "Can fly\nCan swim\nCan dive"
        ```

        Direction and styling

        ```mermaid
        classDiagram
            direction LR
            class Student {
                +String name
            }
            class Course
            Student "1" --> "*" Course : enrolled
            classDef highlight fill:#f9f,stroke:#333,color:#000
            class Student:::highlight
            style Course fill:#bbf,stroke:#338
        ```
        """;

    private const string State =
        """
        # Mermaid — State diagram

        A `stateDiagram-v2` models states and the transitions between them. `[*]` is the
        start/end pseudostate; `state X { … }` nests a composite state; `<<choice>>`,
        `<<fork>>`/`<<join>>` and notes are supported.

        Simple sample

        ```mermaid
        ---
        title: Simple sample
        ---
        stateDiagram-v2
            [*] --> Still
            Still --> [*]

            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]
        ```

        Older renderer keyword

        ```mermaid
        stateDiagram
            [*] --> Still
            Still --> [*]

            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]
        ```

        States and descriptions

        ```mermaid
        stateDiagram-v2
            stateId
        ```

        ```mermaid
        stateDiagram-v2
            state "This is a state description" as s2
        ```

        ```mermaid
        stateDiagram-v2
            s2 : This is a state description
        ```

        Transitions

        ```mermaid
        stateDiagram-v2
            s1 --> s2
        ```

        ```mermaid
        stateDiagram-v2
            s1 --> s2: A transition
        ```

        Start and end

        ```mermaid
        stateDiagram-v2
            [*] --> s1
            s1 --> [*]
        ```

        Composite states

        ```mermaid
        stateDiagram-v2
            [*] --> First
            state First {
                [*] --> second
                second --> [*]
            }

            [*] --> NamedComposite
            NamedComposite: Another Composite
            state NamedComposite {
                [*] --> namedSimple
                namedSimple --> [*]
                namedSimple: Another simple
            }
        ```

        Nested composite states

        ```mermaid
        stateDiagram-v2
            [*] --> First

            state First {
                [*] --> Second

                state Second {
                    [*] --> second
                    second --> Third

                    state Third {
                        [*] --> third
                        third --> [*]
                    }
                }
            }
        ```

        Sibling composites

        ```mermaid
        stateDiagram-v2
            [*] --> First
            First --> Second
            First --> Third

            state First {
                [*] --> fir
                fir --> [*]
            }
            state Second {
                [*] --> sec
                sec --> [*]
            }
            state Third {
                [*] --> thi
                thi --> [*]
            }
        ```

        Choice

        ```mermaid
        stateDiagram-v2
            state if_state <<choice>>
            [*] --> IsPositive
            IsPositive --> if_state
            if_state --> False: if n < 0
            if_state --> True : if n >= 0
        ```

        Forks and joins

        ```mermaid
        stateDiagram-v2
            state fork_state <<fork>>
            [*] --> fork_state
            fork_state --> State2
            fork_state --> State3

            state join_state <<join>>
            State2 --> join_state
            State3 --> join_state
            join_state --> State4
            State4 --> [*]
        ```

        Notes

        ```mermaid
        stateDiagram-v2
            State1: The state with a note
            note right of State1
                Important information! You can write
                notes.
            end note
            State1 --> State2
            note left of State2 : This is the note to the left.
        ```

        Concurrency

        ```mermaid
        stateDiagram-v2
            [*] --> Active

            state Active {
                [*] --> NumLockOff
                NumLockOff --> NumLockOn : EvNumLockPressed
                NumLockOn --> NumLockOff : EvNumLockPressed
                --
                [*] --> CapsLockOff
                CapsLockOff --> CapsLockOn : EvCapsLockPressed
                CapsLockOn --> CapsLockOff : EvCapsLockPressed
                --
                [*] --> ScrollLockOff
                ScrollLockOff --> ScrollLockOn : EvScrollLockPressed
                ScrollLockOn --> ScrollLockOff : EvScrollLockPressed
            }
        ```

        Direction

        ```mermaid
        stateDiagram
            direction LR
            [*] --> A
            A --> B
            B --> C
            state B {
              direction LR
              a --> b
            }
            B --> D
        ```

        Comments

        ```mermaid
        stateDiagram-v2
            [*] --> Still
            Still --> [*]
        %% this is a comment
            Still --> Moving
            Moving --> Still %% another comment
            Moving --> Crash
            Crash --> [*]
        ```

        Styling with classDefs

        ```mermaid
        stateDiagram
            direction TB

            accTitle: This is the accessible title
            accDescr: This is an accessible description

            classDef notMoving fill:white
            classDef movement font-style:italic
            classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow

            [*]--> Still
            Still --> [*]
            Still --> Moving
            Moving --> Still
            Moving --> Crash
            Crash --> [*]

            class Still notMoving
            class Moving, Crash movement
            class Crash badBadEvent
        ```

        Inline class operator

        ```mermaid
        stateDiagram
            direction TB

            classDef notMoving fill:white
            classDef movement font-style:italic
            classDef badBadEvent fill:#f00,color:white,font-weight:bold,stroke-width:2px,stroke:yellow

            [*] --> Still:::notMoving
            Still --> [*]
            Still --> Moving:::movement
            Moving --> Still
            Moving --> Crash:::movement
            Crash:::badBadEvent --> [*]
        ```

        Spaces in state names

        ```mermaid
        stateDiagram
            classDef yourState font-style:italic,font-weight:bold,fill:white

            yswsii: Your state with spaces in it
            [*] --> yswsii:::yourState
            [*] --> SomeOtherState
            SomeOtherState --> YetAnotherState
            yswsii --> YetAnotherState
            YetAnotherState --> [*]
        ```
        """;

    private const string Extensions =
        """
        ---
        title: Markdig extensions
        author: Nexaflow
        tags: [emphasis, abbreviations, alerts]
        ---

        # Markdig extensions

        The `--- … ---` block above is YAML front matter — document metadata that is
        parsed but **not rendered** (same as Markdig's HTML output).

        Showcases the non-diagram extensions Nexaflow renders: **emphasis extras**,
        **abbreviations**, and GitHub **alert blocks**.

        ## Emphasis extras

        Plain `*emphasis*` and `**strong**` still work, alongside the extras:

        - Strikethrough: ~~deleted text~~
        - Subscript: H~2~O and CO~2~
        - Superscript: E = mc^2^ and the 1^st^ / 2^nd^ place
        - Marked / highlight: ==important phrase== in a sentence
        - Inserted: ++added text++

        They compose too: ~~**struck bold**~~ and ==marked `code`==.

        ## Abbreviations

        The first standard for the web was HTML, served over HTTP by the W3C.
        Hover any of those to see the expansion.

        *[HTML]: HyperText Markup Language
        *[HTTP]: HyperText Transfer Protocol
        *[W3C]: World Wide Web Consortium

        ## Alert blocks

        > [!NOTE]
        > Useful information that users should know, even when skimming content.

        > [!TIP]
        > Helpful advice for doing things better or more easily.

        > [!IMPORTANT]
        > Key information users need to know to achieve their goal.

        > [!WARNING]
        > Urgent info that needs immediate user attention to avoid problems.

        > [!CAUTION]
        > Advises about risks or negative outcomes of certain actions.

        Alerts hold normal markdown — lists, `code`, **emphasis**:

        > [!TIP]
        > You can nest content:
        >
        > 1. First step
        > 2. Second step with ==marked== text and an abbreviation: HTML.
        """;

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
