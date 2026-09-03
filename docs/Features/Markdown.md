# Markdown

Nexaflow renders Markdown **natively** — no browser, no JavaScript, no round-trip to a web view.
Open a `.md` file and you get a fast, scrollable, fully-styled document: headings, tables, task
lists, callouts, real LaTeX math, and a complete family of **Mermaid diagrams** drawn directly on
the canvas.

This page is a tour of what the renderer can do, with the Markdown you'd write on the left and the
actual Nexaflow output on the right.

---

## Viewing & authoring

Markdown opens in its own tab — there's nothing to configure.

- **Open** — double-click any `.md` file in the file explorer (or open it from a project, a
  search result, or a snaplink). It lands in a Markdown tab.
- **Read & edit inline** — by default the tab shows the *rendered* document and lets you edit it in
  place: type into a heading, a list, or a paragraph and it stays formatted as you go.
- **Toggle the raw source** — flip the toolbar switch to drop to plain Markdown source when you want
  to hand-tune the exact text (front-matter, a fiddly table, a diagram fence), then flip back.
- **Save** — `Ctrl`+`S`. The tab tracks unsaved changes and the title shows when you're dirty.

Everything below works in both the rendered view and the preview — it's the same renderer.

---

## The basics

Standard CommonMark — headings, **bold**, _italic_, `inline code`, ordered and unordered lists,
links, and block quotes — all themed to match the app.

````markdown
# Release notes

**Nexaflow** renders _CommonMark_ plus a stack of extensions, drawn natively in WPF — no browser.

1. Open any `.md` file from the explorer
2. Edit inline, or toggle the raw source
3. Save with `Ctrl`+`S`

> Diagrams are first-class — drop a fenced mermaid block anywhere.
````

![Rendered headings, emphasis, list and block quote](images/markdown/feature-basics.png)

---

## Tables & task lists

Pipe tables support per-column alignment and inline formatting; task lists render as tidy
check glyphs.

````markdown
| Feature     | Status | Notes              |
|:------------|:------:|-------------------:|
| Pipe tables |   ✅   | alignment + inline |
| Task lists  |   ✅   | display glyphs     |
| Math        |   ✅   | LaTeX via WpfMath  |

- [x] Render tables
- [x] Render task lists
- [ ] Pour another coffee
````

![A rendered pipe table with alignment and a task list](images/markdown/feature-tables.png)

---

## Callouts

GitHub-style alert blocks (`> [!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]`, `[!CAUTION]`) become
coloured, labelled callouts.

````markdown
> [!NOTE]
> Diagrams render natively — no JavaScript, no browser.

> [!TIP]
> Toggle the raw source from the toolbar to tweak the markdown.

> [!WARNING]
> Remote images aren't fetched; only local files load.
````

![NOTE, TIP and WARNING callouts with coloured borders](images/markdown/feature-callouts.png)

---

## Rich inline extensions

Beyond CommonMark you also get highlight (`==`), strikethrough (`~~`), subscript (`~`), superscript
(`^`), underline (`++`), definition lists, and citations.

````markdown
You can ==highlight==, ~~strike out~~, write H~2~O and E = mc^2^, and ++underline++ inline.

Leader
: A definition list term with a hanging-indent description.

Attribute it with an ""inline citation"" too.
````

![Highlight, strikethrough, sub/superscript, a definition list and a citation](images/markdown/feature-extras.png)

---

## Math

Block (`$$…$$`) and inline (`$…$`) LaTeX are typeset with real math layout.

````markdown
The Gaussian integral, rendered as real math:

$$\int_{-\infty}^{\infty} e^{-x^2}\,dx = \sqrt{\pi}$$

Inline math like $E = mc^2$ flows with the text.
````

![A typeset Gaussian integral and inline math](images/markdown/feature-math.png)

---

## Mermaid diagrams

This is where Nexaflow goes furthest. Drop a fenced `mermaid` block into any document and it's drawn
**natively** — there's no diagram server and no embedded browser. Every diagram below was produced by
the built-in renderer.

To create one, just fence your diagram with `mermaid`:

````markdown
```mermaid
flowchart LR
    A --> B
```
````

The rest of this section walks through every supported diagram type.

### Flowchart

Boxes, decisions and flows in any direction, with shaped nodes and labelled edges.

````markdown
```mermaid
flowchart LR
    Start([Open .md]) --> Parse[Parse markdown]
    Parse --> Q{Diagram fence?}
    Q -- yes --> Native[Render natively]
    Q -- no --> Text[Render as text]
    Native --> Done([Display])
    Text --> Done
```
````

![A flowchart with stadium, rectangle and diamond nodes](images/markdown/mermaid-flowchart.png)

### Sequence diagram

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

