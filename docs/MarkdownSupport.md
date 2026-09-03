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
| Musical notation | `UseMusicNotation()` (custom) | ✅ (custom) | ✅ | `MusicBlockParserTests`, `AbcParserTests`, `LilyPondParserTests`, `WpfScoreRendererTests`, `MusicRendererTests`, `MarkdownSampleRenderTests`. The repo's own `#% … #%` block extension → engraved sheet music (see below). |

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
> embedded score owns its own click/drag (measure / note-group selection, see Musical Notation
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

**Mermaid sub-types** ([`MermaidDiagramHandler`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Handlers/MermaidDiagramHandler.cs)):

| Sub-type | Status | Tests |
|---|---|---|
| `graph` / `flowchart` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests` — shapes, arrows, edge ids, chains `A-->B-->C`, fan-out `A-->B & C`, nested subgraphs) + sample render |
| `pie` | ✅ | ✅ render + routing (`DiagramRendererTests`) + sample render |
| `quadrantChart` | ✅ | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample |
| `sequenceDiagram` | ✅ | ✅ parser (`DiagramParsersTests`, extensive) + render (`DiagramRendererTests`) + sample |
| `gantt` | ✅ | ✅ parser (`DiagramParsersTests`) + sample render |
| `gitGraph` | ✅ | ✅ parser (`DiagramParsersTests`) + sample render |
| `mindmap` | ✅ | ✅ parser (`DiagramParsersTests`) + sample render |
| `stateDiagram` / `stateDiagram-v2` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `classDiagram` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `requirementDiagram` | ✅ (Sugiyama layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `kanban` | ✅ (column/card layout) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `xychart` / `xychart-beta` | ✅ (bar + line, both orientations) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `radar-beta` | ✅ (polar plot) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `ishikawa` / `ishikawa-beta` | ✅ (fishbone) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `sankey` | ✅ (flow diagram) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `erDiagram` | ✅ (graph layout) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `venn-beta` | ✅ (overlapping circles) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `architecture-beta` | ✅ (grid layout, icon glyphs) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `swimlane-beta` | ✅ (lane bands) | ✅ parser (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `cynefin-beta` | ✅ (five-domain grid) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `timeline` | ✅ (period spine, LR or TD) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `journey` | ✅ (scored faces, actor legend) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |
| `C4Context` / `C4Container` / `C4Component` / `C4Dynamic` / `C4Deployment` | ✅ (graph layout, C4-PlantUML macro set) | ✅ parser + projection (`C4ParserTests`, `C4ProjectionTests`) + card/palette (`C4ElementTests`) + render + sample render. See sub-features below. |
| `C4Sequence` *(Nexaflow extension)* | ✅ (shared sequence renderer) | ✅ projection (`C4SequenceProjectionTests`) + render + sample render. See sub-features below. |
| `block-beta` | ✅ (author-placed grid, nested blocks) | ✅ parser + config (`DiagramParsersTests`) + render (`DiagramRendererTests`) + sample render. See sub-features below. |

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
`style A fill:…`, inline `A:::name`). A front-matter / `title:` is centred over the diagram. **Limitations:**
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
diagram **except `xychart`**, `config:` is recognised but not applied, so the ticket renders as a plain chip.

**XY-chart sub-features** ([`MermaidXyChartParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidXyChartParser.cs)
+ [`WpfXyChartRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfXyChartRenderer.cs)).
An XY chart has its own [`XyChart`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/XyChart.cs) model (title,
a category x-axis, a numeric y-axis, and any number of `bar`/`line` series) drawn on a measured canvas. Supported:
`xychart` / `xychart-beta`, **vertical** (default) and **`horizontal`** orientation; a categorical x-axis
(`x-axis [a, "b c", d]`) or a numeric range (`x-axis "t" 0 --> 100`); a numeric y-axis with an explicit range
(`y-axis "t" min --> max`) or auto-ranged from the data with "nice" ticks; `bar` and `line` series (a named series
joins the legend), grouped bars when several share the axes; per-point labels on line points (`line [540 "PaLM", 7]`,
mixable with unlabeled values); signed/decimal/leading-dot values (`+1.3`, `.6`, `-.34`); and `%%` comments.
**The front-matter `config:` block is applied** (`xychart` and `radar` are the only diagrams that read it)
([`XyChartConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/XyChartConfigParser.cs) →
[`XyChartConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/XyChartConfig.cs)): the `config: xyChart`
layout/flag keys (`width`, `height`, `showTitle`, `titleFontSize`/`titlePadding`, `showLegend`/`legend*`,
`chartOrientation`, `plotReservedSpacePercent`, `showDataLabel`/`showDataLabelOutsideBar`, and the per-axis
`xAxis`/`yAxis` `AxisConfig` — `showLabel`/`showTick`/`showAxisLine`/`tickLength`/`tickWidth`/`axisLineWidth`/
`labelRotation`/`labelFontSize`/`titleFontSize`/…) and the `config: themeVariables: xyChart` colours
(`backgroundColor`, `titleColor`, `dataLabelColor`, `legendTextColor`, the eight `x`/`yAxis…Color` keys, and the
comma-separated `plotColorPalette`). Colours fall back to the active `MarkdownPalette` (series colours from its
`Series` bank) when a key is unset. **Limitation:** the legacy inline `%%{init: …}%%` config-directive form isn't
read — front-matter only.

**Radar-chart sub-features** ([`MermaidRadarParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidRadarParser.cs)
+ [`WpfRadarRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfRadarRenderer.cs)).
A radar / spider / Kiviat chart has its own [`RadarChart`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/RadarChart.cs)
model — a set of `axis` spokes and any number of `curve` datasets — drawn on a polar plot (first axis at the top,
clockwise). Supported: `radar-beta`; `title` (inline or front-matter); `axis` spokes as bare ids (`axis A, B, C`)
or `id["Label"]`, several per line; `curve` datasets in **positional** form (`curve c["Label"]{1, 2, 3}`, mapped to
the axes in order) or **keyed** form (`curve c{ axisId: value, … }`, mapped by axis id), several per line; and the
body options `min` / `max` (auto from the data when omitted) / `ticks` (concentric rings) / `graticule circle|polygon`
/ `showLegend`. Each curve is a closed cardinal spline rounded by `curveTension`, filled at `curveOpacity`; the
legend wraps to multiple rows when it would overflow. **The front-matter `config:` block is applied**
([`RadarConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/RadarConfigParser.cs) →
[`RadarConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/RadarConfig.cs)): the `config: radar` geometry
(`width`, `height`, `margin*`, `axisScaleFactor`, `axisLabelFactor`, `curveTension`), the `config: themeVariables: radar`
styling (`axisColor`/`axisStrokeWidth`, `axisLabelFontSize`, `curveOpacity`/`curveStrokeWidth`, `graticuleColor`/
`graticuleOpacity`/`graticuleStrokeWidth`, `legendBoxSize`/`legendFontSize`), and the global `themeVariables`
`titleColor`, `fontSize`, and the `cScale0…N` curve palette. Colours fall back to the active `MarkdownPalette` (curve
colours from its `Series` bank) when unset. **Limitation:** as with xychart, the legacy `%%{init: …}%%` directive form
isn't read.

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

**Venn sub-features** ([`MermaidVennParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidVennParser.cs)
+ [`WpfVennRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Rendering/WpfVennRenderer.cs)).
A Venn diagram has its own [`VennDiagram`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/VennDiagram.cs) model —
`set` circles and the `union` (intersection) regions between them — drawn as overlapping circles in a canonical layout
(one / two side-by-side / three in a triangle, a ring for four+). Supported: `venn-beta`; `title`; `set id["Label"]:size`
(comma is the only intersection operator, so `union A,B["AB"]:size` is the A∩B region — `:size` weights the circle area,
`["Label"]` renames it); `text` items (indented under the most recent set/union, or an explicit `text A,B id["label"]`);
and `style id` / `style A,B` (fill / color / stroke / fill-opacity, the comma target being the intersection region).
**The front-matter `config:` block is applied** ([`VennConfigParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/VennConfigParser.cs)
→ [`VennConfig`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Charts/VennConfig.cs)): `config: venn` `width`/`height`/
`padding`, and the `themeVariables` `venn1…venn8` circle palette (else the palette's series bank). **Limitations:** the
layout is canonical (radii scale with `size`), not the area-proportional solver Mermaid uses, so overlap areas are
indicative rather than exact; `useDebugLayout` is ignored.

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
`swimlane-beta [DIR]` header to a `flowchart [DIR]` header and reuses `MermaidParser` for the full grammar; the
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
`UpdateBoundaryStyle` — a relationship's `$lineColor`/`$textColor`/`$lineStyle` colour its edge in both the structural and the sequence renderers; `SHOW_LEGEND`, `HIDE_STEREOTYPE`, `LAYOUT_TOP_DOWN`/`LAYOUT_LEFT_RIGHT`/`LAYOUT_LANDSCAPE`, the
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

Mermaid `--- … ---` front-matter (title/config) is stripped and a title applied
(`MermaidFrontmatter`); this is tested directly (`DiagramParsersTests` →
`Frontmatter_*`, and `DiagramRendererTests.Frontmatter_PieRoutesToChartNotSourceText`). The `config:` block is
discarded for every diagram **except `xychart`, `radar-beta`, `ishikawa-beta`, `sankey`, `erDiagram`, `venn-beta`,
`architecture-beta` and `cynefin-beta`** (which re-read it via `MermaidFrontmatter.RawBlock` and apply the
`config:` options described above) **and the `config: nexaflow:` block**, which every graph-family diagram reads.
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

**Four pieces**, none of which knows about the next:

| Piece | Does |
|---|---|
| [`QrBlockParser`](../src/Nexaflow.Visuals.Text/Markdown/Qr/QrBlockParser.cs) | lines → a `QrBlock` (payload + settings), or a message saying which line is wrong |
| [`QrPayload`](../src/Nexaflow.Visuals.Text/Markdown/Qr/QrPayload.cs) | `type:` + fields → the one string that gets encoded, in the convention its scanners expect |
| [`QrEncoder`](../src/Nexaflow.Visuals.Text/Markdown/Qr/QrEncoder.cs) | string → a `QrMatrix`. WPF-free, so a symbol can be asserted on without a UI thread |
| [`WpfQrRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Qr/WpfQrRenderer.cs) | matrix → one `Path` on a quiet-zone `Border` |

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
It contributes **no layout tree** (`Root` is null): the value is one run of characters, and a tree would
be inventing structure to describe a flat string. What it keeps instead is the x, row and height of
every place the caret can stand.

While the caret is in it the text row shows the **value**, not the number the symbol prints. For most
of the retail family those are different strings — an ISBN's value carries hyphens the symbol never
prints, an EAN-13's gains a computed check digit, a UPC-E completes both ends — and a caret measured
against one while the keys edit the other points at the wrong character. *This is the known rough
edge*: the honest fix is a parse tree with a layout tree mapping onto it (as LaTeX has), which would
let the caret sit in the printed number and still know which value characters it edits, and would make
the caption editable. It waits on `ILayoutNode` losing its source offsets.

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

## Musical Notation — sub-support

Musical notation is written in a **`#% … #%`** block — the repo's only custom Markdig block extension
([`MusicBlockExtension`](../src/Nexaflow.Visuals.Text/Markdown/Music/MusicBlockExtension.cs), registered
via `UseMusicNotation()`). The opening fence carries an optional dialect tag; the dialect is
auto-detected when omitted:

```
#%abc                     #%lilypond                 #%
X:1                       \relative c' {             X:1
T:Speed the Plough          \clef treble             K:C
M:4/4                       c4 d e f | g1            CDEF
K:G                       }                          #%
GABc dedB|c2A2 A2BA|      #%                        (untagged → auto-detected as ABC)
#%
```

**Two parsers, one renderer.** Both notations parse into a shared, WPF-free score model
([`Music/Model/`](../src/Nexaflow.Visuals.Text/Markdown/Music/Model)) which a single native-WPF engraver
([`WpfScoreRenderer`](../src/Nexaflow.Visuals.Text/Markdown/Music/Rendering/WpfScoreRenderer.cs)) draws
with the bundled **Bravura** SMuFL font (SIL OFL) plus WPF geometry — no browser, no JS, matching the
diagram engine's native approach. Ink follows the `MarkdownPalette`; the score sizes to **40–80% of the
column, centred**, and wraps by width (honouring notation line breaks first). Unparseable notation
degrades to a themed source-text box; unsupported constructs render what they can and note the rest.

| Dialect | Parser | Support | Not yet |
|---|---|---|---|
| **ABC** ([spec](https://abcnotation.com/wiki/abc:standard:v2.1)) | [`AbcParser`](../src/Nexaflow.Visuals.Text/Markdown/Music/Parsers/AbcParser.cs) | **Complete for the practical language** — see the table below. | Voice overlays (`&`), inline `[L:]`/`[Q:]`, `%%` stylesheet directives, per-voice clef inference, `P:` parts. |
| **LilyPond** ([docs](https://lilypond.org/doc/v2.26/Documentation/notation/index)) | [`LilyPondParser`](../src/Nexaflow.Visuals.Text/Markdown/Music/Parsers/LilyPondParser.cs) | **Complete for the practical language, at par with ABC** — see the table below. | Polyphony within one staff (`<< … \\ … >>` — the first voice is engraved, the rest reported), dynamics and hairpins, figured bass, `\transpose`, mid-staff clef changes, note names other than Dutch, embedded Scheme (tolerated + skipped). |

**The two parsers are held to each other.** `TheSameTune_WrittenInBothDialects_LandsOnTheSameScore` parses *Speed
the Plough* — the tune both sample docs print — from ABC and from LilyPond and asserts the two scores agree note for
note, duration for duration, bar line for bar line. It is the only test that can catch one parser drifting from the
other, and it doubles as the guarantee that neither sample document contains a wrong note.

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
| Keys & modes | `K:C` `K:Cm` `K:C Lydian` `K:Bb` `K:F# clef=bass` | Full circle of fifths from tonic + mode, in any case, glued or spaced |
| Meter | `M:4/4` `M:C` `M:C\|` `M:none` | Figures, or the **C / ¢ symbols** when the source asked for them; free meter prints none |
| Mid-tune changes | `K:` `M:` `T:` in the body, `[K:G]` inline | Key/meter change printed in place and carried into the next system's header; `T:` becomes a section heading |
| Rests | `z2` `x2` `Z` | Visible, invisible (time only), whole-bar (centred) |
| Voices | `V:` / `[V: P1]` | A **bracketed system**: one staff per voice, sharing one bar grid, with the bar lines running through and the voice names at the left. A voice that names no clef has one read off its range, so a bass part isn't buried in ledger lines. Voices the source barred differently fall back to an honest stack |
| Lyrics | `w:` with `-` `_` `*` `\|` `~` `\-` | Syllables under the notes, hyphens, melisma extenders, bar sync, stacked verses |
| Header fields | `T:` `C:` `O:` `R:` `S:` `Z:` `N:` `W:` | Title + subtitles centred; `R:` italic top-left; `C: (O:)` top-right; `N:`/`S:`/`Z:`/`W:` under the score |

### LilyPond coverage

Three things LilyPond does have **no ABC counterpart**, and they are where a LilyPond parse can be wrong in a way an
ABC parse cannot — so they are the ones worth knowing:

- **Bar lines come from the meter.** A `|` is a bar *check*, not a bar line; a tune with none in it still bars
  itself. `\partial` shortens the pickup, `\cadenzaOn` suspends barring altogether, and a `\bar "…"` may arrive
  *after* the meter has already closed the bar it belongs to — so it has to reach back to it.
- **Beams come from the meter too**, rather than from how the source is spaced, so they are worked out after the
  fact ([`AutoBeam`](../src/Nexaflow.Visuals.Text/Markdown/Music/Parsers/LilyPondParser.cs)).
- **Accidentals are printed, not written.** A note name carries its own alteration — `fis` is F sharp whatever the
  key — so unlike ABC the source never says "print a sharp here". That is an engraving decision, and it follows the
  ordinary rule: print one only where the note departs from what is already in force in that bar.

| Construct | Written | Engraved as |
|---|---|---|
| Pitch entry | `\relative c'`, `\fixed c'`, absolute | Nearest-octave tracking; `c` is C3 and `c'` middle C |
| Note names | `c cis cisis ces ceses`, `as` `es` | Dutch names, including the contracted flats |
| Note lengths | `\breve 1 2 4 8 16 32 64`, `4.`, `2*3` | Breve → 64th, dots, duration scaling; a bare note inherits the last length |
| Beams | the meter, or a manual `[ … ]` | Eighths in fours in common time, threes in a compound one, pairs otherwise; shorter values by the beat. A tuplet beams as one group |
| Bar lines | `\bar "\|\|" "\|." ".\|:" ":\|." ":\|.\|:"` | Double, final, both repeat forms — reaching back to the bar the meter already closed |
| Bar checks / pickup | `\|`, `\partial 4`, `\cadenzaOn` | A check closes its bar; a pickup shortens the first; a cadenza suspends barring and prints no meter |
| Repeats | `\repeat volta 2 { … }`, `\alternative` | Repeat bar lines + numbered brackets; `\repeat unfold n` is written out |
| Tuplets | `\tuplet 3/2 { … }`, `\times 2/3 { … }` | Compressed spacing + the number; the *time* is scaled, so the bar still adds up |
| Ties & slurs | `c~ c`, `c( d e)`, phrasing `\(` `\)` | Curves; `(` opens on the note it *follows*, where ABC's precedes |
| Chords | `<c e g>2`, `<c e g>~` | Stacked heads on one stem; `\relative` tracks the chord's *first* note |
| Grace notes | `\grace`, `\acciaccatura`, `\appoggiatura` | Cue-size heads, beamed, slashed for an acciaccatura |
| Articulations | `-.` `->` `--` `-^`, `\staccato` `\fermata` `\trill` `\upbow` … | Note marks hug the head; staff marks stack above |
| Text | `c^"Fine"`, `c_"dolce"`, `\markup` | Placed above / below the note |
| Chord symbols | `\new ChordNames \chordmode { c1 g:7 }` | Placed above the note they start on, matched by *time* against the melody |
| Keys & modes | `\key c \major`, `\minor` `\dorian` `\lydian` … | Full circle of fifths from tonic + mode |
| Meter | `\time 4/4` `2/2` `6/8`, `\numericTimeSignature` | 4/4 and 2/2 print as **C / ¢** — LilyPond's default — until the source asks for figures, which it may do *after* the `\time` it applies to |
| Rests | `r2` `R1*3` `s4` | Visible; whole-bar (written out one bar at a time); invisible spacer |
| Staves | `\new Staff`, `\with { instrumentName = … }`, `StaffGroup`/`ChoirStaff`/`PianoStaff` | One staff per `\new Staff`; voices that run in step are **bracketed into one system** with a shared bar grid |
| Lyrics | `\addlyrics`, `\new Lyrics \lyricsto "id"`, `--` `__` `_` | Syllables under the notes, hyphens, melisma extenders, stacked verses |
| Header | `\header { title composer opus poet source … }` | Mapped by *where LilyPond prints each field*: title centred, poet/meter top-left, composer (and opus) top-right |
| Structure | `\score`, `\book`, `name = { … }` + `\name`, `%` and `%{ … %}` | Definitions substituted inline (so `\global` flows into a voice); Scheme `#( … )` skipped |

**Engraving rules.** The judgement calls live in
[`Engraving`](../src/Nexaflow.Visuals.Text/Markdown/Music/Rendering/Engraving.cs), separate from the
drawing so they can be asserted rather than eyeballed:

- **Stems** flip *strictly above* the middle line — a note on the middle line stems up — and in a beam
  group the note reaching furthest from the middle line decides for all of them.
- **Beams** take half the group's interval, capped in both rise and steepness, and go **flat whenever the
  contour isn't monotonic**: `ABcdABcd` climbs twice but zig-zags, so a leaning beam would assert a
  direction the music doesn't have.
- **Justification**: every system but a short final one fills the same width. The short one is *not*
  stretched to match, but nor is it left at its natural width — it is scaled by the same factor its
  siblings were, so its note spacing is continuous with the lines above and only the right edge is ragged.
- **Note spacing** follows `base + rate × √duration` — the classical proportional-but-compressed curve, so a
  whole note is about three times an eighth rather than eight times it — with a floor of a note head plus air
  so a septuplet's heads can't touch.
- **Room outside the staff** is measured from the notation, not fixed: `AboveMusic`/`BelowMusic` are how far
  the ledger heads, stems, beams and marks actually reach, and everything that lives outside the staff (chord
  symbols, repeat brackets, lyrics) is placed against *that*. A chord symbol belongs above the music, and how
  high that is depends on how high the music went.
- **Lyrics** charge a note only *half* its syllable plus half its neighbour's, because a syllable is centred
  under its head. Charging the full width made a line of long and short words lurch.
- **Glyphs** are drawn as filled outlines, not as text: WPF's text pipeline gamma-corrects glyph coverage,
  which visibly fattens a music font's thin strokes.

**A score's prose is text, not pixels.** The title, subtitles and the notes/source/verses under the score are
*not* painted into the engraved element — they come back as real FlowDocument paragraphs either side of it
(`WpfScoreRenderer.RenderBlocks`), so the reader can drag-select and copy them like any other markdown text.
Only the notation itself is engraved. (The syllables under the notes are the exception: they are glued to head
positions, so they stay part of the drawing.)

The score is **interactive**: click a note head to select that note, click a measure's background to select
the whole measure (highlighted barline-to-barline), or drag to select a note range — a themed accent wash,
exposed via `ScoreElement.SelectedRange` / `SelectionChanged`. Inside a RichTextBox host the whole gesture
is driven by the host through `IInteractiveBlock` (Begin/Extend/EndPointerSelect), since mouse events never
reach an embedded element reliably. Figured bass, polyphony within a single staff, dynamics and MIDI playback
remain on the roadmap (tracked as `should` nodes under `product:score-renderer`).

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
| [`Visuals/Markdown/DiagramRendererTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/DiagramRendererTests.cs) | WPF render smoke tests for quadrant + sequence; state/class/requirement + kanban routing; XY chart (vertical/horizontal, per-point labels, front-matter config); radar (circle/polygon graticule, keyed curve, front-matter config); ishikawa (fishbone routing, front-matter config); sankey (CSV routing, front-matter config + node colours); ER (graph routing, word-cardinality + front-matter config); venn (circle routing, three-set + custom palette + front-matter config); architecture (grid routing not raw text, groups/icons/cross-group edges/junction); swimlane (lane routing not raw text, horizontal direction); cynefin (domain routing not raw text, confusion overflow + front-matter config); timeline (spine routing not raw text, sections + `disableMulticolor` + front-matter title, `direction TD`); journey (face routing not raw text, all five scores + actor/section colours from config); block (grid routing not raw text, nested groups + every shape + block arrows + edges + front-matter padding); front-matter pie routing. (UI category.) |
| [`Unit/Markdown/DiagramParsersTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/DiagramParsersTests.cs) | WPF-free parser tests: quadrant, sequence (extensive), flowchart, gantt, git graph, mindmap, state, class, requirement, kanban, XY chart + `XyChartConfig` (layout/axis/theme keys, `plotColorPalette`), radar + `RadarConfig` (axes, positional/keyed curves, options, geometry/styling/`cScale`), ishikawa + `IshikawaConfig` (head/category/nested-cause indentation, `diagramPadding`), sankey + `SankeyConfig` (CSV quoting/doubled-quotes/comments, shared nodes, enums + `nodeColors`), ER + `ErConfig` (symbol/word cardinality, identification, attributes/keys/comments, aliases, `layoutDirection`), venn + `VennConfig` (sets/unions/sizes, indented + explicit text, styling, `venn1…8` palette); architecture + `ArchitectureConfig` (groups/services/icons/membership, nested groups, edge sides + all four arrow forms, cross-group edges, junctions, alignment, custom icon packs); cynefin + `CynefinConfig` (domain items, all five domains, confusion overflow, transitions, theme colours); swimlane (direction, top-level subgraph lanes, node shapes, edge styles/labels, cross-lane edges, accessibility lines); timeline + `TimelineConfig` (periods/events, `:` continuation lines, sections, `<br>`/`#colon;`, `direction`, indexed `cScale`/`cScaleLabel` slots); journey + `JourneyConfig` (sections/tasks/scores/actors, distinct actor order, score defaults + clamping, actor-less tasks, colour lists + `fillType`); block + `BlockConfig` (columns/widths/shapes, every bracket shape, nested groups with own columns, spaces + block arrows incl. combined directions, edges with labels + inline shapes, style/classDef/class incl. forward references, entity/`<br>` labels, header variants); front-matter. |
| [`Visuals/Markdown/MarkdownSampleRenderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownSampleRenderTests.cs) | End-to-end: every diagram in the sample dataset parses + renders, plus the `extensions.md` sample (emphasis extras, abbreviations, alert blocks) renders every block. (UI category.) |
| [`Unit/Markdown/MarkdownBlocksTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/MarkdownBlocksTests.cs) | **Editor** block model (split/join/compact) — *not* renderer coverage. |
| [`Unit/Markdown/HtmlToMarkdownTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/HtmlToMarkdownTests.cs) | **HTML→markdown paste** conversion — *not* renderer coverage. |
| [`Markdown/Qr/QrEncoderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Qr/QrEncoderTests.cs) | The QR encoder: round trips through every version and level via `QrTestDecoder`, non-ASCII, the capacity boundary, and the published capacity / alignment-centre / format-code-word tables. |
| [`Markdown/Qr/QrBlockParserTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Qr/QrBlockParserTests.cs) | The `qr` block body: the exact payload each `type:` builds (escaping included), every setting, and each diagnostic — unknown type, mistyped setting, foreign field, missing field, bad value. |
| [`Markdown/Qr/QrRendererTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Qr/QrRendererTests.cs) | QR dispatch through `DiagramRenderer`, geometry area = the dark modules, `cellSize`/`margin` measurement, palette vs. block colours, and both failure paths rendering their reason. (UI category.) |
| [`Markdown/Barcode/BarcodeEncoderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeEncoderTests.cs) | Every one of the twenty-three formats down to the module: published symbol tables, computed and verified check digits, Code 128 subset switching, and each value a format refuses. |
| [`Markdown/Barcode/BarcodeReferenceImageTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeReferenceImageTests.cs) | Reads externally generated PNGs back to modules and compares. Opt-in via `NEXAFLOW_BARCODE_IMAGES`; inconclusive without it. |
| [`Markdown/Barcode/BarcodeBlockParserTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeBlockParserTests.cs) | The `barcode` block body: every setting and its bounds, the value offset, and each structural diagnostic — separately from a value the format cannot carry, which is not one. |
| [`Markdown/Barcode/BarcodeElementTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeElementTests.cs) | The element itself: measurement, the error presentation, caret placement, selection and the typing verbs. (UI category.) |
| [`Markdown/Barcode/BarcodeInEditorTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Visuals/Markdown/Barcode/BarcodeInEditorTests.cs) | The barcode driven through the editor's block seam — arrowing in and out, typing, space, Home/End, undo, cut, and editing a value whose printed form differs from it. (Desktop category.) |

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

- **No syntax highlighting** in code blocks — monospace only.
- **No remote images** — only local files load; remote URLs degrade to alt text.
- **Raw HTML is not rendered** — inline HTML is dropped, HTML blocks show as raw text.
- **No emoji shortcodes** (`:tada:`).
- **Task list checkboxes are display-only** — not clickable to toggle.
- **Every Mermaid family now renders** — nothing falls back to raw source.

If any of the disabled extensions are wanted, the change is usually a one-line
`.UseX()` in `MarkdownPipelineFactory` **plus** renderer cases in both `BlockRenderer`
and `MarkdownFlowDocument` (and, ideally, a sample + test in `MarkdownSampleRenderTests`).
