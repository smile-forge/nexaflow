# Markdown — Diagrams: flow & structure

Diagrams for how something is built, and how it runs: flowcharts, sequence, class, state and ER diagrams, and the C4 family.

---

## Flowchart

Boxes, decisions and flows in any direction, with shaped nodes, labelled edges, subgraphs that gather them and classes that
colour them.

````markdown
```mermaid
flowchart LR
    Start([Open .md]) --> Parse[Parse markdown]
    Parse --> Q{Diagram fence?}
    subgraph Drawing
        Q -- yes --> Native[Render natively]
        Q -- no --> Text[Render as text]
    end
    Native --> Done([Display])
    Text --> Done
    classDef chosen fill:#6e6ce6,stroke:#333
    class Native chosen
```
````

![A flowchart running left to right, two of its nodes gathered in a subgraph and one coloured by a class](images/markdown/mermaid-flowchart.png)

---

## Sequence diagram

Participants and the messages between them, including notes and async arrows.

````markdown
```mermaid
sequenceDiagram
    participant U as User
    participant N as Nexaflow
    participant R as Renderer
    U->>N: Open notes.md
    N->>R: Parse + render blocks
    R-->>N: WPF elements
    N-->>U: Rendered document
    Note over R: Diagrams drawn natively
```
````

![A sequence diagram with three participants and a note](images/markdown/mermaid-sequence.png)

---

## Class diagram

UML classes with attribute and method compartments and the full relationship set.

````markdown
```mermaid
classDiagram
    class Animal {
        +String name
        +int age
        +makeSound() void
    }
    class Dog {
        +String breed
        +bark() void
    }
    class Cat {
        +scratch() void
    }
    Animal <|-- Dog
    Animal <|-- Cat
```
````

![A class diagram with inheritance arrows](images/markdown/mermaid-class.png)

---

## State diagram

States, transitions, start/end markers and composite states.

````markdown
```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Loading : open file
    Loading --> Rendered : success
    Loading --> Error : failure
    Rendered --> Idle : close
    Error --> Idle : retry
    Rendered --> [*]
```
````

![A state machine with start and end markers](images/markdown/mermaid-state.png)

---

## Entity-relationship diagram

Entities with typed attributes and keys, joined by **crow's-foot** cardinality. Identifying
relationships use solid lines, non-identifying use dashed.

````markdown
```mermaid
erDiagram
    CUSTOMER ||--o{ ORDER : places
    CUSTOMER {
        string name
        string email PK
    }
    ORDER ||--|{ LINE_ITEM : contains
    ORDER {
        int id PK
        date placedAt
    }
    LINE_ITEM {
        string product
        int qty
    }
```
````

![An ER diagram with crow's-foot cardinality and attribute boxes](images/markdown/mermaid-er.png)

---

## Block diagram

Blocks on a grid you place yourself: columns, spans, spaces, nested blocks, fat block arrows and edges by id.

````markdown
```mermaid
block-beta
  columns 3
  Frontend blockArrowId6<[" "]>(right) Backend
  space:2 down<[" "]>(down)
  Disk left<[" "]>(left) Database[("Database")]

  classDef front fill:#696,stroke:#333;
  classDef back fill:#969,stroke:#333;
  class Frontend front
  class Backend,Database back
```
````

![A block diagram with block arrows, a database cylinder and class-coloured blocks](images/markdown/mermaid-block.png)

---

## Architecture diagram

Services laid out by the sides their edges leave by: groups box what is put in them, and a junction is where several edges
meet.

````markdown
```mermaid
architecture-beta
    group api(cloud)[API]

    service db(database)[Database] in api
    service disk1(disk)[Storage] in api
    service disk2(disk)[Storage] in api
    service server(server)[Server] in api

    db:L -- R:server
    disk1:T -- B:server
    disk2:T -- B:db
```
````

![An architecture diagram with a cloud group holding a database, two disks and a server](images/markdown/mermaid-architecture.png)

---

## C4 diagrams

Software architecture at C4's zoom levels — context, containers, components — plus deployment and dynamic views.
Elements are cards carrying their kind, technology and description; boundaries nest; relationships name the protocol
they run over.

````markdown
```mermaid
C4Container
title Container diagram for the Internet Banking System

Person(customer, "Personal Banking Customer", "A customer of the bank.")

System_Boundary(c1, "Internet Banking", "System") {
  Container(spa, "Single-Page App", "JavaScript, Angular", "Provides banking functionality in the browser.")
  Container(api, "API Application", "Java, Docker", "Provides banking functionality via a JSON/HTTPS API.")
  ContainerDb(db, "Database", "SQL Database", "Stores user registration information and access logs.")
}

Rel(customer, spa, "Visits bigbank.com/ib using", "HTTPS")
Rel(spa, api, "Makes API calls to", "JSON/HTTPS")
Rel(api, db, "Reads from and writes to", "JDBC")
```
````

![A C4 container diagram with a system boundary, element cards and technology-labelled relationships](images/markdown/mermaid-c4.png)

The body accepts the fuller [C4-PlantUML](https://github.com/plantuml-stdlib/C4-PlantUML) macro vocabulary — `$tags`
with `AddElementTag`, `UpdateElementStyle`, `SHOW_LEGEND`, `Deployment_Node` nesting, `RelIndex` numbering — not just
Mermaid's subset.

---

## C4 sequence

`C4Sequence` has no Mermaid equivalent — it mirrors C4-PlantUML's `C4_Sequence`, and is drawn by the *same* renderer as
a native sequence diagram, so native control lines work inside it.

````markdown
```mermaid
C4Sequence
title Sign-in sequence
SHOW_INDEX()

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
else rejected
  Rel_Back(spa, signin, "401 Unauthorized")
end
```
````

![A C4 sequence diagram with element-card lifelines, a boundary group and an alt fragment](images/markdown/mermaid-c4-sequence.png)

---

More diagram types in [Diagrams](help:MarkdownDiagrams). Back to [Markdown](help:Markdown).