### Class diagram

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

### State diagram

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

### Entity-relationship diagram

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

### Requirement diagram

SysML-style requirements and elements with their satisfy / verify / derive relationships.

````markdown
```mermaid
requirementDiagram
    requirement render_req {
        id: 1
        text: Render markdown natively
        risk: low
        verifymethod: test
    }
    element renderer {
        type: component
    }
    renderer - satisfies -> render_req
```
````

![A requirement diagram linking an element to a requirement](images/markdown/mermaid-requirement.png)

### Gantt chart

Project schedules with sections, dependencies (`after`) and task states (`done` / `active`).

````markdown
```mermaid
gantt
    title Project timeline
    dateFormat YYYY-MM-DD
    section Design
    Spec           :done,   des1, 2024-01-01, 2024-01-07
    Mockups        :active, des2, 2024-01-08, 5d
    section Build
    Implementation :        b1, after des2, 10d
    Testing        :        b2, after b1, 4d
```
````

![A Gantt chart with sections and dependency-timed bars](images/markdown/mermaid-gantt.png)

### Git graph

Commit history across branches, with merges.

````markdown
```mermaid
gitGraph
    commit
    branch develop
    commit
    commit
    checkout main
    merge develop
    commit
```
````

![A git graph with a branch and a merge](images/markdown/mermaid-gitgraph.png)

### Kanban board

Columns of cards with priority, assignee and ticket metadata — drawn as a real board.

````markdown
```mermaid
kanban
  Todo
    [Write the showcase doc]
    a[Add the Venn diagram]@{ priority: 'High' }
  [In progress]
    b[Polish the renderers]@{ assigned: 'team', priority: 'Very High' }
  Done
    c[Ship ER diagrams]@{ ticket: NX-42 }
```
````

![A kanban board with priority and ticket chips](images/markdown/mermaid-kanban.png)

### Mindmap

Free-form hierarchies branching out from a central idea.

````markdown
```mermaid
mindmap
  root((Nexaflow))
    Markdown
      CommonMark
      Extensions
      Mermaid
    Files
      Explorer
      Editors
    AI
      Chat
      Tools
```
````

![A mindmap radiating from a central node](images/markdown/mermaid-mindmap.png)

### Pie chart

Proportional slices with a title.

````markdown
```mermaid
pie title Time in Nexaflow
    "Editing" : 45
    "Reading docs" : 30
    "Diagrams" : 25
```
````

![A pie chart with three slices](images/markdown/mermaid-pie.png)

### XY chart

Bar and line series over a shared axis — combine them, name them for a legend, and weight the
colours with config.

````markdown
```mermaid
xychart-beta
    title "Monthly revenue"
    x-axis [jan, feb, mar, apr, may, jun]
    y-axis "Revenue ($k)" 0 --> 60
    bar [20, 35, 30, 45, 50, 55]
    line [20, 35, 30, 45, 50, 55]
```
````

![A combined bar and line XY chart](images/markdown/mermaid-xychart.png)

### Radar chart

Multi-axis comparison (a.k.a. spider chart), with one closed curve per series.

````markdown
```mermaid
---
title: "Skill coverage"
---
radar-beta
  axis ui["UI"], api["API"], db["DB"], qa["QA"], docs["Docs"]
  curve alice["Alice"]{85, 70, 60, 75, 90}
  curve bob["Bob"]{70, 85, 80, 60, 65}
  max 100
```
````

![A radar chart comparing two people across five axes](images/markdown/mermaid-radar.png)

### Quadrant chart

Plot items against two axes, captioned by quadrant.

````markdown
```mermaid
quadrantChart
    title Reach and engagement of campaigns
    x-axis Low Reach --> High Reach
    y-axis Low Engagement --> High Engagement
    quadrant-1 Expand
    quadrant-2 Promote
    quadrant-3 Re-evaluate
    quadrant-4 Improve
    Campaign A: [0.3, 0.6]
    Campaign B: [0.45, 0.23]
    Campaign C: [0.57, 0.69]
    Campaign D: [0.78, 0.34]
```
````

![A quadrant chart with four labelled quadrants and plotted points](images/markdown/mermaid-quadrant.png)

### Sankey diagram

Flows whose ribbon widths are proportional to value, with optional value labels and units.

````markdown
```mermaid
---
config:
  sankey:
    showValues: true
    suffix: " TWh"
---
sankey

Coal,Electricity,75
Gas,Electricity,40
Nuclear,Electricity,90
Electricity,Industry,80
Electricity,Homes,75
Electricity,Losses,50
```
````

![A Sankey diagram of energy flowing from sources to uses](images/markdown/mermaid-sankey.png)

### Ishikawa (fishbone) diagram

