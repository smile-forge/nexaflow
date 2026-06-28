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
| Alert blocks | `UseAlertBlocks()` | ✅ | ✅ | `MarkdownExtensionsTests` + `extensions.md` sample render. GitHub callouts `> [!NOTE]` / `[!TIP]` / `[!IMPORTANT]` / `[!WARNING]` / `[!CAUTION]` → coloured left-border callout with a bold kind label. Each kind maps to a semantic accent (`Accent`/`Success`/`Important`/`Warning`/`Danger` palette tokens). `AlertBlock` extends `QuoteBlock`, so it's matched before the generic quote case. Selectable path falls back to `BlockRenderer`. |
| YAML front matter | `UseYamlFrontMatter()` | ✅ (stripped) | ✅ | `MarkdownExtensionsTests`. A leading `--- … ---` metadata block is parsed as a `YamlFrontMatterBlock` and **not rendered** (matches Markdig's HTML renderer). Both paths suppress it — block renderer returns a collapsed placeholder, the selectable path emits nothing. |
| Figures | `UseFigures()` | ✅ | ✅ | `^^^` figure block + caption. |
| Footers | `UseFooters()` | ✅ | ✅ | `^^ footer`. |
| Citations | `UseCitations()` | ✅ | ✅ | `""text""` → raised, coloured citation text. **Delimiter is a doubled double-quote, not `^^`** (see note below). |
| Mathematics | `UseMathematics()` | ✅ | ✅ | Block `$$…$$` (`MarkdownPipelineFactoryTests` + `BlockRendererTests`) and inline `$…$` (`MarkdownExtensionsTests`). Rendered with **WpfMath** (LaTeX); falls back to the LaTeX source if unparseable. |
| Diagrams | `UseDiagrams()` | ✅ (custom) | ✅ | `MarkdownPipelineFactoryTests`, `BlockRendererTests`, `MarkdownSampleRenderTests`. Rendering is **fully custom** (see below). |

> **Citation delimiter.** `UseCitations()` emits `""text""` with `DelimiterChar == '"'`, so the
> citation delimiter the renderer matches is a doubled double-quote (`""…""`), not `^^`.

> **Grid-table block cells.** Both renderers render every child block of a cell, so a cell holding a
> list or multiple paragraphs renders fully; a single-paragraph cell takes a styled/aligned fast-path.
> Covered by tests in both `BlockRenderer` and `MarkdownFlowDocument`.

> Note: in `MarkdownFlowDocument` (the selectable path), definition lists, figures, footers,
> alert blocks, math and diagrams are rendered via the `BlockRenderer` UIElement fallback, so they
> display correctly but are **not text-selectable**. Headings, paragraphs, lists, code, quotes and
> tables are fully selectable. The selectable path has table tests (`MarkdownExtensionsTests`);
> its non-table block rendering is otherwise untested.

---

## Diagrams — sub-support

Diagram fences are intercepted by [`DiagramRenderer`](../src/Nexaflow.Visuals.Text/Markdown/DiagramRenderer.cs)
and drawn natively in WPF (no JS/Mermaid.js, no browser).

**Languages:**

| Language | Status | Tests |
|---|---|---|
| `nomnoml` | ✅ | ❌ — no test or sample fixture |
| `mermaid` | ⚠️ Partial | ✅ — see sub-types below |

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
| `timeline` | ❌ raw source | ❌ |
| `journey` | ❌ raw source | ❌ |
| `C4Context` | ❌ raw source | ❌ |
| `block-beta` | ❌ raw source | ❌ |
| `architecture-beta` | ❌ raw source | ❌ |

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

Mermaid `--- … ---` front-matter (title/config) is stripped and a title applied
(`MermaidFrontmatter`); this is tested directly (`DiagramParsersTests` →
`Frontmatter_*`, and `DiagramRendererTests.Frontmatter_PieRoutesToChartNotSourceText`). The `config:` block is
discarded for every diagram **except `xychart`, `radar-beta`, `ishikawa-beta`, `sankey`, `erDiagram` and `venn-beta`**,
which re-read it (via `MermaidFrontmatter.RawBlock`) and apply their `config:` options described above.
A document-level YAML front-matter block is handled separately (`UseYamlFrontMatter`, parsed but
not rendered — see the extensions table above); this Mermaid front-matter is a different, fence-local
mechanism.

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

Tests live in `Nexaflow.Tests.Core` (the renderer ships in `Nexaflow.Visuals.*`,
covered by the Core test project):

| File | Covers |
|---|---|
| [`Visuals/Markdown/MarkdownPipelineFactoryTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownPipelineFactoryTests.cs) | Pipeline parses pipe tables, math blocks, diagram fences; singleton reuse. |
| [`Visuals/Markdown/BlockRendererTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/BlockRendererTests.cs) | Per-block render (headings incl. setext, paragraph, HR, quote, lists incl. nested/loose, indented + fenced code, table, diagram dispatch, math block) **and the full CommonMark inline layer** (inline code, emphasis, strong, links, reference links, autolinks, images local + remote, line breaks, escapes, entities, raw-HTML drop). (UI category.) |
| [`Visuals/Markdown/MarkdownViewTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownViewTests.cs) | `MarkdownView` populates its block panel. (UI category.) |
| [`Visuals/Markdown/MarkdownExtensionsTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownExtensionsTests.cs) | Enabled extensions (grid tables, task lists, emphasis extras, auto links, definition lists, list extras, abbreviations, alert blocks, figures, footers, citations, inline math) + expanded pipe-table edge cases + selectable `MarkdownFlowDocument` tables. (UI category.) |
| [`Visuals/Markdown/DiagramRendererTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/DiagramRendererTests.cs) | WPF render smoke tests for quadrant + sequence; state/class/requirement + kanban routing; XY chart (vertical/horizontal, per-point labels, front-matter config); radar (circle/polygon graticule, keyed curve, front-matter config); ishikawa (fishbone routing, front-matter config); sankey (CSV routing, front-matter config + node colours); ER (graph routing, word-cardinality + front-matter config); venn (circle routing, three-set + custom palette + front-matter config); front-matter pie routing. (UI category.) |
| [`Unit/Markdown/DiagramParsersTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/DiagramParsersTests.cs) | WPF-free parser tests: quadrant, sequence (extensive), flowchart, gantt, git graph, mindmap, state, class, requirement, kanban, XY chart + `XyChartConfig` (layout/axis/theme keys, `plotColorPalette`), radar + `RadarConfig` (axes, positional/keyed curves, options, geometry/styling/`cScale`), ishikawa + `IshikawaConfig` (head/category/nested-cause indentation, `diagramPadding`), sankey + `SankeyConfig` (CSV quoting/doubled-quotes/comments, shared nodes, enums + `nodeColors`), ER + `ErConfig` (symbol/word cardinality, identification, attributes/keys/comments, aliases, `layoutDirection`), venn + `VennConfig` (sets/unions/sizes, indented + explicit text, styling, `venn1…8` palette), front-matter. |
| [`Visuals/Markdown/MarkdownSampleRenderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownSampleRenderTests.cs) | End-to-end: every diagram in the sample dataset parses + renders, plus the `extensions.md` sample (emphasis extras, abbreviations, alert blocks) renders every block. (UI category.) |
| [`Unit/Markdown/MarkdownBlocksTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/MarkdownBlocksTests.cs) | **Editor** block model (split/join/compact) — *not* renderer coverage. |
| [`Unit/Markdown/HtmlToMarkdownTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/HtmlToMarkdownTests.cs) | **HTML→markdown paste** conversion — *not* renderer coverage. |

Sample fixtures (driving `MarkdownSampleRenderTests`) live in
[`Nexaflow.Tests.Fixtures/MarkdownSamples.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/MarkdownSamples.cs):
the `mermaid-*` diagram docs (pie, flowchart, quadrant, sequence, gantt, git graph, mindmap, state, class,
requirement, kanban, xychart, radar, ishikawa, sankey, er, venn) and `extensions.md` (YAML front matter, emphasis extras, abbreviations, alert blocks).

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
- **Several Mermaid families fall back to raw text** (timeline, journey,
  C4, block, architecture).

If any of the disabled extensions are wanted, the change is usually a one-line
`.UseX()` in `MarkdownPipelineFactory` **plus** renderer cases in both `BlockRenderer`
and `MarkdownFlowDocument` (and, ideally, a sample + test in `MarkdownSampleRenderTests`).
