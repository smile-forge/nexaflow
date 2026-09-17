# Markdown Support

What the Nexaflow markdown renderer (`Nexaflow.Visuals.Text`) currently supports,
checked against the Markdig [CommonMark](https://xoofx.github.io/markdig/docs/commonmark/)
and [extensions](https://xoofx.github.io/markdig/docs/extensions/) docs.

## How it's wired

- **Parser:** Markdig **1.3.2** (`Markdig` package).
- **Pipeline:** one shared config — [`MarkdownPipelineFactory.Default`](../src/Nexaflow.Visuals.Text/Markdown/MarkdownPipelineFactory.cs).
  Every surface parses with it: the read-only `MarkdownView`, the selectable
  `SelectableMarkdownView` (via `MarkdownFlowDocument`), the AI overlay, AIChat, and
  the block editor in `Nexaflow.Features.Markdown`. There is no second pipeline.
- **Renderers:** Markdig's own HTML renderer is **not** used. Two custom WPF renderers
  walk the parsed AST:
  - [`BlockRenderer`](../src/Nexaflow.Visuals.Text/Markdown/BlockRenderer.cs) → `FrameworkElement` per block (display + editor).
  - [`MarkdownFlowDocument`](../src/Nexaflow.Visuals.Text/Markdown/MarkdownFlowDocument.cs) → a selectable `FlowDocument`; text blocks become real selectable text, everything else falls back to `BlockRenderer` wrapped in a `BlockUIContainer`.
- **Consequence:** a feature can be *parsed* by an enabled extension yet not *drawn* if
  neither renderer has a case for it. The tables below track **rendered** support, which
  is what actually matters.

### Legend

- **Status** — rendered support: ✅ full · ⚠️ partial · ❌ none.
- **Tests** — does a test exercise the parser/renderer path for *this* feature?
  ✅ direct test · ⚠️ indirect (parse-only or covered by a broad smoke test) · ❌ none.
  "Tested" means the markdown **render/parse** path; editor-model tests
  (`MarkdownBlocksTests`) and HTML-paste tests (`HtmlToMarkdownTests`) cover different
  concerns and are **not** counted here. Test files are listed under
  [Test coverage](#test-coverage).

---

## CommonMark (base spec)

The base CommonMark block/inline set is always on. Rendering status:

All inline-level rows below are covered by `BlockRendererTests` (the inline tests render a
paragraph and assert on the resulting WPF inline tree).

| Feature | Status | Tests | Notes |
|---|---|---|---|
| ATX headings (`#`…`######`) | ✅ | ✅ | `BlockRendererTests`, `MarkdownViewTests`. H1/H2 get an underline rule. |
| Setext headings (`===` / `---`) | ✅ | ✅ | `BlockRendererTests`. Parsed to the same `HeadingBlock`. |
| Paragraphs | ✅ | ✅ | `BlockRendererTests`, `MarkdownViewTests`. |
| Thematic breaks (`---`, `***`, `___`) | ✅ | ✅ | `BlockRendererTests`. |
| Block quotes | ✅ | ✅ | `BlockRendererTests`. Nested quotes render recursively. |
| Unordered lists | ✅ | ✅ | `BlockRendererTests`, `MarkdownViewTests`. |
| Ordered lists | ✅ | ✅ | `BlockRendererTests` (honours a custom start number). |
| Nested / loose lists | ✅ | ✅ | `BlockRendererTests` (nested sub-list + loose-list cases). |
| Indented code blocks | ✅ | ✅ | `BlockRendererTests`. Plain monospace. |
| Fenced code blocks | ✅ | ✅ | `BlockRendererTests`. **No syntax highlighting** — the language tag only routes diagram/math fences. |
| Inline code | ✅ | ✅ | Monospace run. |
| Emphasis (`*` / `_`) | ✅ | ✅ | Italic span. |
| Strong (`**` / `__`) | ✅ | ✅ | Bold span. |
| Inline links | ✅ | ✅ | In-app navigation hook, else OS browser. |
| Reference links | ✅ | ✅ | Resolved by the parser to the same link inline. |
| Images `![]()` | ⚠️ | ✅ | **Local files only** (absolute, `file:`, or relative to the doc's base dir). Remote `http(s)`/`data:` images are never fetched — alt text is shown instead. Both paths tested. |
| Autolinks `<https://…>` | ✅ | ✅ | `AutolinkInline` → hyperlink (incl. `mailto:` for `<user@host>`). |
| Hard line breaks | ✅ | ✅ | Two trailing spaces / backslash. |
| Soft line breaks | ✅ | ✅ | Rendered as a space. |
| Backslash escapes | ✅ | ✅ | Parser-level; suppresses emphasis. |
| Entity & numeric refs (`&amp;`, `&#9731;`) | ✅ | ✅ | `HtmlEntityInline` → decoded text. |
| Raw inline HTML (`<b>`, `<br>`, …) | ❌ | ✅ | Silently **dropped** (drop behaviour is asserted). |
| Raw HTML blocks (`<div>…`) | ❌ | ❌ | Not interpreted; shown as muted raw source text. |

> `<autolinks>` and `&entity;`/`&#nn;` references are distinct Markdig inline types
> (`AutolinkInline`, `HtmlEntityInline`), each with its own case in `BlockRenderer.AddInlines`.

---

## Markdig extensions — enabled

These are turned on in the pipeline **and** have renderer support.

All rows are tested in `MarkdownExtensionsTests` (parse-triggered + rendered-content assertions)
unless noted otherwise.

| Extension | Pipeline call | Status | Tests | Notes |
|---|---|---|---|---|
| Pipe tables | `UsePipeTables()` | ✅ | ✅ | `MarkdownExtensionsTests` (minimal, no-outer-pipes, alignment, inline formatting, CRLF, empty/escaped cells, ragged rows, in-blockquote, paragraph-interrupt) + `BlockRendererTests`. |
| Grid tables | `UseGridTables()` | ✅ | ✅ | `MarkdownExtensionsTests` (columns, `colspan`, **block-content cells**). |
| Task lists | `UseTaskLists()` | ✅ | ✅ | `[ ]` / `[x]` → ☐ / ☑ glyphs (display only, not interactive). |
| Emphasis extras | `UseEmphasisExtras()` | ✅ | ✅ | `MarkdownExtensionsTests`. `~~strike~~` (strikethrough), `~sub~`, `^super^`, `==mark==` (highlight wash, `Marked` palette token), `++ins++` (underline). All map to `EmphasisInline` distinguished by `DelimiterChar`/`DelimiterCount` in `BlockRenderer.AddInlines`. |
| Auto links | `UseAutoLinks()` | ✅ | ✅ | Bare `https://…` / `www.` URLs become links. |
| Definition lists | `UseDefinitionLists()` | ✅ | ✅ | Term + definition styling. |
| List extras | `UseListExtras()` | ✅ | ✅ | `a.`/`A.` alphabetic and `i.`/`I.` roman ordered markers. |
| Abbreviations | `UseAbbreviations()` | ✅ | ✅ | `MarkdownExtensionsTests`. `*[HTML]: HyperText…` defines an abbreviation; each occurrence renders dotted-underlined with the definition as a hover tooltip. The definition line itself is consumed (not shown). |
| Alert blocks | `UseAlertBlocks()` | ✅ | ✅ | `MarkdownExtensionsTests` + `extensions.md` sample render. GitHub callouts `> [!NOTE]` / `[!TIP]` / `[!IMPORTANT]` / `[!WARNING]` / `[!CAUTION]` → coloured left-border callout with a bold kind label. Each kind maps to a semantic accent (`Accent`/`Success`/`Important`/`Warning`/`Danger` palette tokens). `AlertBlock` extends `QuoteBlock`, so it's matched before the generic quote case. The selectable path renders alerts **natively** (a styled `Section`, mirroring the quote path) so callout text is drag-selectable. |
| YAML front matter | `UseYamlFrontMatter()` | ✅ (stripped) | ✅ | `MarkdownExtensionsTests`. A leading `--- … ---` metadata block is parsed as a `YamlFrontMatterBlock` and **not rendered** (matches Markdig's HTML renderer). Both paths suppress it — block renderer returns a collapsed placeholder, the selectable path emits nothing. |
| Figures | `UseFigures()` | ✅ | ✅ | `^^^` figure block + caption. |
| Footers | `UseFooters()` | ✅ | ✅ | `^^ footer`. |
| Citations | `UseCitations()` | ✅ | ✅ | `""text""` → raised, coloured citation text. **Delimiter is a doubled double-quote, not `^^`** (see note below). |
| Mathematics | `UseMathematics()` | ✅ | ✅ | Block `$$…$$` (`MarkdownPipelineFactoryTests` + `BlockRendererTests`) and inline `$…$` (`MarkdownExtensionsTests`). Rendered with **WpfMath** (LaTeX); falls back to the LaTeX source if unparseable. |
| Diagrams | `UseDiagrams()` | ✅ (custom) | ✅ | `MarkdownPipelineFactoryTests`, `BlockRendererTests`, `MarkdownSampleRenderTests`. Rendering is **fully custom** (see below). |
| Musical notation | `UseMusicNotation()` (custom) | ✅ (custom) | ✅ | `MusicBlockParserTests`, `AbcBuilderTests`, `LilyPondBuilderTests`, `EngravingRulesTests`, `MusicRendererTests`, `MusicSampleDocTests`, `MarkdownSampleRenderTests`. Fenced `abc` / `lilypond` blocks and the repo's own `#% … #%` block extension → engraved sheet music (see below). |

> **Citation delimiter.** `UseCitations()` emits `""text""` with `DelimiterChar == '"'`, so the
> citation delimiter the renderer matches is a doubled double-quote (`""…""`), not `^^`.

> **Grid-table block cells.** Both renderers render every child block of a cell, so a cell holding a
> list or multiple paragraphs renders fully; a single-paragraph cell takes a styled/aligned fast-path.
> Covered by tests in both `BlockRenderer` and `MarkdownFlowDocument`.

> Note: in `MarkdownFlowDocument` (the selectable path), definition lists, figures, footers,
> math and diagrams are rendered via the `BlockRenderer` UIElement fallback, so they display
> correctly but their text is **not drag-selectable**. Headings, paragraphs, lists, code, quotes,
> tables and **alert blocks** are fully selectable (alerts render as a native styled `Section`).
> Music blocks are a special case: not text-selectable, but **interactively selectable** — the
> embedded score owns its own click/drag (a note, a beamed group or a run, see Musical Notation
> below); both `InlineMarkdownEditor` and `SelectableMarkdownView` locate the score under the
> mouse with a geometric visual hit-test (the text container's event-source attribution over
> embedded UIElement islands is unreliable) and drive it directly. Making diagram label text
> selectable is tracked as backlog (`product:diagram-text-selection`).

---

## Diagrams — sub-support

Diagram fences are intercepted by [`DiagramRenderer`](../src/Nexaflow.Visuals.Text/Markdown/DiagramRenderer.cs)
and drawn natively in WPF (no JS/Mermaid.js, no browser).

**Languages:**

| Language | Status | Tests |
|---|---|---|
| `nomnoml` | ✅ | ❌ — no test or sample fixture |
| `mermaid` | ⚠️ Partial | ✅ — see sub-types below |
| `qr` | ✅ | ✅ — see [QR codes](#qr-codes--sub-support) below |
| `barcode` | ✅ | ✅ — see [Barcodes](#barcodes--sub-support) below |
| `datamatrix` | ✅ | ✅ — see [Data Matrix](#data-matrix--sub-support) below |
| `pdf417` | ✅ | ✅ — see [PDF417](#pdf417--sub-support) below |
| `aztec` | ✅ | ✅ — see [Aztec Code](#aztec-code--sub-support) below |
| `smiles` | ✅ | ✅ — chemical structures on the shared syntax tree; see [Chemical structures](#chemical-structures--sub-support) below |
| `abc` | ✅ | ✅ — ABC music on the shared syntax tree; see [Musical Notation](#musical-notation--sub-support) below |

**Mermaid sub-types** ([`MermaidDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/MermaidDiagramHandler.cs)):

| Sub-type | Status | Tests |
|---|---|---|
| `graph` / `flowchart` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests` — shapes, arrows, edge ids, chains `A-->B-->C`, fan-out `A-->B & C`, nested subgraphs) + sample render |
| `pie` | ✅ (shared layout tree; donut, legend positions, highlight) | ✅ grammar (`PieGrammarTests`) + chart + config (`PieChartTests`) + draw (`PieBuilderTests`) + routing (`DiagramRendererTests`) + sample render. See sub-features below. |
| `quadrantChart` | ✅ (shared layout tree; styled points and classes, written in place) | ✅ grammar (`QuadrantGrammarTests`) + points, styles + config (`QuadrantChartTests`) + draw (`QuadrantBuilderTests`) + writing (`QuadrantEditingTests`) + sample render. See sub-features below. |
| `sequenceDiagram` | ✅ | ✅ parser (`DiagramParsersTests`, extensive) + render (`DiagramRendererTests`) + sample |
| `gantt` | ✅ | ✅ parser (`DiagramParsersTests`) + sample render |
| `gitGraph` | ✅ | ✅ parser (`DiagramParsersTests`) + sample render |
| `mindmap` | ✅ | ✅ parser (`DiagramParsersTests`) + sample render |
| `stateDiagram` / `stateDiagram-v2` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `classDiagram` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `requirementDiagram` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `kanban` | ✅ (column/card layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `xychart` / `xychart-beta` | ✅ (shared layout tree; bar + line, both orientations, written in place) | ✅ grammar (`XyGrammarTests`) + axes, series + config (`XyChartTests`) + draw (`XyBuilderTests`) + writing (`XyEditingTests`) + sample render. See sub-features below. |
| `radar-beta` | ✅ (shared layout tree; polar plot, written in place) | ✅ grammar (`RadarGrammarTests`) + curves, options + config (`RadarChartTests`) + draw (`RadarBuilderTests`) + writing (`RadarEditingTests`) + sample render. See sub-features below. |
| `ishikawa` / `ishikawa-beta` | ✅ (fishbone) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `sankey` | ✅ (flow diagram) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `erDiagram` | ✅ (graph layout) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `venn-beta` | ✅ (shared layout tree; circles by area, written in place) | ✅ grammar (`VennGrammarTests`) + regions, styles + config (`VennDiagramTests`) + draw (`VennBuilderTests`) + writing (`VennEditingTests`) + sample render. See sub-features below. |
| `architecture-beta` | ✅ (grid layout, icon glyphs) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `swimlane-beta` | ✅ (lane bands) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `cynefin-beta` | ✅ (five-domain grid) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `timeline` | ✅ (period spine, LR or TD) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `journey` | ✅ (scored faces, actor legend) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `C4Context` / `C4Container` / `C4Component` / `C4Dynamic` / `C4Deployment` | ✅ (graph layout, C4-PlantUML macro set) | ✅ parser + projection (`C4ParserTests`, `C4ProjectionTests`) + card/palette (`C4ElementTests`) + render + sample render. See sub-features below. |
| `C4Sequence` *(Nexaflow extension)* | ✅ (shared sequence renderer) | ✅ projection (`C4SequenceProjectionTests`) + render + sample render. See sub-features below. |
| `block-beta` | ✅ (author-placed grid, nested blocks) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |

**Pie sub-features** ([`PieGrammar`](../src/Nexaflow.Markdown/Mermaid/Pie/PieGrammar.cs) →
[`PieChart`](../src/Nexaflow.Markdown/Mermaid/Pie/PieChart.cs) →
[`PieBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Mermaid/Pie/PieBuilder.cs)).
A pie is the first diagram drawn on the **shared layout tree** rather than by a renderer of its own, so it is selectable
and its values are the numbers themselves. Supported: `pie`, `showData`, a `title` after the keyword or on a line of its
own, `"label" : value` slices in clockwise order, `%%` comments and `accTitle`/`accDescr`. A value that is not a number
greater than nought keeps its slice and carries the reason on the number; a line that is no slice is held as written
with the reason, and a pie with nothing worth drawing is shown as its own source — unless it is being written in.
**Written in place:** a label or value deleted to nothing leaves a hole rather than an error, Enter anywhere on a slice
starts a new slice under it with holes for its label and value (Tab moves between them), Backspace in a slice nothing
has been written on takes it back, Backspace and Delete stop at the edges of a label, value or title rather than
taking the quote, colon or line end past them, Up and Down move between legend rows and between the title and the
legend, Ctrl+Z takes an edit back with the pie still drawn, and the pointer is a bar only over labels, values, the
title and holes. A quote typed into a label is written `#quot;` and drawn as a quote, and Shift+click and Ctrl+click
choose as they do in any diagram on the layout tree.
**The front matter is applied** — `config: pie:` `textPosition` (0.75), `donutHole` (0, to 0.9), `legendPosition`
(`right`/`left`/`top`/`bottom`/`center`) and `highlightSlice` (a label, or `hover`), and the `themeVariables`
`pie1`…`pie12`, `pieStrokeWidth` (the gap between slices), `pieOuterStrokeColor`/`pieOuterStrokeWidth`, `pieOpacity`,
the section/legend text sizes and colours, and `pieTitleTextColor`. A size or colour nobody wrote is the theme's.
Each wedge **stands in its own shape**, so a press means the slice it lands in rather than whichever bounding box was
asked first, and the gaps between slices are real gaps of even width. A wedge, the share written on it and its legend
row all point at the same slice, so choosing one highlights all three. **Limitations:** `hover` highlighting needs a
pointer the layout does not yet report, so it picks out nothing; and a value is typed into only where the host makes
the block editable.

**State-diagram sub-features** ([`MermaidStateParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidStateParser.cs)).
State diagrams reuse the shared graph model, the Sugiyama layout and `WpfGraphRenderer` (pseudostate
shapes: a filled **start** dot, a ringed **end** dot, a fork/join **bar**; a choice is a diamond; a note
is a dashed amber callout; composite boxes get a tinted **header band**). Supported: states + descriptions
(`state "d" as id`, `id : d`), transitions with labels, `[*]` start/end, **arbitrarily-nested** composite
states (`state X { … }`, laid out as boxes-within-boxes via `Subgraph.ParentId`), choice/fork/join, notes
(single- and multi-line), `direction`, comments, and styling (`classDef` / `class` / inline `:::`).
Antiparallel transition pairs (`A --> B` / `B --> A`) are bowed apart so both arrows show. **Limitations:**
concurrency `--` dividers are not drawn (regions just stack), and `[*]` is one shared start + one shared
end per scope.

**Class-diagram sub-features** ([`MermaidClassParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidClassParser.cs)).
Class diagrams reuse the shared graph model, the Sugiyama layout and `WpfGraphRenderer`. Each class is a
**`ClassBox`** node ([`ClassBox.cs`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/ClassBox.cs)) drawn as a
UML box with name / attribute / method compartments; relationships are edges whose
`EdgeArrow` heads (`TriangleHollow`/`DiamondFilled`/`DiamondHollow`) and `Edge.StartLabel`/`EndLabel`
multiplicities draw the UML markers. Supported: classes (`class A`, block `class A { … }`, label override
`class A["Pretty"]`, generics `A~T~` → `A<T>` incl. nested `List~List~int~~` → `List<List<int>>`, implicit
declaration from a member/relationship); members via block lines or the `A : +member` shorthand, with
visibility (`+ - # ~`), attribute-vs-method by `()`, a method return type shown after a colon
(`getId() int` → `getId() : int`), classifiers `*` (abstract → *italic*) / `$` (static → underline);
annotations `<<interface>>`/`<<enumeration>>`/… → «stereotype»; the full relationship set (`<|--` inheritance,
`*--` composition, `o--` aggregation, `-->` association, `--`/`..` links, `..>` dependency, `..|>` realization)
in either direction **including two-way forms** (`<|--|>`, `<-->`), with multiplicity (`A "1" --> "*" B`) and a
`: label`; lollipop interfaces (`A --() iface`, `iface ()-- A`) drawn as a small circle on a short straight stub
off the class box with the name beside it (a decoration on the class, reserved by the layout — not a routed node);
`namespace N { … }`
with **hierarchical (dotted) nesting** (`namespace A.B.C` nests `C` inside `B` inside `A`); notes (`note "…"`,
`note for A "…"`, with `<br>` / `\n` line breaks); `direction`; comments; and styling (`classDef`, `cssClass`,
`style A fill:…`, inline `A:::name`). A front-matter / `title:` is centred over the diagram. Multiplicity survives
`namespace` nesting — the clustered layout rebuilds each edge per level, and until recently that rebuild dropped the end
labels, so `Order "1" --> "*" LineItem` lost its `1` and `*` as soon as either class sat in a namespace. **Limitations:**
`hideEmptyMembersBox` and interactive `callback`/`link` directives are ignored.

**Requirement-diagram sub-features** ([`MermaidRequirementParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidRequirementParser.cs)).
A requirement / element is structurally a UML box, so it reuses the **`ClassBox`** node, the Sugiyama layout and
`WpfGraphRenderer` — a «type» stereotype + name header over a *single* field compartment (the
`ClassInfo.SingleCompartment` flag suppresses the class box's second/methods compartment). Supported: every
requirement type (`requirement`, `functionalRequirement`, `interfaceRequirement`, `performanceRequirement`,
`physicalRequirement`, `designConstraint`) and `element`, each with `id` / `text` / `risk` / `verifymethod` /
`type` / `docref` fields (keys shown as `Id`/`Text`/`Risk`/`Verification`/`Type`/`Doc Ref`, enum values
title-cased, `functionalRequirement` → «Functional Requirement»); relationships `src - type -> dst` and the
reverse `dst <- type - src` labelled «type» — `contains` draws as a solid line with the SysML composite
crosshair (⊕) at the container end, the others (`copies`, `derives`, `satisfies`, `verifies`, `refines`,
`traces`) as dashed open arrows; `direction`; comments; and styling (`style`, `classDef`, `class a,b name`,
inline `:::name`). **Limitation:** single-line `req name { … }` blocks aren't parsed — the opening brace must
end the line (the standard multi-line form).

**Kanban-board sub-features** ([`MermaidKanbanParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidKanbanParser.cs)
+ [`WpfKanbanRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfKanbanRenderer.cs)).
A kanban board has its own [`KanbanBoard`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/KanbanBoard.cs)
model and a panel-based renderer (native WPF layout, not the measured canvas the graph/chart renderers use,
so multi-line card text and chip rows wrap for free): columns lay out left-to-right (horizontally scrollable),
each a header (title + card count) over a vertical stack of cards. Hierarchy is **indentation-based** —
columns sit at the shallowest indent (taken as the minimum across the board), cards are indented beneath their
column. A node is `id[Title]`, `[Title]` (id defaults to the title) or bare `Title`; cards may carry a trailing
`@{ key: value, … }` metadata block (attached with or without a leading space) whose keys are `ticket`,
`assigned` and `priority` (`Very High` / `High` / `Low` / `Very Low`, quoted or not). Each column takes a
categorical `Swatch.*`/`Series` colour; a card shows its text, ticket/assignee chips, and a left stripe + label
coloured by priority (Very High → `Danger`, High → `Warning`, Low → `Accent`, Very Low → `TextMuted`).
Comments (`%%`) and `<br>` line breaks are handled. **Limitation:** the `ticketBaseUrl` config (which would turn
a `ticket` into a hyperlink) is parsed away with the rest of the front-matter `config:` block — like every Mermaid
diagram that does not read it, `config:` is recognised but not applied, so the ticket renders as a plain chip.

**Quadrant-chart sub-features** ([`QuadrantGrammar`](../src/Nexaflow.Markdown/Mermaid/Quadrant/QuadrantGrammar.cs) →
its stage [`ResolveClasses`](../src/Nexaflow.Markdown/Mermaid/Quadrant/Stages/ResolveClasses.cs) →
[`QuadrantChart`](../src/Nexaflow.Markdown/Mermaid/Quadrant/QuadrantChart.cs) →
[`QuadrantBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Mermaid/Quadrant/QuadrantBuilder.cs)).
Drawn on the **shared layout tree**. Supported, as Mermaid documents it: `quadrantChart`; a `title`; `x-axis Low --> High`
and `y-axis Low --> High`, the high end optional and either end in quotes; `quadrant-1`…`quadrant-4` captions (the first top
right, then anticlockwise); points as `Name: [x, y]` from 0 to 1, a class after `:::` and a style after the position —
`radius`, `color`, `stroke-color`, `stroke-width` — laid over the class's; and `classDef` lines, written above the points
or below. Captions sit in the middle of their quadrants, or at the top where there are points; the x-axis's ends go over
the chart where there are no points and under it where there are. A quadrant stands for its caption's line, a dot for its
point. What Mermaid would refuse is said beneath — a position outside 0 to 1 or not two numbers, a class no `classDef`
writes, a style key nobody knows. **The front matter is applied** ([`QuadrantConfig`](../src/Nexaflow.Markdown/Mermaid/Quadrant/QuadrantConfig.cs)):
`config: quadrantChart:` `chartWidth`, `chartHeight`, `titleFontSize`, `quadrantLabelFontSize`, `x`/`yAxisLabelFontSize`,
`pointLabelFontSize`, `pointRadius`, `xAxisPosition`, `yAxisPosition` and the border widths; and `themeVariables`
`quadrant1Fill`…`quadrant4Fill`, `quadrant1TextFill`…`quadrant4TextFill`, `quadrantPointFill`, `quadrantPointTextFill`, the
axis text fills, the border fills and `quadrantTitleFill`. Mermaid's paddings are not applied.
**Written in place:** a caption, an axis's end and a point's name are typed into where drawn; a colon, quote or bracket
typed into bare text puts it in quotes; a class renamed in its `classDef` is renamed in the points taking it; Enter on a
caption starts the next quadrant's and elsewhere a point with its name still to write.

**XY-chart sub-features** ([`XyGrammar`](../src/Nexaflow.Markdown/Mermaid/Xy/XyGrammar.cs) →
[`XyChart`](../src/Nexaflow.Markdown/Mermaid/Xy/XyChart.cs) →
[`XyBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Mermaid/Xy/XyBuilder.cs)).
Drawn on the **shared layout tree**, so what is drawn is selectable and each category, title and name is the characters it
was written as. Supported, as Mermaid documents it: `xychart` / `xychart-beta`, **vertical** (default) or **`horizontal`**;
a `title`; a categorical x-axis (`x-axis "Month" [jan, "feb 2"]`) or a numeric range (`x-axis title 0 --> 100`); a y-axis
with a title and a range (`y-axis "Revenue" 4000 --> 11000`), either left out — the range then the values' own, from
nought where there are bars, widened to round numbers; `bar` and `line` series, named or not (a named series has a
legend row), bars side by side where several share a slot and lines over them; labels on line points
(`line [540 "PaLM", 7]`); signed and leading-point values (`+1.3`, `.6`, `-.34`); and `%%` comments. A bar stands for its
value and a line for its series; over a numeric x-axis the values are spread from the range's start to its end. What
Mermaid would refuse is held with the reason beneath — categories on a y-axis, a value that is no number, a list never closed.
**The front matter is applied** ([`XyConfig`](../src/Nexaflow.Markdown/Mermaid/Xy/XyConfig.cs)): `config: xyChart:`
`width`, `height`, `showTitle`, `titleFontSize`, `showLegend`, `legendFontSize`, `legendPadding`, `chartOrientation`,
`showDataLabel`, `showDataLabelOutsideBar`, `useMaxWidth`, and each axis's `xAxis:`/`yAxis:` `showLabel`,
`labelFontSize`, `showTitle`, `titleFontSize`, `showTick`, `tickLength`, `showAxisLine` and `axisLineWidth`; and
`themeVariables: xyChart:` `backgroundColor`, `titleColor`, `dataLabelColor`, `legendTextColor`, the eight
`xAxis…Color`/`yAxis…Color` keys and `plotColorPalette`. A size nobody wrote is the app's own, and a colour the theme's.
**Not applied:** `labelRotation` and axis titles turned upright (words on the layout tree are set level — an upright
axis's title sits over it), and `plotReservedSpacePercent`, `titlePadding`, `labelPadding` and
`tickWidth`, which size Mermaid's own layout.
**Written in place:** a category is typed into under its tick, an axis's title where it is drawn, and a series' name in
its legend row; a word given a space or a bracket is put in quotes; a category deleted to nothing leaves a hole; Enter
on a series starts another of its kind with its values still to write.**Radar-chart sub-features** ([`RadarGrammar`](../src/Nexaflow.Markdown/Mermaid/Radar/RadarGrammar.cs) →
its stage [`ResolveCurves`](../src/Nexaflow.Markdown/Mermaid/Radar/Stages/ResolveCurves.cs) →
[`RadarChart`](../src/Nexaflow.Markdown/Mermaid/Radar/RadarChart.cs) →
[`RadarBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Mermaid/Radar/RadarBuilder.cs)).
Drawn on the **shared layout tree**, so what is drawn is selectable and each label is the characters it was written as.
Supported, as Mermaid documents it: `radar-beta` (with or without a colon); a `title`, on its own line or in the front
matter; `axis` lines naming axes as bare ids (`axis A, B, C`) or `id["Label"]`, several to a line; `curve` lines naming
curves with their values in braces, several to a line — **positional** (`curve c["Label"]{1, 2, 3}`, one per axis in the
order the axes are written, wherever they are written) or **keyed** (`curve c{ axisId: value, … }`); the options `min`
(0), `max` (the greatest value where none is written), `ticks` (5 rings), `graticule circle|polygon` and `showLegend`,
several to a line with a comma between; and `%%` comments. The first axis points up and the rest follow clockwise; a curve
reaches along each axis as far as its value is from `min` to `max`, rounded by `curveTension` over circles and straight
over a polygon, and the legend sits right of the chart, or under it where the room is too narrow. Beyond Mermaid, a name
may be written in quotes and a label without them. What Mermaid would refuse is drawn as far as it goes with the reason
beneath — a curve not giving each axis one value, a value naming no axis or mixing the two kinds, an axis written twice,
values never closed, an option set to something it cannot be. A curve's values written across several lines are not read.
**The front matter is applied** ([`RadarConfig`](../src/Nexaflow.Markdown/Mermaid/Radar/RadarConfig.cs)): `config: radar:`
`width`, `height` (the radius is half the smaller), `margin*`, `axisScaleFactor`, `axisLabelFactor`, `curveTension` and
`useMaxWidth`; `config: themeVariables: radar:` `axisColor`, `axisStrokeWidth`, `axisLabelFontSize`, `curveOpacity`,
`curveStrokeWidth`, `graticuleColor`, `graticuleOpacity`, `graticuleStrokeWidth`, `legendBoxSize` and `legendFontSize`;
and `themeVariables` `titleColor`, `fontSize` and `cScale0`…`cScale11`. A size nobody wrote is the app's own, and a colour
the theme's.
**Written in place:** an axis's label (or its name) is typed into at the end of its spoke, and a curve's in its legend
row; an axis renamed is renamed in every value naming it, bare or in quotes; Enter on a curve starts another curve with a
hole for its name, on an axis another axis, and on an option nothing; a label deleted to nothing leaves a hole.
**Ishikawa-chart sub-features** ([`MermaidIshikawaParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidIshikawaParser.cs)
+ [`WpfIshikawaRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfIshikawaRenderer.cs)).
A fishbone / cause-and-effect chart has its own [`IshikawaDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/IshikawaDiagram.cs)
model — an effect (the head) and a tree of cause categories. It is **indentation-structured** like a mindmap, not a
node/edge graph: `ishikawa-beta` (alias `ishikawa`); the **first content line is the effect** (the fish head); every
later line is a cause attached to the nearest shallower line by relative leading-whitespace depth (indent width is
flexible — 2 or 4 spaces both work — and nesting is arbitrarily deep). Rendered as a horizontal spine pointing into the
head box, with the categories as diagonal bones alternating above/below the spine (each a `Series`-coloured chip) and
their nested causes listed as an indented outline. **The front-matter `config:` block is applied**
([`IshikawaConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/IshikawaConfigParser.cs) →
[`IshikawaConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/IshikawaConfig.cs)) — the whole documented surface
is `config: ishikawa` `diagramPadding` and `useMaxWidth`. A front-matter `title:` (Ishikawa has no inline title keyword)
renders above the diagram. **Limitation:** Mermaid exposes no colour/size theme options for ishikawa yet, so bone
colours come from the palette.

**Sankey sub-features** ([`MermaidSankeyParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidSankeyParser.cs)
+ [`WpfSankeyRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfSankeyRenderer.cs)).
A flow diagram with its own [`SankeyDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/SankeyDiagram.cs)
model — nodes inferred from links. After the `sankey` keyword the body is **RFC-4180 CSV**: three columns
`source,target,value`, one link per row; fields with commas are double-quoted and a literal quote is a doubled `""`;
blank lines and `%%` comments are skipped. Laid out left→right by longest-path depth (adjusted by `nodeAlignment`),
nodes sized by throughput and joined by bezier ribbons whose width is the value. **The front-matter `config:` block is
applied** ([`SankeyConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/SankeyConfigParser.cs) →
[`SankeyConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/SankeyConfig.cs)): `config: sankey`
`width`/`height`, `linkColor` (`source`/`target`/`gradient`/a fixed colour), `nodeAlignment` (`justify`/`center`/`left`/
`right`), `showValues` + `prefix`/`suffix`, `nodeWidth`/`nodePadding`, `labelStyle` (`legacy`/`outlined`), and the
`nodeColors` map (per-node colour overrides). Node/link colours otherwise come from the palette's series bank. A
front-matter `title:` (Sankey has no inline title keyword) renders above the diagram. **Limitation:** newlines inside
a quoted CSV field (a record spanning lines) aren't supported — each row is one line.

**ER-diagram sub-features** ([`MermaidErParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidErParser.cs)).
An entity is structurally a UML box, so ER reuses the shared graph model + Sugiyama layout + `WpfGraphRenderer`
(like class / requirement diagrams): each entity is a single-compartment `ClassBox`, each relationship an edge with
**crow's-foot cardinality** markers at both ends (new `EdgeArrow.Er*` heads — a min indicator: bar = one, circle =
zero; plus a max indicator: bar = one, fork = many) and a solid (identifying `--`) or dashed (non-identifying `..`)
line. Supported: entities — bare `NAME`, quoted `"name with space"`, or aliased `id[Alias]` / `id["Multi word"]` —
with an optional `{ type name [keys] ["comment"] }` block (keys `PK`/`FK`/`UK` comma-separated, optional-type `?`,
array/parameterised types `string[]` / `string(99)`); relationships in **both** the symbol form (`||--o{`, `}o..o{`,
even with no surrounding spaces) and the **word-alias** form (`one to zero or more`, `many(0) optionally to 0+`),
with `--`/`to` identifying vs `..`/`optionally to` non-identifying; a `: label`; `direction`; and styling (`style`,
`classDef` incl. a `default` class, `class`, inline `:::`). **The front-matter `config:` block is applied**
([`ErConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/ErConfigParser.cs) →
[`ErConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/ErConfig.cs)): `config: er` `layoutDirection` (when
the body has no `direction`) and explicit `fill`/`stroke` colours; the remaining spacing keys (`minEntityWidth`,
`nodeSpacing`, `fontSize`, …) are parsed but the shared layout uses its own metrics. **Limitations:** `subgraph … end`
grouping is flattened (entities still render, ungrouped), and entity-name markdown isn't rendered (shown as plain text).

**Venn sub-features** ([`VennGrammar`](../src/Nexaflow.Markdown/Mermaid/Venn/VennGrammar.cs) →
its stages [`GroupRegions`](../src/Nexaflow.Markdown/Mermaid/Venn/Stages/GroupRegions.cs) and [`ResolveRegions`](../src/Nexaflow.Markdown/Mermaid/Venn/Stages/ResolveRegions.cs) →
[`VennDiagram`](../src/Nexaflow.Markdown/Mermaid/Venn/VennDiagram.cs) →
[`VennBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Mermaid/Venn/VennBuilder.cs)).
Drawn on the **shared layout tree**, so what is drawn is selectable and each label is the characters it was written as.
Supported, as Mermaid documents it: `venn-beta`; a `title`, quoted or not; `set id["Label"]:size`, a name bare or in
quotes and a label as `["Label"]` or `[Label]`; `union A,B["Label"]:size` over two sets or more, each written above it;
`text` items indented under a set or a union, or written at the start of a line naming their region first
(`text A,B AB1["OpenAPI"]`), called by a word, a number or words in quotes; `style` of a set, a union (`style A,B`) or
an item, setting `fill`, `color`, `stroke`, `stroke-width` and `fill-opacity`; and `%%` comments on a line of their own
or closing one. **Areas mean sizes:** a circle's area is its set's size — ten where none is written — and two circles
overlap by their union's — ten over the square of the count where none is — placed as Mermaid's venn.js places them; a
union of three sets or more implies an overlap of a quarter of the smaller set for each pair it covers, so it has a
region to sit in, while a size written for what three sets or more share is fitted along with every pair's, as venn.js
fits it; sets nothing says overlap stand apart. A region's words sit where it has most room: a set with no
label shows its name, a union with none the names of its sets, and items stack under the label, spreading into columns
only where a column would run out of the region. What Mermaid would refuse is drawn where it plainly belongs with the
reason beneath — a union naming a set not written above it, an item with no region or indented against the rule, a style
naming nothing or setting something no style sets, a size that is no number.
**The front matter is applied** ([`VennConfig`](../src/Nexaflow.Markdown/Mermaid/Venn/VennConfig.cs)): `config: venn:`
`width`, `height`, `padding` (15), `useMaxWidth` and `useDebugLayout` (a cross at each circle's centre and a box round
each region's words), and the `themeVariables` `venn1`…`venn8`, `vennTitleTextColor` and `vennSetTextColor`. A width
or height nobody wrote is the app's own, and a colour the theme's.
**Written in place:** a label is typed into where it is drawn; Enter on a set or a union starts an item in it and on an
item another in the same region, with a hole for its name — a new set anywhere else; a label deleted to nothing leaves
a hole; and Backspace in an item nothing has been written in takes it back. A character a place cannot hold is escaped as it is
typed: a bare name or a bracketed label is put in quotes, and a quote in quotes is written `#quot;`, which is drawn as a
quote. A set or an item renamed where it is declared is renamed wherever it is used — in the unions, the items naming
their region and the styles — unless another set or item already has the name. Shift+click chooses from the caret to the
press, and Ctrl+click adds a region, a label or an item to what is chosen. Pressing a circle chooses its set and the items
in it, and pressing where a union's circles overlap chooses the union. **Limitations:** a diagram is drawn in the app's
theme, so Mermaid's `redux-color` theme, `neo` look and hand-drawn look are not.

**Architecture sub-features** ([`MermaidArchitectureParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidArchitectureParser.cs)
+ [`WpfArchitectureRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfArchitectureRenderer.cs)).
An architecture diagram has its own [`ArchitectureDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/ArchitectureDiagram.cs)
model — groups, the services/junctions inside them, and side-anchored edges — drawn by a **dedicated grid renderer**
(not the Sugiyama pipeline): services are placed on a grid seeded from the edges' side hints (`A:R -- L:B` puts B to
the right of A), groups draw as boxes around their members, and edges anchor to the declared `T`/`B`/`L`/`R` side.
Supported: `group id(icon)[Title]` with nesting (`in parent`); `service id(icon)[Title] in group`; `junction`;
edges `id{group}?:SIDE {<}?--{>}? SIDE:id{group}?` (all four arrow forms, cross-group `{group}` endpoints); and
`align row`/`align column`. The five default icons (cloud/database/disk/internet/server) render as **built-in vector
glyphs**; unknown/custom `pack:name` icons fall back to a caption. **The front-matter `config:` block is applied**
([`ArchitectureConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/ArchitectureConfigParser.cs) →
[`ArchitectureConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/ArchitectureConfig.cs)): `nodeSeparation`
tunes cell spacing; the physics keys (`randomize`/`seed`/`idealEdgeLengthMultiplier`) are parsed but the grid layout
is deterministic. **Limitations:** placement is a deterministic grid heuristic, not Mermaid's force-directed engine,
so complex graphs may lay out differently; edges route as straight side-to-side lines.

**Swimlane sub-features** ([`MermaidSwimlaneParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidSwimlaneParser.cs)
+ [`WpfSwimlaneRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfSwimlaneRenderer.cs)).
Swimlane syntax is flowchart syntax where every **top-level `subgraph` is a lane**, so the parser rewrites the
`swimlane-beta [DIR]` header to a `flowchart [DIR]` header and reuses `MermaidFlowchartParser` for the full grammar; the
dedicated renderer draws each lane as a band (horizontal bands for `TB`/`BT`, vertical columns for `LR`/`RL`) with
its nodes flowing along the lane and edges (including cross-lane ones) drawn between node centres. Supported:
direction (`TB`/`TD`/`BT`/`LR`/`RL`); lanes via top-level `subgraph id[Label] … end`; flowchart node shapes
(`[rect]`, `(round)`, `([stadium])`, `{decision}`, `((circle))`); flowchart edges (`-->`, `---`, `-->|label|`,
`-.->`, `==>`); and `accTitle`/`accDescr` (dropped as accessibility metadata). **Limitations:** each lane lays its
nodes out in a single row/column in declaration order (no per-lane Sugiyama), so long lanes scroll rather than wrap.

**Cynefin sub-features** ([`MermaidCynefinParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidCynefinParser.cs)
+ [`WpfCynefinRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfCynefinRenderer.cs)).
A Cynefin diagram has its own [`CynefinDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/CynefinDiagram.cs)
model — the five fixed domains, the items in each, and the transitions between them — drawn as a **fixed 2×2 grid**
(Complex top-left, Complicated top-right, Chaotic bottom-left, Clear bottom-right) with `confusion` as a central
ellipse. Supported: `cynefin-beta`; optional `title`; the five domain keywords with indented quoted `"item"` lines
(unknown keywords are not domains); the confusion centre shows up to three items with a **`+N more`** overflow badge;
and transitions `domainA --> domainB : "label"` as labelled arrows. **The front-matter `config:` block is applied**
([`CynefinConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/CynefinConfigParser.cs) →
[`CynefinConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/CynefinConfig.cs)): `config: cynefin`
`width`/`height`/`padding`/`showDomainDescriptions`, and the `themeVariables: cynefin` domain backgrounds
(`complexBg`/`complicatedBg`/`clearBg`/`chaoticBg`/`confusionBg`/`boundaryColor`, else the palette's series bank).

**Timeline sub-features** ([`MermaidTimelineParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidTimelineParser.cs)
+ [`WpfTimelineRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfTimelineRenderer.cs)).
A timeline has its own [`TimelineDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/TimelineDiagram.cs) model —
sections of periods, each period carrying its events — drawn as boxes on a **spine** (a row for `LR`, a column for
`direction TD`) with the events stacked away from it and a tinted band over each section. Supported: `title`;
`section`; `period : event : event` with further events on continuation lines starting with `:`; `<br>` line breaks;
`#colon;` for a literal colon; `direction LR|TD` (also `timeline TD`); and `accTitle`/`accDescr` (dropped). Colour
follows Mermaid's rule — with sections every period in a section shares the section's slot, without them each period
takes the next one. **The front-matter `config:` block is applied**
([`TimelineConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/TimelineConfigParser.cs) →
[`TimelineConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/TimelineConfig.cs)): `config: timeline`
`disableMulticolor` (everything on slot 0) and `padding`, and the `themeVariables` slots `cScale0…11` / `cScaleLabel0…11`
(kept by index, else the palette's series bank). **Limitations:** every colon splits, exactly as in Mermaid, so a
colon inside an event needs `#colon;`; labels wrap at a fixed column width rather than Mermaid's `useMaxWidth` fit.

**Journey sub-features** ([`MermaidJourneyParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidJourneyParser.cs)
+ [`WpfJourneyRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfJourneyRenderer.cs)).
A user journey has its own [`JourneyDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/JourneyDiagram.cs) model —
sections of scored tasks and the actors on each, with the distinct actor list (first-appearance order) as the legend.
Tasks run left to right under a band per section; above each task a **face** floats in a score lane (higher for a better
score, drawn from vector strokes — no emoji font), coloured by mood from the palette's success/warning/danger tokens:
5–4 smile, 3 flat, 2–1 frown. Each actor gets a colour, shown in a legend and as a dot on every task they take part in.
Supported: `title`; `section`; `Task name: score: actor, actor` (a missing score reads as 3, one outside 1–5 is
clamped, a task may have no actors, tasks before any `section` land in an unnamed one); `%%` comments; and
`accTitle`/`accDescr` (dropped). **The front-matter `config:` block is applied**
([`JourneyConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/JourneyConfigParser.cs) →
[`JourneyConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/JourneyConfig.cs)): `config: journey`
`width`/`height`/`boxMargin`/`taskFontSize`, the colour lists `actorColours`/`sectionFills` (flow or block YAML lists), and
the `themeVariables` `fillType0…7` section palette (else the palette's series bank). **Limitations:** actors are matched by
exact name; `useMaxWidth` and `leftMargin`/`rightMargin` are ignored.

**Block sub-features** ([`MermaidBlockParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidBlockParser.cs)
+ [`WpfBlockRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfBlockRenderer.cs)).
A block diagram has its own [`BlockDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/BlockDiagram.cs) model — a
root group of items (nodes, spaces, block arrows, nested groups) plus edges — drawn by a **grid renderer, not a layout
engine**: the author's placement is what renders. Each group wraps its items into rows by its `columns` count (auto = one
row), a column is as wide as its widest item, a spanning item widens the columns it covers, and a nested group is measured
first and then stretched to the cell it lands in. Supported: `block-beta` / `block` headers; `columns N`; `id:N` widths;
`space` / `space:N`; `block:id:N … end` and anonymous `block … end` with their own `columns`; every flowchart bracket shape
(`()`, `([])`, `[[]]`, `[()]`, `(())`, `((()))`, `>]`, `{}`, `{{}}`, `[//]`, `[\\]`, `[/\]`, `[\/]`), reusing the shared
`NodeShape` vocabulary; block arrows `id<["label"]>(dir)` for `right`/`left`/`up`/`down`/`x`/`y` and comma-combined
directions (unioned into one glyph); edges `-->` / `---` / `-- "label" -->` between any two items by id, including groups,
with inline shapes on either end; `<br>` and HTML entities in labels; `%%` comments; and styling via `style`, `classDef`
+ `class` (`fill`, `stroke`, `stroke-width`, `color`, `stroke-dasharray`), applied whether the line precedes or follows the
item. **The front-matter `config:` block is applied**
([`BlockConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/BlockConfigParser.cs) →
[`BlockConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/BlockConfig.cs)): `config: block` `padding`.
**Limitations:** edges are straight centre-to-centre lines (Mermaid routes around blocks); `useMaxWidth` is ignored (the
canvas is sized to its content and scrolls); a label containing `--` outside quotes reads as an edge.

**C4 sub-features** ([`MermaidC4Parser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidC4Parser.cs)
+ [`C4GraphProjector`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/C4GraphProjector.cs)
+ [`C4ElementPainter`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/C4ElementPainter.cs)).
A C4 diagram is a node-and-edge graph with richer boxes, so it is **projected onto the shared graph pipeline** — the same
Sugiyama layout, `WpfGraphRenderer`, viewport, panning, selection and expandable nodes as a flowchart — rather than
getting a layout engine of its own. Elements become `NodeShape.C4Element` nodes carrying a
[`C4ElementInfo`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/C4Element.cs) card (bold title, a `[Kind: technology]`
stereotype, a wrapped description), boundaries and deployment nodes become nested styled subgraph boxes with a `[type]`
line under their title, and relationships become edges whose second label line is the `[technology]`.

**The body is C4-PlantUML's macro set, not Mermaid's subset** — Mermaid supports a fraction of what people write, the two
agree wherever Mermaid has an opinion, so accepting the larger language rejects far less. Supported: the headers
`C4Context` / `C4Container` / `C4Component` / `C4Dynamic` / `C4Deployment`; `Person`, `System`, `Container`, `Component`
with every `_Ext`, `Db` and `Queue` variant; `Boundary`, `Enterprise_Boundary`, `System_Boundary`, `Container_Boundary`
and `Deployment_Node`/`Node`/`Node_L`/`Node_R`, nested by braces or closed with `Boundary_End()`; `Rel` with its
`_U`/`_D`/`_L`/`_R`/`_Neighbor`/`_Back`/`BiRel` variants and `RelIndex`; `$techn`, `$descr`, `$tags`, `$link` and
`$index=Index()`/`LastIndex()`/`SetIndex()`/`increment()`; `UpdateElementStyle` (by element *type* as C4-PlantUML writes
it **or** by *alias* as Mermaid does), `AddElementTag`/`AddBoundaryTag`/`AddRelTag` with `UpdateRelStyle` and
`UpdateBoundaryStyle` — a relationship's `$lineColor`/`$textColor`/`$lineStyle` colour its edge in both the structural and the sequence renderers; `SHOW_LEGEND($hideStereotype, $details)` — which, as in C4-PlantUML, **hides the stereotypes by default**, since the legend then carries what they said, and whose `$details` (`None()`/`Small()`/`Normal()`) sizes the legend rows; `HIDE_STEREOTYPE`, `LAYOUT_TOP_DOWN`/`LAYOUT_LEFT_RIGHT`/`LAYOUT_LANDSCAPE`, the
`SHOW_PERSON_OUTLINE`/`SHOW_PERSON_PORTRAIT` shape variants; `<br/>` and HTML entities in labels; and both `%%` and
PlantUML `'` comments. **Colours map C4's scheme onto the theme** rather than copying its hex — what carries the meaning
is the grading (depth tracks abstraction level, grey means external), so it is reproduced from the active accent and a
theme can retune it (see [theming.md](theming.md) → *Diagram tokens*). **The front-matter `config:` block is applied**
([`C4ConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/C4ConfigParser.cs) →
[`C4Config`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/C4Diagram.cs)): `config: c4` `wrap`, `width`, `height`,
with `c4ShapeInRow`/`c4BoundaryInRow` recorded but not obeyed. **Limitations:** `Lay_*`, the `_U`/`_D`/`_L`/`_R`
direction suffixes, `$sprite` and `UpdateRelStyle`'s pixel offsets are parsed and ignored — they exist to nudge
graphviz, and placement here belongs to the shared layout; `SvgGraphRenderer` draws a C4 element as a plain rectangle
(it has no geometry for the person and cylinder outlines) though it does write all three of the card's text rows.

**C4 sequence** ([`C4SequenceProjector`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/C4SequenceProjector.cs)).
`C4Sequence` is a **Nexaflow extension** — it mirrors C4-PlantUML's `C4_Sequence.puml`, which Mermaid has no keyword
for. It is drawn by the *same* [`WpfSequenceDiagramRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfSequenceDiagramRenderer.cs)
as a native `sequenceDiagram`: element macros become participant lifelines whose heads are C4 element cards instead of
plain boxes, a `Boundary`…`Boundary_End()` pair becomes the `box` grouping over the participants it spans, and each
`Rel` becomes a message carrying its `[technology]` under the label. `SHOW_INDEX()` numbers the messages (an explicit
`$index`/`RelIndex` wins and the count continues from it), `SHOW_FOOT_BOXES(false)` drops the repeated heads at the
bottom, and `SHOW_ELEMENT_DESCRIPTIONS()` puts each element's description into its card — hidden by default, because a
lifeline head is a column header and a paragraph in every column only pushes the columns apart.

`SHOW_LEGEND()` works here too, drawn below the timeline by the same painter and built from the same rows as a
structural diagram's.

**Native sequence syntax works inside it.** Any line the C4 reader does not claim is replayed through
`MermaidSequenceParser.ParseLine`, so `alt`/`else`/`end`, `loop`, `par`, `critical`, `note over`, `activate` and even a
plain `participant` sit alongside C4 macros in one diagram and nest around them correctly — it is the native grammar
itself, not a second copy of it.

### Expandable nodes + the viewport (graph-family diagrams)

`graph`/`flowchart`, `stateDiagram`, `classDiagram`, `erDiagram` and `requirementDiagram` share the graph model,
the Sugiyama layout and [`WpfGraphRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfGraphRenderer.cs),
so they also share two things that only matter once a graph gets big.

**A node can hide a subtree.** `Node.Expansion` is `Leaf` / `Collapsed` / `Expanded`, and a non-leaf node is drawn
with a **`[+]` / `[−]` chip** on its top-right corner — a *second* hit region, so the node's body keeps its own
`click` target and expansion doesn't have to be smuggled into the label or the href. Which nodes those are is
declared in a **`config: nexaflow:`** front-matter block, namespaced so it can never collide with a real mermaid
key and so stock mermaid simply ignores it
([`NexaflowConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/NexaflowConfigParser.cs) →
[`NexaflowGraphConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/NexaflowGraphConfig.cs)):

```yaml
---
config:
  nexaflow:
    expandDepth: 2          # auto-open this many levels from the roots; deeper nodes get a [+]
    maxFanOut: 24           # more siblings than this fold behind one "+N more" chip (0 = off)
    collapsed: [n3, n7]     # ids owning a hidden subtree — or a keyed block, below
    expanded:
      n0: app.exe           # id → the producer's own name, echoed back on the expand request
---
```

[`GraphExpansion`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Layout/GraphExpansion.cs) derives the *visible*
graph from the parsed one plus that config plus whatever the reader has since opened — the parsed graph is never
mutated, so re-laying it out is idempotent. A diagram that says nothing about expansion gets no chips and renders
exactly as before. Clicking a chip goes to the host first (`SelectableMarkdownView.DiagramExpand` → a
`DiagramExpandRequest`); a host that *generated* the diagram claims it and re-emits with more walked (the PE
inspector's import tree), and if nobody claims it the diagram opens the node itself from its own source.

**Layout.** The layout counts crossings and keeps the best ordering (barycenter ⊕ median ⊕ adjacent transposition),
then pulls each node toward the median of its neighbours so a child sits under its parent. It also respects the
width it has, in two ways: a layer too wide for the space wraps onto further rows rather than becoming one endless
line, and a label long enough to set the width of its whole layer is capped and wrapped instead
([`NodeLabelMetrics`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/NodeLabelMetrics.cs), shared with the renderer so
the two agree about where text sits). The cap is derived from the space available and only ever binds on the labels
that caused the overflow — so a diagram that already fits is untouched, and one that didn't spends height, where
the room actually is, instead of growing sideways. The width laid out for is the view's **actual** width, not a
per-diagram constant.

**Viewport.** A graph diagram sits on a
[`PanZoomSurface`](../src/Nexaflow.Visuals.Common/Layout/PanZoomSurface.cs) — drag to pan, Ctrl+wheel or the
`−`/`+`/`Fit`/`1:1` chips to zoom, and an overview minimap (of the node boxes, not just the bounding box) that
appears once part of the diagram is off-screen. **Always**, not only once the diagram happens to overflow: a
gesture that comes and goes with the size of the content is one nobody can learn, and "it fits" is only true until
the next node is opened. The one exception is a surface that set `FitContentToWidth` (the inline editor), which
keeps scaling the diagram down to its column — panning inside an already-scaled picture would fight both the
scaling and text selection.

**Selecting.** Clicking a node selects it: the node and every edge touching it are drawn in the selection colour
and lifted above the rest, which is what makes one line followable across a dense diagram. Two host options tune
what else a click does, both off by default:

| Option (on `SelectableMarkdownView`) | Effect |
|---|---|
| `DiagramOpenOnDoubleClick` | A single click only selects; the node's link opens on double-click. For a pane where opening costs something the user may not have meant — the PE inspector spawns a whole tab. |
| `DiagramZoomOnWheel` | A plain wheel zooms the diagram instead of scrolling the page past it. Only for a pane whose whole content is the diagram; in a flowing document it would trap the wheel. |

Both reach the diagram through `IInteractiveBlock` (`PointerDoubleClick`, `WantsPointerWheel`), because the host
intercepts mouse input on the way down — a block that is never asked never sees a double-click or a wheel event
at all, and the chrome of a surface inside a text container would otherwise need a second click to reach.

`PanZoomSurface` lives in `Visuals.Common` beside the `PanZoomMiniMap` arithmetic it drives, because the scratchpad
corkboard and the image collage each hand-rolled the same WPF half — transforms, drag, minimap redraw, zoom
buttons — around that shared arithmetic. It is the half that was missing.

A Mermaid block is read by [`MermaidParser`](../src/Nexaflow.Markdown/Mermaid/MermaidParser.cs) into a lossless tree
of what every diagram type shares — `--- … ---` front-matter (title/config), `%%` comments, `%%{ … }%%` directives
(read as written; none is obeyed), the header naming the diagram, and `accTitle`/`accDescr` — and
[`MermaidBlock`](../src/Nexaflow.Markdown/Mermaid/MermaidBlock.cs) hands each diagram the body past the front matter,
the front-matter title (applied to a chart that has none of its own) and the config between the fences
([docs/markdown-ast.md](markdown-ast.md#mermaid)). Tested by `MermaidParserTests`, `MermaidBlockTests` and
`DiagramRendererTests.Frontmatter_PieRoutesToChartNotSourceText`. The `config:` block is discarded for every diagram
**except `xychart`, `radar-beta`, `ishikawa-beta`, `sankey`, `erDiagram`, `venn-beta`, `architecture-beta` and
`cynefin-beta`** (which read `MermaidBlock.Config` and apply the `config:` options described above) **and the
`config: nexaflow:` block**, which every graph-family diagram reads. A header naming no diagram type shows the block
as written, with a wave under the header's word and the reason beneath
([`UnknownDiagramBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Mermaid/UnknownDiagramBuilder.cs), tested by
`MermaidBuilderTests`).
A document-level YAML front-matter block is handled separately (`UseYamlFrontMatter`, parsed but
not rendered — see the extensions table above); this Mermaid front-matter is a different, fence-local
mechanism.

---

## QR codes — sub-support

A **`qr`** fence ([syntax](https://markdown.org/tools/diagrams/qr/)) generates a QR symbol. It is not a
diagram, but it arrives the same way — a fenced block rendered to an element in place of its source — so
it is registered as an [`IDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/QrDiagramHandler.cs)
and reaches both markdown surfaces through the one dispatcher.

The body is a flat `key: value` list; the key is everything before the first colon, so a URL on the right
needs no quoting. **Unrecognised keys are refused, not ignored** — a mistyped `cellsize` that silently did
nothing would render a plausible-looking code that is not the one the author asked for.

**Five pieces**, parser to builder:

| Piece | Does |
|---|---|
| [`MatrixParser`](../src/Nexaflow.Markdown/Matrix/MatrixParser.cs) | lines → a lossless tree of fields, shared by every 2D code; a line that is not a field is held with the reason |
| [`QrBlockReader`](../src/Nexaflow.Visuals.Text/Markdown/Qr/QrBlockReader.cs) | the tree → a `QrBlock` (payload + settings), or a message saying which line is wrong |
| [`QrPayload`](../src/Nexaflow.Visuals.Text/Markdown/Qr/QrPayload.cs) | `type:` + fields → the one string that gets encoded, in the convention its scanners expect |
| [`QrEncoder`](../src/Nexaflow.Visuals.Text/Markdown/Qr/QrEncoder.cs) | string → a `QrMatrix`. WPF-free, so a symbol can be asserted on without a UI thread |
| [`QrBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Qr/QrBuilder.cs) | reads, encodes and lays the symbol out: its three finders, its timing lines and its modules, on a quiet-zone ground |

**The encoder is ours** (ISO/IEC 18004 model 2), not a package: versions 1–40, all four error-correction
levels, numeric / alphanumeric / byte modes with the narrowest one chosen for the payload, Reed–Solomon
parity, block interleaving, and all eight masks scored by the penalty rules. The whole job is arithmetic
over a byte array — no IO, no platform, nothing to keep current.

| `type:` | Fields | Encodes as |
|---|---|---|
| `text` | `text` | the text verbatim |
| `url` | `url` | the URL; a missing scheme becomes `https://` |
| `email` | `email`, `subject`, `body` | `mailto:` with a percent-encoded query |
| `phone` | `phone` | `tel:`, spacing and punctuation stripped |
| `sms` | `number`, `message` | `SMSTO:number:message` (the form both mobile OSes act on) |
| `wifi` | `ssid`, `password`, `security`, `hidden` | `WIFI:T:…;S:…;P:…;;` — `;` `,` `:` `"` `\` escaped; no password ⇒ `nopass`; `H:true;` only when hidden |
| `vcard` | `name`, `org`, `title`, `phone`, `email`, `url`, `address` | vCard 3.0, CRLF-delimited, with a structured `N:` split on the last space |
| `mecard` | `name`, `phone`, `email`, `url`, `address`, `note` | DENSO Wave's one-line `MECARD:…;;` — fewer fields, smaller symbol; `\` `;` `:` `,` escaped, and `N:last,first` joined on a raw comma |
| `geo` | `lat`, `lng` | `geo:lat,lng`, range-checked |
| `event` | `title`, `location`, `start`, `end` | a `VCALENDAR`-wrapped `VEVENT` — the form every calendar app takes, where a bare `VEVENT` is read only by some — with `DTSTART`/`DTEND` in basic format; a trailing `Z` is kept as UTC |
| `epc` | `name`, `iban`, `bic`, `amount`, `purpose`, `reference`, `message` | EPC069-12 (GiroCode): twelve LF-separated elements, version `002` so the BIC stays optional, trailing empties dropped, 331-byte cap. The IBAN is validated mod-97; `reference` and `message` are mutually exclusive |
| `crypto` | `coin`, `address`, `amount` | BIP-21 `coin:address?amount=…`; tickers (`BTC`, `ETH`, …) resolve to the scheme |

**Settings** (any type): `ec` (`L`/`M`/`Q`/`H`, default `M`), `cellSize` (1–64, default 4), `margin`
(0–32 modules, default 4), `dark` and `light` (`#RGB`, `#RRGGBB` or `#AARRGGBB`).

**Colour is the one thing the palette does not follow.** `MarkdownPalette.QrDark` / `QrLight` are their own
pair rather than `Text` over `FigureBg`: a code that inverted with a dark theme would stop scanning. A theme
can retune them (`QrDarkBrush` / `QrLightBrush`); a block's `dark:` / `light:` win over both.

**Testing it.** A round trip is the only thing that can say a symbol is right, so
[`QrTestDecoder`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Qr/QrTestDecoder.cs) is a scanner
without the camera: it reads the format information, undoes the mask, walks the zigzag, de-interleaves the
blocks, checks every Reed–Solomon syndrome and hands back the string. Where it can it takes a *different*
route to the same answer — log/antilog tables against the encoder's shift-multiply, polynomial evaluation
against its long division — because two copies of one mistake agree with each other.
`RoundTrip_GrowsThroughEveryVersion` walks a payload up until the symbol reaches version 40, so every
version and every row of the block tables is built and read back. The tables themselves are pinned from
outside, on published figures: byte-mode capacities at versions 1/2/10/40 × L/M/Q/H, the alignment-pattern
centres (including version 32, the one that breaks the spacing rule), and the Table C.1 format code words.

---

## Barcodes — sub-support

A **`barcode`** fence ([syntax](https://markdown.org/tools/diagrams/barcode/)) generates a linear
barcode. Like `qr` it is not a diagram but arrives as one, so it is registered as an
[`IDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/BarcodeDiagramHandler.cs)
and reaches both markdown surfaces through the one dispatcher.

The body is a flat `key: value` list, and **unrecognised keys are refused rather than ignored** — the
same reasoning as `qr`.

**Twenty-three formats, all encoded here**, in
[`Markdown/Barcode/Encoders`](../src/Nexaflow.Visuals.Text/Markdown/Barcode/Encoders):

| Encoder | Formats |
|---|---|
| `Code128Encoder` | `CODE128` (auto subset switching), `CODE128A`, `CODE128B`, `CODE128C` |
| `EanEncoder` | `EAN13`, `EAN8`, `EAN5`, `EAN2`, `UPC`/`UPCA`, `UPCE` |
| `WidthEncoders` | `CODE39`, `ITF`, `ITF14`, `MSI` ×4, `PHARMACODE`, `CODABAR` |
| `PublicationEncoder` | `ISBN`, `ISSN`, `ISMN` — each works out the thirteen digits its number stands for and hands them to `EanEncoder`, plus an optional add-on set apart by a 12-module gap |

Every symbology reduces to the same thing — a row of equal-width modules, each ink or paper — so
[`BarcodePattern`](../src/Nexaflow.Visuals.Text/Markdown/Barcode/BarcodePattern.cs) carries all of them
and the renderer never learns what an EAN is.

**The parser does not encode.** That split is the whole design:

- A **structural** fault (unknown key, no such format, a width that isn't a number) means the block
  cannot be understood, and it falls back to its source with the reason — `DiagramRenderer.ErrorElement`.
- A value the format **cannot carry** is not structural. The block is well formed and the value is the
  part being edited, so it must keep rendering: a valid sample value's bars are drawn faint, struck
  through, with a red wave under the value and the reason on hover. A value is invalid for every
  keystroke but the last while an EAN-13 is being typed.

**Human-readable layout is a property of the format, not of the encoding**, so it is worked out in one
place — [`BarcodeTextLayout`](../src/Nexaflow.Visuals.Text/Markdown/Barcode/BarcodeTextLayout.cs) —
from the symbology and the encoded text, rather than threaded back through each encoder. It returns
the text broken into `BarcodeTextRun`s (each with the modules it sits over and whether it goes below,
outside, or above the bars), the guard runs that drop past the digits, and a caption. Everything
outside the retail family returns nothing and is centred underneath.

This matters more than it sounds: an EAN printed as one centred string reads as the wrong barcode even
when every module is right, which is exactly what the reference images caught.

**Editing.** `BarcodeElement` implements
[`IEditableBlock`](../src/Nexaflow.Visuals.Text/Editing/IEditableBlock.cs), so the caret crosses into
it, selects, types and leaves through the same host code that drives a formula — see
[`InlineMarkdownEditor.Blocks.cs`](../src/Nexaflow.Visuals.Text/Markdown/InlineMarkdownEditor.Blocks.cs).
It contributes a **layout tree** ([`BarcodeLayout`](../src/Nexaflow.Visuals.Text/Markdown/Barcode/BarcodeLayout.cs)),
built from a parse tree of the symbol's text
([`BarcodePart`](../src/Nexaflow.Visuals.Text/Markdown/Barcode/BarcodePart.cs)) — so the shared queries
answer where the caret can stand and what a press landed on, exactly as they do for a formula.

**Only the characters somebody typed carry a part.** These formats do not print what they are given:
Codabar brackets the value in a start and a stop mark, an EAN-13 works out a thirteenth digit, a UPC-E
fills in both ends, an ISBN takes the hyphens out. Each printed run is cut against the window where the
value appears verbatim, so it becomes `EncodedText`, then a `Character` per typed character, then
`EncodedText` again. A layout node gets a part only where the piece is a `Character`; the guard patterns
and the bars get none at all. A piece with no part is drawn and is not selectable — which is why the
caret is never offered inside a check digit, and why an ISBN takes it in the caption (the number as it
was written) and not under the bars.

`BarcodeBlock.ValueStart` is relative to the fence's **content**, while the editing host splices into
the whole block — `DiagramRenderOptions.SourceOffset` carries the difference, set by `BlockRenderer`,
which is the only place that holds both strings.

**Settings**: `width` (0.5–20, default 2 — the width of one *bar*, not of the symbol), `height`
(4–1000, default 100), `displayValue` (default true), `fontSize` (4–200, default 20), `textAlign`
(`left`/`center`/`right`), `lineColor`, `background` (`#RGB`, `#RRGGBB`, `#AARRGGBB`), `margin`
(0–200, default 10).

**Colour follows the same rule as QR**: `MarkdownPalette.BarcodeDark` / `BarcodeLight` are their own
pair (`BarcodeDarkBrush` / `BarcodeLightBrush` to retune), because bars that inverted with a dark theme
would stop scanning. A block's `lineColor:` / `background:` win over both.

**Testing it.** The encoders are checked two ways. `BarcodeEncoderTests` pins the module strings against
the published tables and check-digit rules; `BarcodeReferenceImageTests` reads **externally generated
PNGs** back to modules and compares — point `NEXAFLOW_BARCODE_IMAGES` at a folder of
`barcode_<value>_<format>.png` files. That second one earned its keep immediately: it found four real
faults (Code 39 and Codabar drawing gaps as bars, pharmacode's one-module gap, and Code 128 not
switching subsets), and it is what showed the retail formats were printing their digits wrong.

19 of 20 reference images match at module level. The three publication images do not, and the cause is
the harness rather than the encoder: their readings are off by a module and parse as no valid EAN
symbol code, while ours decode correctly against the digits printed on those same images (the ISSN's
`0111011` then `0010001` are the L- and G-codes for 7, matching body 9770311175001).

---

## 2D symbols — the shared layer

QR was the first matrix symbology and everything about its block that was not the QR encoder turned out
to be the same for the next three: a rectangular grid of modules drawn as merged runs on a quiet-zone
ground, a flat `key: value` body, `cellSize` / `margin` / `dark` / `light`, and Reed–Solomon parity over
some Galois field. Those live in [`Markdown/Matrix`](../src/Nexaflow.Visuals.Text/Markdown/Matrix), and
the parser in [`Nexaflow.Markdown/Matrix`](../src/Nexaflow.Markdown/Matrix).

They render the way formulas and scores do — [docs/markdown-ast.md](markdown-ast.md). `MatrixParser`
reads the fence into a tree, and the tree goes straight to that code's builder: there is nothing in a
symbol to edit, so no pipeline stage sits between them. Each code is its own builder, because each reads
its own fields, encodes its own way and is made of its own parts; what they share is `MatrixBuilder`. The
layout is hosted read-only in the shared `ContentElement`, which the caret arrows over like a word.

| Piece | Does |
|---|---|
| [`IModuleMatrix`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/IModuleMatrix.cs) | any finished symbol: `Width`, `Height`, and whether a module is dark. Rectangular, because Data Matrix is |
| [`MatrixParser`](../src/Nexaflow.Markdown/Matrix/MatrixParser.cs) | any 2D block's body → a tree: a line per line, a field its key, colon and value. `Print(Parse(s)) == s` for anything |
| [`MatrixSettings`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/MatrixSettings.cs) + [`MatrixBlockReader`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/MatrixBlockReader.cs) | the drawing settings every 2D block takes, and the fields the parser found; a symbology's reader takes the fields these hand back and adds its own keys |
| [`MatrixBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/MatrixBuilder.cs) | what the four builders share: the symbol laid as a layout tree of the parts it is made of, each part's module runs merged into one geometry, aliased edges so no seam reads as a light line, and the quiet-zone ground. A block that will not read or encode draws a valid symbol of its kind, faint and struck through, with the reason beneath. Takes a row-height multiplier for stacked symbologies |
| [`GaloisField` + `ReedSolomon`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/ReedSolomon.cs) | parity over a field chosen per symbology — `0x11D` for QR, `0x12D` for Data Matrix, the prime field 929 for PDF417, and one of `0x13`/`0x43`/`0x12D`/`0x409`/`0x1069` for Aztec by symbol size — with the generator's first root a parameter, because the standards disagree about it and getting it wrong is silent |

The QR code is re-pointed at all of it; its suite is what proves the shared codec. `QrColor` folded into
`HexColor`. The palette's `QrDark` / `QrLight` pair is what every 2D symbol draws in — a theme retuning
it retunes them all, which is the intent: they are "scannable dark on light", not "QR".

---

## Data Matrix — sub-support

A **`datamatrix`** fence generates an ECC 200 symbol. Registered as an
[`IDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/DataMatrixDiagramHandler.cs)
beside `qr` for the same reason.

**The encoder is ours** (ISO/IEC 16022), in
[`DataMatrixEncoder`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/DataMatrix/DataMatrixEncoder.cs):
the thirty-row size table; ASCII encodation with digit pairs and the upper shift; C40 with all three
shift sets and every end-of-data rule the standard defines; ECI 26 for text outside ASCII; FNC1 and
Macro 05/06; the 253-state pad; interleaved Reed–Solomon under `0x12D` from the first root; and Annex
F's placement walk with its four corner cases. Both encodations are tried and the shorter wins — a
reader decodes either, so choosing is a matter of size only.

**Payloads** ([`DataMatrixPayload`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/DataMatrix/DataMatrixPayload.cs))
are the `qr` vocabulary plus four that are message formats rather than text conventions:

| `type:` | Wire form |
|---|---|
| `gs1` | FNC1, then AIs and values with the brackets off; GS after each variable-length element that is followed by another. Fixed-length AIs (00, 01, 02, 11–17, 20, 410–417) are length-checked |
| `ppn` | Macro 06 around `9N` + PPN, `1T` + lot, `D` + expiry, `S` + serial, GS-separated. PPN = `11` + PZN + two check digits (ASCII values weighted 2–11, mod 97). The PZN's check (weights 1–7, mod 11) is verified first |
| `ntin` | FNC1, `01` + GTIN-14 (`0 4150` + PZN + mod-10 check), `17` + expiry, `10` + lot, `21` + serial |
| `mailmark` | the message verbatim, upper-case alphanumerics and spaces, exactly the length the format defines, in the size it mandates: 7 → 51 in 24×24, 9 → 90 in 32×32, 29 → 70 in 16×48 |

*A note on Mailmark:* the block guarantees length, character set and symbol size. The field layout
inside the message — country, format, version, class, supply-chain and item ids, postcode, service
type, customer content — is Royal Mail's barcode specification's, and their induction systems validate
it; the block does not.

**Testing it.** [`DataMatrixTestDecoder`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/DataMatrixTestDecoder.cs)
reads a symbol back: finder patterns checked, mapping unframed, codewords read, every Reed–Solomon
syndrome evaluated at its root (evaluation, not the encoder's division — two copies of one mistake agree
with each other), and ASCII/C40 decoded with the implicit-unlatch ending. The placement walk is the
one thing it shares with the encoder, being a transcription of the annex with no second derivation to
take; it is pinned instead by invariants over all thirty sizes (every bit of every codeword placed
exactly once, only the fixed corner left) and by the standard's own worked example — `123456` is
`142 164 186` with parity `114 25 5 88 102` in a 10×10 — which pins field, generator, first root and
digit pairing in one figure.

---

## PDF417 — sub-support

A **`pdf417`** fence generates a PDF417 symbol (ISO/IEC 15438), registered as an
[`IDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/Pdf417DiagramHandler.cs)
beside `qr` and `datamatrix`. It is the first stacked symbology here, and the first user of
`MatrixBuilder`'s row-height multiplier.

**The encoder is ours** ([`Pdf417Encoder`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/Pdf417/Pdf417Encoder.cs)):
text compaction with all four sub-modes and their latches and shifts, numeric compaction in 44-digit
groups through `BigInteger`, byte compaction five-to-six, the symbol-length descriptor, Reed–Solomon
over GF(929), the row indicators, and the layout.

**The symbol-character table is not.** It cannot be derived — every legal character is 17 modules of
four bars and four spaces each one to six wide, but that admits 1,484 characters in cluster 0 where the
standard uses 929, and the ones it picks follow no computable order (six orderings were tried against
known codeword→pattern pairs; all scored zero). So the 3 × 929 table is ingested from Uzi Granot's
PDF417 Barcode Encoder under CPOL 1.02 — see `ThirdPartyNotices.md` — and held packed 15 bits per
character, the leading bar and trailing space being implicit.

**It is verified, not trusted**, three ways:

- `Pdf417EncoderTests.EveryTableEntryIsALegalSymbolCharacter` checks all 2,787 entries against the
  standard's structural rules — 17 modules, eight elements of width 1–6, in the cluster they are filed
  under.
- `MatchesAnotherGeneratorsCodewordsForTheSameText` pins the data codewords for `nexaflow` to
  `[823, 143, 5, 344, 689]`, taken off a symbol this code had no hand in making.
- [`Pdf417ReferenceImageTests`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/Pdf417ReferenceImageTests.cs)
  decodes `*_PDF417.png` files from `NEXAFLOW_BARCODE_IMAGES` end to end.

**A latent bug this found.** `ReedSolomon.Generator` built ∏(x + gⁱ) rather than ∏(x − gⁱ) — written
the way every GF(2ⁿ) implementation writes it, multiply-and-XOR, which is correct only because −1 ≡ 1
in a binary field. QR and Data Matrix were unaffected and always had been; over GF(929) it produced
parity that was perfectly self-consistent and that no real scanner would accept. Only an external
reference could have caught it, which is the argument for keeping those images in the loop.

**Testing it.** [`Pdf417TestDecoder`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/Pdf417TestDecoder.cs)
reads a symbol back: the table reversed, the row indicators cross-checked against the shape they claim
(and against each other — the three clusters each carry part of it, so a disagreement is caught rather
than averaged), every Reed–Solomon syndrome evaluated at its root rather than re-divided, and the
compaction decoded. The rendered picture is rasterised and read back too.

---

## Aztec Code — sub-support

An **`aztec`** fence generates an Aztec Code (ISO/IEC 24778), registered as an
[`IDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/AztecDiagramHandler.cs) beside
`qr`, `datamatrix` and `pdf417`. Both families are supported: **compact** (11-module core, 1–4 layers,
15×15 to 27×27) and the **full range** (15-module core, 1–32 layers, 19×19 to 151×151, with a reference
grid through the larger sizes). `format:` picks one; left alone the encoder takes compact while the
message fits, because at any given side length a compact symbol carries more.

**The high-level encoder is a shortest path, not a scan**
([`AztecHighLevelEncoder`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/Aztec/AztecHighLevelEncoder.cs)).
Every character can be reached several ways — latch into the set it lives in, shift into it for one
character, or drop a run into a byte shift — and which is cheapest depends on what follows. So it is a
dynamic program over (position, character set in force). A greedy encoder fails this invisibly: the
symbol it makes still scans, it is just bigger than it needed to be. `DigitRunsCostLessThanLetterRuns`
and `ALoneCapitalIsShiftedIntoRatherThanLatched` are the two tests that would catch that. The latch
table itself is derived by Floyd–Warshall over the ten direct latches rather than written out, because
the transitive routes are where a hand-copied table is wrong (`Digit → Lower` goes through `Upper`, and
the cheap way there is not the obvious one).

**Bit stuffing.** A codeword whose leading *width − 1* bits are all equal gets a forced complementary
bit, so no codeword can be all ones or all zeros — a run that wide would read as reference grid rather
than as data. `StuffingLeavesNoUniformCodeword` runs 800 run-heavy bit streams through all four widths.

**The geometry was derived from reference images, and is verified against them**
([`AztecLayout`](../src/Nexaflow.Visuals.Text/Markdown/Matrix/Aztec/AztecLayout.cs)). This matters more
here than for the other symbologies: an encoder and a decoder that share a placement walk agree with
each other whether or not the walk is right, so a round trip proves nothing about the layout. The
orientation marks (three dark modules then two, one and none, clockwise from the top left), the
mode-message ring, the data spiral's direction and starting corner, the first Reed–Solomon root, and the
leading pad bits were all settled by comparing against symbols from two other generators —
[`AztecReferenceImageTests`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/AztecReferenceImageTests.cs)
keeps that check in the suite, and asserts equality module for module, not merely that the text decodes.

**Two closed forms replace two tables.** The symbol size is `(compact ? 11 : 14) + 4·layers`, plus, for
the full range, the reference-grid lines (`+ 1 + 2·((base/2 − 1) / 15)`); the capacity is
`((compact ? 88 : 112) + 16·layers) · layers`, which is just the area of the ring the layers occupy.
Both are asserted against the standard's published tables — all 32 full-range sizes and all 32
codeword counts — rather than against what the code computes, because a formula that is right for the
sizes you happened to try is the failure mode here.

**Still to build — wanted, not declined:** **Aztec Runes**, **reader-initialisation symbols**, and
**structured append** (`aztec-runes`, `aztec-reader-init`, `aztec-structured-append`).

The first two are *blocked* rather than deferred, and the distinction matters because it says what would
unblock them: both are defined in the standard's annexes, whose bit-level detail is not public, and there
is no reference symbol to validate an implementation against — so an attempt cannot be told apart from a
symbol that looks right and carries nothing. What moves them is a reference symbol or the annex text, not
another go at the encoder. Structured append is a different shape: it is one payload across several
symbols, so what it needs first is a **block syntax for a symbol set**, which is our question rather than
the standard's.

> An earlier version of this section read as a design decision — "not implemented", with the reasons
> arranged as justification. They were obstacles, not choices, and writing them up that way hid three
> wanted features from every list that asks what is left to do.

---

## Chemical structures — sub-support

A **`smiles`** fence draws molecules from SMILES strings, in the format described at
[markdown.org](https://markdown.org/tools/diagrams/chemistry/): a molecule per line, an optional caption in
double quotes after it, `#` comments, and the `chemistry` keyword, which may open the block and may be
left out. It is registered as an
[`IDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/SmilesDiagramHandler.cs), and is the
fourth language on the shared syntax tree ([markdown-ast.md](markdown-ast.md#smiles)).

| Piece | What it does |
|---|---|
| [`SmilesParser`](../src/Nexaflow.Markdown/Chemistry/SmilesParser.cs) | the block and each molecule into a lossless tree, to the character: bracket atoms (isotope, symbol, chirality, hydrogens, charge, class), organic-subset and aromatic atoms, the seven bond symbols, branches, ring closures (`1`, `%12`) and dots. What will not read is held with the reason |
| [`SmilesPipeline`](../src/Nexaflow.Markdown/Chemistry/SmilesPipeline.cs) | three stages: `ConnectAtoms` pairs ring closures; `CountHydrogens` fills unbracketed atoms up to their valence and flags atoms with too many bonds; `Kekulize` gives aromatic rings alternating double bonds by maximum matching |
| [`Elements`](../src/Nexaflow.Markdown/Chemistry/Elements.cs) | the periodic table, RDKit's valence lists, and a charge moving an atom along its row (N⁺ bonds like carbon) |
| [`Molecule`](../src/Nexaflow.Markdown/Chemistry/Molecule.cs) + [`MoleculeRings`](../src/Nexaflow.Markdown/Chemistry/MoleculeRings.cs) | the graph read off the stages' answers; ring bonds, and the smallest set of smallest rings |
| [`StructureLayout`](../src/Nexaflow.Markdown/Chemistry/Depiction/StructureLayout.cs) | 2D coordinates: ring systems as polygons edge on edge, chains grown as zig-zags with `/` `\` honoured, overlaps untangled across single bonds, the result straightened, and a wedge per `@`/`@@` centre |
| [`CageLayout`](../src/Nexaflow.Markdown/Chemistry/Depiction/CageLayout.cs) | a cage drawn as the solid it is: built in 3D from its bonds and angles (classical scaling, then stress majorization), seen from whichever of four hundred directions `Readability` scores clearest — its substituents included — and kept only where that reads better than the flat drawing |
| [`SmilesBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Chemistry/SmilesBuilder.cs) | the drawing: carbon as a corner, other elements as symbols with their hydrogens away from the bonds, charges and mass numbers, ring double bonds inside the ring, bonds coloured half and half by `MarkdownPalette.Elements`, wedges, captions, and entries flowing to the column |

**Read-only, but selectable.** Every atom and every written bond is a piece carrying the part it was
typed as, so a selection across a structure copies its SMILES and trouble is waved under the atom that
caused it — a carbon with five bonds, an aromatic ring that cannot alternate — with the reason in red
beneath the caption. A string with no atom that reads is shown as itself, struck through.

**Held to RDKit.** Hydrogen counts and what is refused follow RDKit, which is what most SMILES is written
for. Nitrogen makes three bonds, except that a nitro group written `N(=O)=O` is accepted, as RDKit's
clean-up accepts it; a neutral four-bonded nitrogen is flagged, with the charged form offered.
[`SmilesCorpusTests`](../src/Nexaflow.Tests/Nexaflow.Tests.Markdown/Chemistry/SmilesCorpusTests.cs) runs
the reader and the layout over SmilesDB's 5,481 molecules against a reference RDKit made: every one reads
back exactly, the refusals and every atom's hydrogens agree, and fewer of our drawings overlap atoms than
RDKit's own.

**Cages are drawn as solids.** Adamantane, cubane, hexamine, the phosphorus oxides, quinuclidine, tropane
— a ring system whose rings share three atoms or more, or an atom shared by three rings, is laid out in
three dimensions and drawn as a picture of the solid, the way a textbook draws it, with each bond at the
back broken where it passes behind one at the front. It is framed the way the textbook frames it, because
that is what makes a picture of a solid read as one rather than inside out: a family of parallel bonds
stands straight up the page, seen from above so their tops are nearer the reader, with the nearest of them
left of the middle — the framing of Wikipedia's adamantane and hexamine. The flat drawing is scored by the same yardstick and
wins whenever it reads as well, so norbornane and the bicycles that draw cleanly on a page stay flat. A system holding an aromatic ring, or a ring larger than eight, is never drawn as a solid: morphine
and a cryptand are drawn flat by everyone. Where a cage's symbols crowd one another, the structure's bonds
are drawn up to half as long again.

**Known limits.** A `/` `\` inside a large ring cannot bend the ring it is part of, so its geometry follows the ring. A stereocentre in a cage gets its wedge from the page
as drawn, not from the solid's depth. Only tetrahedral centres get wedges: `@AL`, `@SP`, `@TB`
and `@OH` read, but draw no stereo. There is no option to draw hydrogens explicitly, and no settings line.

---

## Musical Notation — sub-support

### One path

ABC and LilyPond are two ways of writing the same thing, and one engraver draws both.

- **Where it is written.** A fenced ```abc or ```lilypond block; a `#% … #%` block, which is a fence
  spelled another way (below); or a file of its own, `.abc` or `.ly`. Every one of them reaches
  [`MusicDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/MusicDiagramHandler.cs) — one
  registration per notation — so one path lights it up on both markdown surfaces.
- **How it is read.** Each notation is read into its own syntax tree, which prints back exactly what was
  written, and worked over by a pipeline of stages —
  [`AbcPipeline`](../src/Nexaflow.Markdown/Music/Abc/AbcPipeline.cs) and
  [`LilyPondPipeline`](../src/Nexaflow.Markdown/Music/LilyPond/LilyPondPipeline.cs) — that hang what each note
  lasts and sounds underneath it.
- **How it is drawn.** A builder per notation —
  [`AbcBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Music/Abc/AbcBuilder.cs),
  [`LilyPondBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Music/LilyPond/LilyPondBuilder.cs) — reads that tree
  into rows of bars, and both are a [`MusicBuilder`](../src/Nexaflow.Visuals.Text/Markdown/Music/MusicBuilder.cs),
  the one engraver, which lays the rows onto the layout tree the formulas and barcodes already use. Every
  piece of the picture says which characters it was drawn from, which is what makes a note something a
  reader can click, select and edit in place. [`MusicScore`](../src/Nexaflow.Visuals.Text/Markdown/Music/MusicScore.cs)
  is the page it sits on. Design: [docs/markdown-ast.md](markdown-ast.md).

A notation's builder does only what that notation leaves to it. ABC writes down where its bars and beams
go; LilyPond leaves bars, beams and printed accidentals to whoever engraves it, so its builder plays the
music through to find them (see *LilyPond coverage*). Everything after that — staves, heads, stems, beams,
curves, words, spacing and line breaks — is the one engraver's.

**Editing it.** A ```abc block is written on in place. Click a note head to select the note, click a
beamed pair to select the pair, drag for a run — then:

| | |
|---|---|
| `A`–`G` | a note, in the octave the one before it was in |
| Page Up / Page Down | an octave up or down |
| `#` / `_` | a semitone up or down, spelled out |
| `+` / `-` | longer or shorter |

…all of them on the selection, or on the note the caret has just passed. A right-click offers the same set
as a small ribbon, so nobody has to remember a key. Design: [docs/markdown-ast.md](markdown-ast.md). A
LilyPond block is drawn and selected the same way; the verbs that type on it are ABC's so far.

A part song is a **bracketed system**: one staff per voice, sharing one bar grid, with a bracket down the
left, the bar lines running through, the voice names at the left of the first line, and each voice in the
clef its `V:` asked for. Voices the source barred differently stack honestly instead — forcing a grid onto
parts that disagree about where the bars are would misalign every bar after the first difference.

**What it reads and does not act on.** Everything below parses and round-trips — the tree holds every
character of it — and nothing downstream does anything with it yet. That is the honest shape of a gap in
this design: the reading is never the thing that is missing.

| | |
|---|---|
| Voice overlays (`&`) | read, and marked *read and not engraved* so a reader is told |
| `Q:` tempo, on a line or inline | read; no tempo is printed anywhere |
| `%%` stylesheet directives | read as comments; none is obeyed |
| `P:` parts | read as a field; the part order is not applied |
| A mid-tune `T:` | read; not printed as a section heading |
| Clef **inference** | a voice takes the clef its `V:` or `K:` names, and the treble otherwise. It does not read one off the part's range, so a bass line that names no clef sits in ledger lines |

**What the corpus does and does not say.** Ten thousand real tunes are held against the reading
(`AbcCorpusTests`, parse-level, no fonts): every one round-trips exactly, the parser only ever copies, and
no pipeline stage changes the source. A sample of them is engraved as well (`AbcCorpusRenderTests`): none
throws, almost none comes out empty, and every piece that names source names source the tune has.

**None of that says the drawing is right.** The corpus ships a reference picture beside every tune and
nothing here has ever looked at one. A ranking sweep against them — the shape `LatexPictureSweepTests`
already has, with `GrayImage.InkOverlap` — is the missing oracle, and until it exists "it engraves" is the
strongest claim available.

**The `#% … #%` block** is the older spelling, kept because documents use it — the repo's only custom
Markdig block extension ([`MusicBlockExtension`](../src/Nexaflow.Visuals.Text/Markdown/Music/MusicBlockExtension.cs),
registered via `UseMusicNotation()`). The opening fence carries an optional dialect tag, and the dialect
is auto-detected when it is omitted; either way it draws exactly what the fenced block would:

```
#%abc                     #%lilypond                 #%
X:1                       \relative c' {             X:1
T:Speed the Plough          \clef treble             K:C
M:4/4                       c4 d e f | g1            CDEF
K:G                       }                          #%
GABc dedB|c2A2 A2BA|      #%                        (untagged → auto-detected as ABC)
#%
```

**One engraver, two readings.** Both notations are drawn with the bundled **Bravura** SMuFL font (SIL OFL)
plus WPF geometry — no browser, no JS, matching the diagram engine's native approach. Ink follows the
`MarkdownPalette`; the score sizes to **40–80% of the column, centred**, and wraps by width (honouring
notation line breaks first). Where nothing can be drawn the source is shown instead; where part of it
cannot, the rest is drawn and that part is marked where it was written.

| Notation | Read by | Support | Not yet |
|---|---|---|---|
| **ABC** ([spec](https://abcnotation.com/wiki/abc:standard:v2.1)) | [`AbcParser`](../src/Nexaflow.Markdown/Music/Abc/AbcParser.cs) | **Complete for the practical language** — see the table below. | Voice overlays (`&`), inline `[L:]`/`[Q:]`, `%%` stylesheet directives, clef inference, `P:` parts. |
| **LilyPond** ([docs](https://lilypond.org/doc/v2.26/Documentation/notation/index)) | [`LilyPondParser`](../src/Nexaflow.Markdown/Music/LilyPond/LilyPondParser.cs) | **Complete for the practical language, at par with ABC** — see the table below. | Polyphony within one staff (`<< … \\ … >>` — the first voice is engraved), dynamics and hairpins, figured bass, mid-staff clef changes, note names other than Dutch, embedded Scheme (read and skipped). |

**The two notations are held to each other.** `TheSameTuneInBothNotations_EngravesTheSame` writes *Speed the
Plough* — the tune both sample docs print — in ABC and in LilyPond, engraves both, and asserts that every bar's
note heads sit on the same lines and spaces. Any drift between the two readings — an octave off, a bar closed
in the wrong place, a duration mis-scaled — moves a head, and that is where it shows.

### A tune can also be a file

`.abc` and `.ly` open in the **markdown tab**, each as the one block it is rather than as a document that
contains one.

The mechanism is `InlineMarkdownEditor.SingleBlock` — a fenced language name, or empty for a document. The
editor owns the fence: the host hands it the tune, the editor puts a ```` ```abc ```` or ```` ```lilypond ````
around it to render, and takes it off again on the way out. So `MarkdownViewModel.Markdown` holds the tune
and nothing else, `Save`
writes exactly what was read, and **the bytes on disk never carry a wrapper**. A file that was never
markdown does not become markdown by having been opened.

That property was already there for maths — it is how the Solver's LaTeX tab has always worked, with `$$`
instead of a fence — and was called `SingleFormula`. ABC is what made it worth generalising: the concept is
"one block of one language", and only the delimiters were ever LaTeX's.

| Touch point | Where |
|---|---|
| Which extensions are one block, and in what language | [`SingleBlockFiles`](../src/Nexaflow.Features/Nexaflow.Features.Markdown/SingleBlockFiles.cs) — one row per file type |
| The tab that opens | [`ShowMusicAction`](../src/Nexaflow.Features/Nexaflow.Features.Markdown/FileActions/ShowMusicAction.cs), experience `/text/music` |
| The extension → experience mapping | `default-filemap.json` |
| The editor property | [`InlineMarkdownEditor.SingleBlock`](../src/Nexaflow.Visuals.Text/Markdown/InlineMarkdownEditor.cs) |

Adding another notation is a row in `SingleBlockFiles`, a filemap entry, and nothing else — the reading,
the rendering, the inline editing, the dirty tracking and the saving are the markdown tab's, unchanged.

**One thing this fixed on the way past.** The editor only ever adopted a `FormulaElement` when a single
block took focus, so any other language rendered and then could not be typed into — a caret no keystroke
reached. Adoption now goes through the `IEditableBlock` seam, which is the same behaviour for maths and the
only thing that makes the rest of them editable at all.

### ABC coverage

| Construct | Written | Engraved as |
|---|---|---|
| Pitches | `C,, … C … c … c''` | Note heads with ledger lines, any octave |
| Note lengths | `A/4 A/2 A/ A A2 A3 A6 A7 A12 A16` | 32nd → **breve** (double whole), with up to 3 augmentation dots |
| Unit note length | `L:1/16`, `L:1/8`, `L:1/4` | Rescales every multiplier; defaults from the meter when absent |
| Beams | whitespace grouping | Primary + secondary beams; **flat unless the group's contour is monotonic** |
| Bar lines | `\|` `\|\|` `[\|` `\|]` `\|:` `:\|` `::` | Single, double, thick-thin, thin-thick, both repeat forms |
| Repeat brackets | `\|1 … :\|2 …`, `[1` | Numbered bracket above the staff, closing at the repeat |
| Broken rhythm | `A>A` `A<A` `A>>A` `A>>>A` | Dots one side, halves the other; the short note keeps a stub beam |
| Tuplets | `(2 (3 (4 … (p:q:r` | Compressed spacing + the number above; a bracket when unbeamed. `q` reads the meter |
| Ties & slurs | `A-A`, `(AB)`, nested `((AA)A)` | Curves; ties cross bar lines and system breaks, slurs bow away from the stems |
| Accidentals | `__A _A =A ^A ^^A` | 𝄫 ♭ ♮ ♯ 𝄪, sized so a double-flat clears its note head |
| Chord symbols | `"Gm7"D` | Text above the staff |
| Annotations | `"^Fine"` `"_x"` `"<x"` `">x"` | Text placed above / below / left / right |
| Decorations | `.` `~` `H` `L` `M` `O` `P` `S` `T` `u` `v`, `!name!` | Staccato, roll, fermata, accent, mordents, coda, segno, trill, bowings — note marks hug the head, staff marks stack above |
| Grace notes | `{g}A`, `{gAGAG}A`, `{/g}A` | Cue-size heads, beamed, slashed for an acciaccatura |
| Chords | `[CEG]2` `[A4d4]` | Stacked heads on one stem; seconds displaced across it |
| Keys & modes | `K:C` `K:Cm` `K:C Lydian` `K:Bb` `K:F# clef=bass` `K:F bass` | Full circle of fifths from tonic + mode, in any case, glued or spaced; a clef named on the `K:` with or without its `clef=` |
| Meter | `M:4/4` `M:C` `M:C\|` `M:none` | Figures, or the **C / ¢ symbols** when the source asked for them; free meter prints none |
| Mid-tune changes | `K:` `M:` in the body, `[K:G]` inline | Key/meter change printed in place and carried into the next system's header. A mid-tune `T:` is read and not printed (see above) |
| Rests | `z2` `x2` `Z` | Visible, invisible (time only), whole-bar (centred) |
| Voices | `V:` / `V: 1` / `[V: P1]` at the start of a line | A **bracketed system**: one staff per voice, sharing one bar grid, with the bar lines running through and the voice names at the left, each voice in the clef its `V:` or `K:` names. A line that opens by naming its voice is that voice's line — the shape a part song written a bar to a line takes. Voices the source barred differently fall back to an honest stack |
| Lyrics | `w:` with `-` `_` `*` `\|` `~` `\-` | Syllables under the notes, hyphens, melisma extenders, bar sync, stacked verses |
| Header fields | `T:` `C:` `O:` `R:` `S:` `Z:` `N:` `W:` | Title + subtitles centred; `R:` italic top-left; `C: (O:)` top-right; `N:`/`S:`/`Z:`/`W:` under the score |

### LilyPond coverage

Three things LilyPond leaves to whoever engraves it have **no ABC counterpart**, and they are where a LilyPond
engraving can be wrong in a way an ABC one cannot. All three follow from the meter and the key in force where a
note is *played* — and a definition's notes are played wherever the definition is used, each time in that
place's meter — so the builder works them out by playing the music through, not by reading them off the tree
([`LilyPondBuilder.Bars`](../src/Nexaflow.Visuals.Text/Markdown/Music/LilyPond/LilyPondBuilder.Bars.cs)):

- **Bar lines come from the meter.** A `|` is a bar *check*, not a bar line: a tune with none in it still bars
  itself, and a check names the line the meter implies there, so a reader can point at it — a line nobody
  wrote names nothing. `\partial` shortens the pickup, `\cadenzaOn` suspends barring altogether, and a
  `\bar "…"` may arrive *after* the meter has already closed the bar it belongs to, so it reaches back to it.
- **Beams come from the meter too**, the way LilyPond beams them rather than how the source is spaced: by the
  beat, except that eighths go by the half bar in four-four and two-two and a whole three-four bar goes under
  one beam. Checked against `lilypond.exe`'s own engraving.
- **Accidentals are printed, not written.** A note name carries its own alteration — `fis` is F sharp whatever the
  key — so unlike ABC the source never says "print a sharp here". That is an engraving decision, and it follows the
  ordinary rule: print one only where the note departs from what is already in force in that bar.

| Construct | Written | Engraved as |
|---|---|---|
| Pitch entry | `\relative c'`, `\fixed c'`, `\transpose c d`, absolute | Nearest-octave tracking; `c` is C3 and `c'` middle C; a chord measures note by note, and the note after it measures from its first |
| Note names | `c cis cisis ces ceses`, `as` `es` | Dutch names, including the contracted flats |
| Note lengths | `\breve 1 2 4 8 16 32 64`, `4.`, `2*3` | Breve → 64th, dots, duration scaling; a bare note inherits the last length |
| Beams | the meter, or a manual `[ … ]` | Eighths in fours in common and cut time, the whole bar in three-four, threes in a compound meter, pairs otherwise; shorter values by the beat. A tuplet beams as itself, and a beam never runs from one tuplet into the next |
| Bar lines | `\bar "\|\|" "\|." ".\|:" ":\|." ":\|.\|:"` | Double, final, both repeat forms — reaching back to the bar the meter already closed |
| Bar checks / pickup | `\|`, `\partial 4`, `\cadenzaOn` | A check names the bar line it checks; a pickup shortens the first bar; a cadenza suspends barring and prints no meter |
| Repeats | `\repeat volta 2 { … }`, `\alternative`, `\volta 1,2 { … }` | Repeat bar lines + numbered brackets — numbered as each `\volta` says, or in order — the last one stopping where its ending does; `\repeat unfold n` is written out |
| Tuplets | `\tuplet 3/2 { … }`, `\times 2/3 { … }` | Compressed spacing + the number; the *time* is scaled, so the bar still adds up |
| Ties & slurs | `c~ c`, `c( d e)`, phrasing `\(` `\)` | Curves; `(` opens on the note it *follows*, where ABC's precedes |
| Chords | `<c e g>2`, `<c e g>~`, `q` | Stacked heads on one stem; `q` repeats the chord before it |
| Grace notes | `\grace`, `\acciaccatura`, `\appoggiatura` | Cue-size heads, beamed, slashed for an acciaccatura |
| Articulations | `-.` `->` `--` `-^`, `\staccato` `\fermata` `\trill` `\upbow` … | Note marks hug the head; staff marks stack above |
| Text | `c^"Fine"`, `c_"dolce"`, `\markup` | Placed above / below the note, clear of the staff |
| Chord symbols | `\new ChordNames \chordmode { c1 g:7 bes:maj }` | Spelled as a lead sheet spells them — `G7`, `B♭maj7` rather than LilyPond's own triangle — and placed above the note they start on, matched by *time* against the melody |
| Keys & modes | `\key c \major`, `\minor` `\dorian` `\lydian` … | Full circle of fifths from tonic + mode |
| Meter | `\time 4/4` `2/2` `6/8`, `\numericTimeSignature` | 4/4 and 2/2 print as **C / ¢** — LilyPond's default — until the source asks for figures, which it may do *after* the `\time` it applies to |
| Rests | `r2` `R1*3` `s1*2` | Visible; whole-bar and spacer rests written out one bar at a time, a spacer printing nothing |
| Staves | `\new Staff`, `\with { instrumentName = … }`, `StaffGroup`/`ChoirStaff`/`PianoStaff` | One staff per `\new Staff`; staves that run in step are **bracketed into one system** with a shared bar grid |
| Lyrics | `\addlyrics`, `\new Lyrics \lyricsto "id"`, `--` `__` `_` | Syllables under the notes, hyphens, melisma extenders, stacked verses; a rest takes no syllable, and a slur or tie holds one over the notes it joins |
| Header | `\header { title composer opus poet source … }` | Mapped by *where LilyPond prints each field*: title centred, poet/meter top-left, composer (and opus) top-right |
| Structure | `\score`, `\book`, `name = { … }` + `\name`, `%` and `%{ … %}` | Definitions played where they are used, in that place's meter (so `\global` flows into a voice); Scheme `#( … )` read and skipped |

**Engraving rules.** The judgement calls live in
[`Engraving`](../src/Nexaflow.Visuals.Text/Markdown/Music/Rendering/Engraving.cs), separate from the
drawing so they can be asserted rather than eyeballed:

- **Stems** point away from the middle line, and in a beam group or a chord the note reaching furthest from it
  decides for all of them. A note *on* the middle line stems down — a convention borrowed from the corpus,
  whose own engraver does so across all ten thousand of its tunes.
- **Beams** take half the group's interval, capped in both rise and steepness, and go **flat whenever the
  contour isn't monotonic**: `ABcdABcd` climbs twice but zig-zags, so a leaning beam would assert a
  direction the music doesn't have.
- **Justification**: every system but a short final one fills the same width. The short one is *not*
  stretched to match, but nor is it left at its natural width — it is scaled by the same factor its
  siblings were, so its note spacing is continuous with the lines above and only the right edge is ragged.
- **Note spacing** follows `base + rate × √duration` — the classical proportional-but-compressed curve, so a
  whole note is about three times an eighth rather than eight times it — with a floor of a note head plus air
  so a septuplet's heads can't touch.
- **Room outside the staff** is measured from the notation, not fixed: how far the ledger heads, stems, beams
  and marks actually reach decides, and everything that lives outside the staff (chord symbols, text, repeat
  brackets, lyrics) is placed against *that*. A chord symbol belongs above the music, and how high that is
  depends on how high the music went. A row of words is as tall as the words set in it, so text over the
  staff clears its top line.
- **Lyrics** charge a note only *half* its syllable plus half its neighbour's, because a syllable is centred
  under its head. Charging the full width made a line of long and short words lurch.
- **Glyphs** are drawn as filled outlines, not as text: WPF's text pipeline gamma-corrects glyph coverage,
  which visibly fattens a music font's thin strokes.

**A score's words are pieces like its notes.** The title, subtitles, credits and the notes under the score
are drawn by the builder, each naming the characters it was drawn from, so they select in the same drag as
the music.

**Selecting it.** Click a note head for the note, a beamed group for the group, drag for a run — the
selection every engraved block shares, over the pieces the builder names. Figured bass, polyphony within a
single staff, dynamics and MIDI playback remain on the roadmap (tracked as `should` nodes under
`product:score-renderer`).

---

## Markdig extensions — NOT enabled

Available in Markdig but not in the pipeline. None are supported, so none are tested.

| Extension | Pipeline call | What you lose |
|---|---|---|
| Generic attributes | `UseGenericAttributes()` | `{#id .class key=val}` on headings/blocks/inlines. |
| Auto identifiers | `UseAutoIdentifiers()` | Auto heading anchors / `#slug` links. |
| Emoji & smiley | `UseEmojiAndSmiley()` | `:smile:` / `:)` → emoji. |
| SmartyPants | `UseSmartyPants()` | Smart quotes, en/em dashes, ellipsis. |
| Custom containers | `UseCustomContainers()` | `::: warning … :::` fenced/inline containers. |
| Media links | `UseMediaLinks()` | YouTube/Vimeo/audio/video embeds. |
| Bootstrap | `UseBootstrap()` | Bootstrap CSS classes on output (HTML-only; N/A for WPF). |
| JIRA links | `UseJiraLinks()` | `ABC-123` → issue links. |
| Globalization | `UseGlobalization()` | RTL / bidi handling. |
| Soft-as-hard breaks | `UseSoftlineBreakAsHardlineBreak()` | Treat every newline as `<br>`. |
| Non-ASCII no-escape | `UseNonAsciiNoEscape()` | (HTML-output concern; N/A for WPF.) |
| Pragma lines | `UsePragmaLines()` | Source-line tracking spans (HTML-output concern). |
| Self-pipeline | `UseSelfPipeline()` | In-document pipeline directives. |
| `UseAdvancedExtensions()` | — | The bundle of most of the above; deliberately **not** called. |

---

## Test coverage

Tests live in `Nexaflow.Tests.Visuals`, beside the `Nexaflow.Visuals.*` code they cover.

> The `Nexaflow.Tests.Core/…` paths in the table below are **stale** — the suite moved and the rows
> have not been re-pointed. The files are under
> [`Nexaflow.Tests.Visuals/Markdown/`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown) under the
> same names.

| File | Covers |
|---|---|
| [`Visuals/Markdown/MarkdownPipelineFactoryTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownPipelineFactoryTests.cs) | Pipeline parses pipe tables, math blocks, diagram fences; singleton reuse. |
| [`Visuals/Markdown/BlockRendererTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/BlockRendererTests.cs) | Per-block render (headings incl. setext, paragraph, HR, quote, lists incl. nested/loose, indented + fenced code, table, diagram dispatch, math block) **and the full CommonMark inline layer** (inline code, emphasis, strong, links, reference links, autolinks, images local + remote, line breaks, escapes, entities, raw-HTML drop). (UI category.) |
| [`Visuals/Markdown/MarkdownViewTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownViewTests.cs) | `MarkdownView` populates its block panel. (UI category.) |
| [`Visuals/Markdown/MarkdownExtensionsTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownExtensionsTests.cs) | Enabled extensions (grid tables, task lists, emphasis extras, auto links, definition lists, list extras, abbreviations, alert blocks, figures, footers, citations, inline math) + expanded pipe-table edge cases + selectable `MarkdownFlowDocument` tables. (UI category.) |
| [`Visuals/Markdown/DiagramRendererTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/DiagramRendererTests.cs) | WPF render smoke tests for sequence; state/class/requirement + kanban routing; ishikawa (fishbone routing, front-matter config); sankey (CSV routing, front-matter config + node colours); ER (graph routing, word-cardinality + front-matter config); architecture (grid routing not raw text, groups/icons/cross-group edges/junction); swimlane (lane routing not raw text, horizontal direction); cynefin (domain routing not raw text, confusion overflow + front-matter config); timeline (spine routing not raw text, sections + `disableMulticolor` + front-matter title, `direction TD`); journey (face routing not raw text, all five scores + actor/section colours from config); block (grid routing not raw text, nested groups + every shape + block arrows + edges + front-matter padding); front-matter pie routing. (UI category.) |
| [`Unit/Markdown/DiagramParsersTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/DiagramParsersTests.cs) | WPF-free parser tests: sequence (extensive), flowchart, gantt, git graph, mindmap, state, class, requirement, kanban, ishikawa + `IshikawaConfig` (head/category/nested-cause indentation, `diagramPadding`), sankey + `SankeyConfig` (CSV quoting/doubled-quotes/comments, shared nodes, enums + `nodeColors`), ER + `ErConfig` (symbol/word cardinality, identification, attributes/keys/comments, aliases, `layoutDirection`), architecture + `ArchitectureConfig` (groups/services/icons/membership, nested groups, edge sides + all four arrow forms, cross-group edges, junctions, alignment, custom icon packs); cynefin + `CynefinConfig` (domain items, all five domains, confusion overflow, transitions, theme colours); swimlane (direction, top-level subgraph lanes, node shapes, edge styles/labels, cross-lane edges, accessibility lines); timeline + `TimelineConfig` (periods/events, `:` continuation lines, sections, `<br>`/`#colon;`, `direction`, indexed `cScale`/`cScaleLabel` slots); journey + `JourneyConfig` (sections/tasks/scores/actors, distinct actor order, score defaults + clamping, actor-less tasks, colour lists + `fillType`); block + `BlockConfig` (columns/widths/shapes, every bracket shape, nested groups with own columns, spaces + block arrows incl. combined directions, edges with labels + inline shapes, style/classDef/class incl. forward references, entity/`<br>` labels, header variants); front-matter. |
| [`Visuals/Markdown/MarkdownSampleRenderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownSampleRenderTests.cs) | End-to-end: every diagram in the sample dataset parses + renders, plus the `extensions.md` sample (emphasis extras, abbreviations, alert blocks) renders every block. (UI category.) |
| [`Unit/Markdown/MarkdownBlocksTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/MarkdownBlocksTests.cs) | **Editor** block model (split/join/compact) — *not* renderer coverage. |
| [`Unit/Markdown/HtmlToMarkdownTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/HtmlToMarkdownTests.cs) | **HTML→markdown paste** conversion — *not* renderer coverage. |
| [`Markdown/Qr/QrEncoderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Qr/QrEncoderTests.cs) | The QR encoder: round trips through every version and level via `QrTestDecoder`, non-ASCII, the capacity boundary, and the published capacity / alignment-centre / format-code-word tables. |
| [`Matrix/MatrixParserTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Markdown/Matrix/MatrixParserTests.cs) | The 2D block tree: every block and every prefix of it prints back as written, every leaf is copied from the source, a field's key / colon / value, and a line that is not a field held with its reason. |
| [`Markdown/Qr/QrBlockReaderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Qr/QrBlockReaderTests.cs) | The `qr` block's fields: the exact payload each `type:` builds (escaping included), every setting, and each diagnostic — unknown type, mistyped setting, foreign field, missing field, bad value. |
| [`Markdown/Qr/QrBuilderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Qr/QrBuilderTests.cs) | QR dispatch through `DiagramRenderer` to read-only content, ink covering each dark module once, `cellSize`/`margin` measurement, palette vs. block colours, the finders and timing lines, and every failure — nonsense included — drawing a struck-through code with its reason. (UI category.) |
| [`Markdown/Barcode/BarcodeEncoderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeEncoderTests.cs) | Every one of the twenty-three formats down to the module: published symbol tables, computed and verified check digits, Code 128 subset switching, and each value a format refuses. |
| [`Markdown/Barcode/BarcodeReferenceImageTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeReferenceImageTests.cs) | Reads externally generated PNGs back to modules and compares. Opt-in via `NEXAFLOW_BARCODE_IMAGES`; inconclusive without it. |
| [`Markdown/Barcode/BarcodeBlockParserTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeBlockParserTests.cs) | The `barcode` block body: every setting and its bounds, the value offset, and each structural diagnostic — separately from a value the format cannot carry, which is not one. |
| [`Markdown/Barcode/BarcodeElementTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeElementTests.cs) | The element itself: measurement, the error presentation, caret placement, selection and the typing verbs. (UI category.) |
| [`Markdown/Barcode/BarcodeInEditorTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeInEditorTests.cs) | The barcode driven through the editor's block seam — arrowing in and out, typing, space, Home/End, undo, cut, and editing a value whose printed form differs from it. (Desktop category.) |
| [`Markdown/Matrix/DataMatrixEncoderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/DataMatrixEncoderTests.cs) | The Data Matrix encoder: the standard's worked example as a golden vector, placement invariants over all thirty sizes, both encodations and every C40 ending, ECI, GS1, Macro 06, multi-block interleaving, shapes and forced sizes. |
| [`Markdown/Matrix/DataMatrixBlockReaderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/DataMatrixBlockReaderTests.cs) | The `datamatrix` block's fields: the shared types, the exact wire form `gs1`, `ppn`, `ntin` and `mailmark` write (check digits included), `shape:` / `size:`, and each diagnostic. |
| [`Markdown/Matrix/DataMatrixBuilderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/DataMatrixBuilderTests.cs) | Dispatch, a rectangular symbol measuring to its own width and height, a finder and clock on every region, ink covering each dark module once across several regions, a bad block still drawing a code, and the picture rasterised and read back through the test decoder. (UI category.) |
| [`Markdown/Matrix/Pdf417EncoderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/Pdf417EncoderTests.cs) | The PDF417 encoder: all 2,787 table entries checked against the standard's structural rules, another generator's codewords as a golden vector, text/numeric/byte compaction round-trips, every error-correction level, shapes and truncation. |
| [`Markdown/Matrix/Pdf417BuilderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/Pdf417BuilderTests.cs) | The `pdf417` block's fields and its layout: dispatch, the shape settings, rows drawn taller than they are wide, the start / row indicator / codeword / stop columns of a full and a truncated symbol, a bad block still drawing a code, and the picture read back. (UI category.) |
| [`Markdown/Matrix/Pdf417ReferenceImageTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/Pdf417ReferenceImageTests.cs) | Decodes PDF417 symbols made by other generators. Opt-in via `NEXAFLOW_BARCODE_IMAGES`; this is what proves the ingested table. |
| [`Markdown/Matrix/AztecEncoderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/AztecEncoderTests.cs) | The Aztec encoder: both published size and codeword-count tables, the data cells of every one of the 36 symbol sizes covering their capacity exactly once, bit stuffing, the choice of family and layer count, forced sizes, error-correction levels, GS1 and ECI, and round trips through the test decoder. |
| [`Markdown/Matrix/AztecBuilderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/AztecBuilderTests.cs) | The `aztec` block's fields and its layout: dispatch, `format` / `layers` / `ecc` / `eci`, the GS1 wire form, the bullseye, mode message and reference grid, a bad block still drawing a code, and the picture read back. (UI category.) |
| [`Markdown/Matrix/AztecReferenceImageTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Matrix/AztecReferenceImageTests.cs) | Decodes Aztec symbols made by other generators **and** asserts our encoder reproduces them module for module. Opt-in via `NEXAFLOW_BARCODE_IMAGES`; this is the only check that can catch a self-consistent but unreadable layout. |
| [`Chemistry/SmilesParserTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Markdown/Chemistry/SmilesParserTests.cs) | The `smiles` tree: every construct and every prefix of it reads back, the parser only copies, a molecule to the character, bracket atoms as their parts, and what will not read held with its reason. |
| [`Chemistry/SmilesPipelineTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Markdown/Chemistry/SmilesPipelineTests.cs) | The stages: the source left alone, hydrogens as RDKit counts them, ring closures as bonds, Kekulé structures (and biphenyl's link left single), and each impossibility said where it was written. |
| [`Chemistry/StructureLayoutTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Markdown/Chemistry/StructureLayoutTests.cs) | The 2D layout: unit bonds, no overlaps, zig-zag chains, regular rings, straight triple bonds, cis and trans as written, mirror-image wedges, cages drawn as solids (and bicycles that read flat left flat), and determinism. |
| [`Chemistry/SmilesCorpusTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Markdown/Chemistry/SmilesCorpusTests.cs) | 5,481 real molecules against RDKit: round trip, refusals, hydrogens, and overlaps. Opt-in via `NEXAFLOW_SMILES_CORPUS` (default `D:\Datasets\smiles`, made by the `make_reference.py` beside it). |
| [`Markdown/Chemistry/SmilesBuilderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Chemistry/SmilesBuilderTests.cs) | The `smiles` drawing: dispatch, atoms and written bonds carrying their parts, captions, wrapping, element colours, a cage's rear bond broken where it passes behind, trouble on the offending atom, and the stand-in. (UI category.) |

Sample fixtures (driving `MarkdownSampleRenderTests`) live in
[`Nexaflow.Tests.Fixtures/MarkdownSamples.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/MarkdownSamples.cs):
the `mermaid-*` diagram docs (pie, flowchart, quadrant, sequence, gantt, git graph, mindmap, state, class,
requirement, kanban, xychart, radar, ishikawa, sankey, er, venn, architecture, swimlane, cynefin, timeline, journey, block, C4) and `extensions.md` (YAML front matter, emphasis extras, abbreviations, alert blocks).

**Where coverage is thin:**

- **`MarkdownFlowDocument`** (the selectable path) is only tested for tables; its other
  block types (headings, lists, code, quotes) rely on the shared `BlockRenderer` but have
  no FlowDocument-specific assertions.
- **`nomnoml`** has neither a test nor a sample fixture.
- The base CommonMark renderer, the enabled extensions, and the Mermaid parser family are
  all well covered.

---

## Known limitations / gaps worth flagging

Each of these is now a node, so it appears in `$nfi query --status should` rather than only here — a
limitation nothing tracks is indistinguishable from a limitation nobody wants fixed.

| Gap | Node | Why it is still open |
|---|---|---|
| **No syntax highlighting** in code blocks — monospace only | `md-code-highlighting` | The engine that would colour it is in the same solution and already resolves a grammar from the fence's language tag. The join is missing, and it has to land in **both** render paths or a document colours in one view and not the other |
| **No remote images** — local files only; remote URLs degrade to alt text | `md-remote-images` | Needs a cache, a size cap and a failure state, and would be the first thing in the renderer to touch the network — policy as much as feature |
| **Raw HTML is not rendered** — inline HTML dropped, HTML blocks shown as source | `md-raw-html` | CommonMark passes HTML through; deciding how much of it a WPF `FlowDocument` should honour is the real question |
| **Task-list checkboxes are display-only** | `md-task-toggle` | Toggling has to write back to the source, so it belongs with the inline editor's block model — the drawing half is done |
| **No emoji shortcodes** (`:tada:`) | `emoji-and-smilies` | One of four Markdig extensions still off; see the extensions table |

- **Every Mermaid family now renders** — nothing falls back to raw source.

If any of the disabled extensions are wanted, the change is usually a one-line
`.UseX()` in `MarkdownPipelineFactory` **plus** renderer cases in both `BlockRenderer`
and `MarkdownFlowDocument` (and, ideally, a sample + test in `MarkdownSampleRenderTests`).