Cause-and-effect / root-cause analysis: an effect on the spine, with categories and nested causes
branching off it.

````markdown
```mermaid
ishikawa-beta
    Slow page load
    Frontend
        Large JS bundle
        No code splitting
    Backend
        N+1 queries
        Missing cache
    Network
        No CDN
        Chatty API
```
````

![A fishbone diagram with categories branching off a central spine](images/markdown/mermaid-ishikawa.png)

### Venn diagram

Overlapping sets, sized by weight, with labelled intersections.

````markdown
```mermaid
venn-beta
  title "The innovation sweet spot"
  set Desirable
  set Feasible
  set Viable
  union Desirable,Feasible,Viable["Innovation"]
```
````

![A three-circle Venn diagram with a central intersection](images/markdown/mermaid-venn.png)

### Timeline

Periods along a spine, each with its events stacked beneath; sections band the periods they group.

````markdown
```mermaid
timeline
    title History of Social Media Platform
    2002 : LinkedIn
    2004 : Facebook
         : Google
    2005 : YouTube
    2006 : Twitter
```
````

![A timeline of periods on a spine with events stacked beneath each](images/markdown/mermaid-timeline.png)

### User journey

Scored steps of a task: a face per score floats higher for a better experience, and each actor gets a colour.

````markdown
```mermaid
journey
    title My working day
    section Go to work
      Make tea: 5: Me
      Go upstairs: 3: Me
      Do work: 1: Me, Cat
    section Go home
      Go downstairs: 5: Me
      Sit down: 5: Me
```
````

![A user journey with section bands, scored faces and actor dots](images/markdown/mermaid-journey.png)

### Block diagram

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

### C4 diagrams

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

### C4 sequence

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

## QR codes

A fenced `qr` block becomes a scannable QR code, generated on your machine like everything else here.
The body is a flat list of `key: value` lines — a `type:`, then the fields that type needs:

````markdown
```qr
type: url
url: https://markdown.org
```
````

![Three QR codes: a link, a Wi-Fi network and a contact card](images/markdown/qr-codes.png)

The point of the `type:` is that a QR code only carries text — a phone offers to *join this network*
or *add this contact* because the text follows a convention. Writing the convention by hand is
miserable, so the block writes it for you:

| `type:` | Fields | What a scanner offers |
|---|---|---|
| `text` | `text` | Plain text |
| `url` | `url` | Open the link (a missing `https://` is filled in) |
| `email` | `email`, `subject`, `body` | Compose a message |
| `phone` | `phone` | Dial |
| `sms` | `number`, `message` | Send a text |
| `wifi` | `ssid`, `password`, `security`, `hidden` | Join the network |
| `vcard` | `name`, `org`, `title`, `phone`, `email`, `url`, `address` | Add the contact |
| `mecard` | `name`, `phone`, `email`, `url`, `address`, `note` | Add the contact, from a smaller code |
| `geo` | `lat`, `lng` | Open the map pin |
| `event` | `title`, `location`, `start`, `end` | Add to the calendar |
| `epc` | `name`, `iban`, `bic`, `amount`, `purpose`, `reference`, `message` | Prefill a bank transfer |
| `crypto` | `coin`, `address`, `amount` | Open the wallet |

`mecard` is the compact form of `vcard`: it drops the organisation and job title and produces a
noticeably smaller code, which older readers also handle more reliably. `epc` is the **GiroCode** seen
on European invoices — it takes euro amounts only, and either a structured `reference:` or free
`message:` text, not both. Its IBAN is checked (including the check digits) before the code is drawn,
because a mistyped one scans perfectly and then fails at the bank.

Any block can also carry these settings:

| Setting | Values | Default |
|---|---|---|
| `ec` | `L`, `M`, `Q`, `H` — how much damage the code survives | `M` |
| `cellSize` | pixels per module, 1–64 | `4` |
| `margin` | quiet zone in modules, 0–32 | `4` |
| `dark` | hex colour of the modules | the theme's |
| `light` | hex colour behind them | the theme's |

```qr
type: wifi
ssid: MyNetwork
password: s3cr3t-pass
security: WPA
ec: H
cellSize: 6
```

Higher error correction is worth it for anything that will be printed, put on a curved surface, or
partly covered — it costs capacity, so the same content needs a slightly larger code.

A block that can't be built says so in place of the picture, naming the line at fault: a misspelled
setting, a field belonging to another type, a missing required field, or content too long to fit.

---

## Barcodes

A fenced `barcode` block becomes a scannable linear barcode, generated on your machine like everything
else here. The body is a flat list of `key: value` lines — a `format:`, a `value:`, and whatever
settings you want:

````markdown
```barcode
format: EAN13
value: 590123412345
```
````

