# Markdown Support

What the Nexaflow markdown renderer (`Nexaflow.Visuals.Text`) currently supports,
checked against the Markdig [CommonMark](https://xoofx.github.io/markdig/docs/commonmark/)
and [extensions](https://xoofx.github.io/markdig/docs/extensions/) docs.

## How it's wired

- **Parser:** Markdig **1.3.1** (`Markdig` package).
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

> Two of these were silently broken until a test caught them: `<autolinks>` and
> `&entity;`/`&#nn;` references are distinct Markdig inline types (`AutolinkInline`,
> `HtmlEntityInline`) that `BlockRenderer.AddInlines` had no case for, so they hit the
> `default` branch and rendered the *type name* instead of the link/character. Both now
> have explicit cases and tests.

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

> **Citation delimiter fix.** The renderer's citation case checked `DelimiterChar == '^'`, but
> Markdig's `UseCitations()` emits `""text""` with `DelimiterChar == '"'`. So `^^text^^` was
> literal text and real `""…""` citations rendered as plain **bold**. Now corrected to `'"'`
> and tested.

> **Grid-table block-cell fix.** Both renderers previously read only `cell[0] as ParagraphBlock`,
> so a grid-table cell containing a list or multiple paragraphs rendered **blank**. Both paths
> now render every child block of a cell (single-paragraph cells keep the styled/aligned
> fast-path), covered by tests in both `BlockRenderer` and `MarkdownFlowDocument`.

> Note: in `MarkdownFlowDocument` (the selectable path), definition lists, figures, footers,
> alert blocks, math and diagrams are rendered via the `BlockRenderer` UIElement fallback, so they
> display correctly but are **not text-selectable**. Headings, paragraphs, lists, code, quotes and
> tables are fully selectable. The selectable path now has table tests (`MarkdownExtensionsTests`)
> but its non-table block rendering is otherwise still untested.

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
| `erDiagram` | ❌ raw source | ❌ |
| `timeline` | ❌ raw source | ❌ |
| `journey` | ❌ raw source | ❌ |
| `C4Context` | ❌ raw source | ❌ |
| `block-beta` | ❌ raw source | ❌ |
| `architecture-beta` | ❌ raw source | ❌ |

**State-diagram sub-features** ([`MermaidStateParser`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/Parsers/MermaidStateParser.cs)).
State diagrams reuse the shared graph model, the Sugiyama layout and `WpfGraphRenderer` (new pseudostate
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
new **`ClassBox`** node ([`ClassBox.cs`](../src/Nexaflow.Visuals.Text/Markdown/Graphs/ClassBox.cs)) drawn as a
UML box with name / attribute / method compartments; relationships are edges whose new
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
`WpfGraphRenderer` — a «type» stereotype + name header over a *single* field compartment (the new
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

Mermaid `--- … ---` front-matter (title/config) is stripped and a title applied
(`MermaidFrontmatter`); this is tested directly (`DiagramParsersTests` →
`Frontmatter_*`, and `DiagramRendererTests.Frontmatter_PieRoutesToChartNotSourceText`).
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
| [`Visuals/Markdown/DiagramRendererTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/DiagramRendererTests.cs) | WPF render smoke tests for quadrant + sequence; front-matter pie routing. (UI category.) |
| [`Unit/Markdown/DiagramParsersTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/DiagramParsersTests.cs) | WPF-free parser tests: quadrant, sequence (extensive), flowchart, gantt, git graph, mindmap, front-matter. |
| [`Visuals/Markdown/MarkdownSampleRenderTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Visuals/Markdown/MarkdownSampleRenderTests.cs) | End-to-end: every diagram in the sample dataset parses + renders, plus the `extensions.md` sample (emphasis extras, abbreviations, alert blocks) renders every block. (UI category.) |
| [`Unit/Markdown/MarkdownBlocksTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/MarkdownBlocksTests.cs) | **Editor** block model (split/join/compact) — *not* renderer coverage. |
| [`Unit/Markdown/HtmlToMarkdownTests.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Core/Unit/Markdown/HtmlToMarkdownTests.cs) | **HTML→markdown paste** conversion — *not* renderer coverage. |

Sample fixtures (driving `MarkdownSampleRenderTests`) live in
[`Nexaflow.Tests.Fixtures/MarkdownSamples.cs`](../src/Nexaflow.Tests/Nexaflow.Tests.Fixtures/MarkdownSamples.cs):
the `mermaid-*` diagram docs (pie, flowchart, quadrant, sequence, gantt, git graph, mindmap, state, class,
requirement) and `extensions.md` (YAML front matter, emphasis extras, abbreviations, alert blocks).

**Where coverage is thin:**

- **`MarkdownFlowDocument`** (the selectable path) is only tested for tables; its other
  block types (headings, lists, code, quotes) rely on the shared `BlockRenderer` but have
  no FlowDocument-specific assertions.
- **`nomnoml`** has neither a test nor a sample fixture.
- The base CommonMark renderer, the enabled extensions, and the Mermaid parser family are
  now all well covered.

---

## Known limitations / gaps worth flagging

- **No syntax highlighting** in code blocks — monospace only.
- **No remote images** — only local files load; remote URLs degrade to alt text.
- **Raw HTML is not rendered** — inline HTML is dropped, HTML blocks show as raw text.
- **No emoji shortcodes** (`:tada:`).
- **Task list checkboxes are display-only** — not clickable to toggle.
- **Several Mermaid families fall back to raw text** (ER, timeline, journey,
  C4, block, architecture).

If any of the disabled extensions are wanted, the change is usually a one-line
`.UseX()` in `MarkdownPipelineFactory` **plus** renderer cases in both `BlockRenderer`
and `MarkdownFlowDocument` (and, ideally, a sample + test in `MarkdownSampleRenderTests`).