![Barcodes: Code 128, an EAN-13 and an ISBN with its price add-on](images/markdown/barcodes.png)

**The value is editable where it is drawn.** Click into the digits under the bars and type — the
symbol re-encodes as you go, and the change goes back into the fence in your document. While the
caret is in it you are shown the value itself rather than the number the symbol prints, because for
several of these formats those are different strings: an ISBN's value carries hyphens the symbol
never prints, and an EAN-13's gains the check digit you left off.

### Formats

| `format:` | Carries | Notes |
|---|---|---|
| `CODE128` | Any ASCII | Moves between its subsets to keep the symbol short |
| `CODE128A` / `CODE128B` / `CODE128C` | One subset each | When a scanner insists on one |
| `EAN13` / `EAN8` | 12 or 7 digits | The check digit is computed, or verified if you write it |
| `UPC` (`UPCA`) / `UPCE` | 11 or 7 digits | `UPC` and `UPCA` are the same thing |
| `EAN2` / `EAN5` | 2 or 5 digits | The add-on block, on its own |
| `ISBN` / `ISSN` / `ISMN` | The number as printed | Hyphens and all; add a space and an add-on for a price or issue |
| `CODE39` | Digits, capitals, `- . $ / + % space` | The old workhorse |
| `ITF` / `ITF14` | An even number of digits | `ITF14` is the shipping-carton one, and adds its own check digit |
| `MSI`, `MSI10`, `MSI11`, `MSI1010`, `MSI1110` | Digits | The suffix says which check digits to add |
| `PHARMACODE` | 3–131070 | Read right to left, by design |
| `CODABAR` | Digits and `- $ : / . +` | Start/stop letters `A`–`D` are added if you leave them off |

The retail formats are drawn the way they are printed: the number broken at the guard bars into the
groups that sit in the wells between them, the first digit set outside the symbol, the guards running
down past the digits, and an add-on's digits above its own bars. The shape is how these are recognised
at a glance, so it is worth getting right even though a scanner only reads the bars.

An ISBN, ISSN or ISMN is not a symbology — each is a numbering scheme that agreed to be *printed* as
an EAN-13 by reserving a prefix. So these work out which thirteen digits your number stands for, print
it under the scheme's own name, and hand the bars to EAN-13. A ten-digit ISBN is promoted the way the
standard says. Anything after a space is the add-on:

````markdown
```barcode
format: ISBN
value: 978-1-56581-231-4 90000
```
````

### Settings

| Setting | Values | Default |
|---|---|---|
| `width` | width of a **single bar** in pixels, 0.5–20 | `2` |
| `height` | bar height in pixels, 4–1000 | `100` |
| `displayValue` | `true` / `false` — print the number under the bars | `true` |
| `fontSize` | pixels, 4–200 | `20` |
| `textAlign` | `left`, `center`, `right` | `center` |
| `lineColor` | hex colour of the bars | the theme's |
| `background` | hex colour behind them | the theme's |
| `margin` | quiet zone in pixels, 0–200 | `10` |

`width` is the width of one bar, not of the whole symbol — that follows from the value, since a
barcode's length is decided by what it encodes. Doubling `width` doubles the symbol.

### When it can't be drawn

The two ways a block can be wrong are treated differently, because only one of them is your problem
while you are typing:

- A block that can't be **understood** — an unknown setting, a format that doesn't exist, a width
  that isn't a number — is shown as its source with the reason above it. There is nothing to draw.
- A value the format can't **carry** still renders. The bars of a valid value in that format are
  drawn faint with a line struck through them, and a red wave goes under the value; hovering says
  why. This matters because a value is invalid every time you are halfway through changing it — an
  EAN-13 has the wrong number of digits for all but the last keystroke.

---

## Good to know

- **It's all local.** Diagrams and math render on your machine — nothing is sent anywhere, and the
  renderer works offline.
- **Local images only.** `![](…)` loads local image files; remote `http(s)` and `data:` images are
  not fetched (you'll see the alt text instead).
- **Front-matter config.** Several diagrams accept a `--- config: … ---` front-matter block to tune
  their look (colours, sizes, orientation) — see the XY, radar, Sankey and Venn examples above.
- **What a QR code *does* is the scanner's business, not the code's.** The same Wi-Fi code joins the
  network when scanned from Android's *add a network* screen and runs a web search when scanned from
  a home-screen search widget; a contact card offers to create a contact from inside the phone's
  Contacts app and to merge into an existing one from a general-purpose scanner. If a code seems not
  to work, scan it from the app that owns the thing it describes before suspecting the code.

For the engineering-level breakdown of exactly what's supported, see
[Markdown support](../MarkdownSupport.md).
