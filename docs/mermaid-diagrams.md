# Mermaid diagrams on the shared layout tree

A Mermaid diagram on the shared layout tree is read and drawn in four steps, like every other language the Markdown
renderer draws ([markdown-ast.md](markdown-ast.md)):

**grammar → stages → model → builder**, the builder emitting pieces of the layout tree, each standing for the part of
the source it was drawn from — which is what makes a diagram selectable, pressable and written in where it is drawn.

The diagrams still on the legacy renderers (`src/Nexaflow.Visuals.Text/Markdown/Graphs/`) move across one at a time, and
each is built from **the Mermaid kit**: the pieces every diagram shares, decided once. Pie, Venn and radar are the
references — pie the smallest whole diagram, Venn one with stages, names, styles and a layout of its own, radar one whose
lines list items with values and whose options share a line, xychart one with axes, ranges and series in brackets.

## What a diagram is made of

| File | What it is | Pie / Venn |
|---|---|---|
| `src/Nexaflow.Markdown/Mermaid/<Type>/<Type>Grammar.cs` | `IMermaidGrammar`: what each line says, read through `MermaidLine`; what a new line starts as (`Blank`); what typing escapes (`Escaping`); the names a rename carries (`Names`, `Naming`); the stages it runs (`Stages`) and where holes stand (`Holds`) | `PieGrammar`, `VennGrammar`, `RadarGrammar` |
| `…/<Type>/<Type>Kinds.cs` | The kinds of the diagram's own lines, and their roles. The shapes lines are made of — names, labels, numbers, styles — are `MermaidKinds`' | `PieKinds`, `VennKinds`, `RadarKinds` |
| `…/<Type>/Stages/*.cs` | `IAstStage`s: what lines mean together, worked out and hung underneath as facts | `ResolveSlices`; `GroupRegions`, `ResolveRegions`; `ResolveCurves` |
| `…/<Type>/<Type>Config.cs` | The front matter's options, from `MermaidConfig.Diagram(name)`, `Theme`, `DiagramTheme(name)` and `Shared` | `PieConfig`, `VennConfig`, `RadarConfig` |
| `…/<Type>/<Type>Diagram.cs` (or `Chart`) | The model: the tree read back into what it describes, every part kept. `Of(MermaidBlock)`. The title is `MermaidBlock.Title` | `PieChart`, `VennDiagram`, `RadarChart` |
| `src/Nexaflow.Visuals.Text/Markdown/Mermaid/<Type>/<Type>Builder.cs` | `MermaidBuilder<TDiagram>`: `Of` reads the model, `Draw` draws it at the origin; a `<Type>Piece` class names its pieces | `PieBuilder`, `VennBuilder`, `RadarBuilder` |
| `MermaidDiagrams.Grammar` · `MermaidBuilders.For` | Where the diagram is named — both, or neither | |

The builder's base draws everything round the diagram: the title (a `title` line, a header's title, or the front
matter's), what could not be read set beneath it, the card, and the element the block is shown and written in.
`MermaidDiagramHandler` asks `MermaidBuilders` first, so a diagram named there never reaches its legacy renderer.

## The kit

The kit's files sit directly in `src/Nexaflow.Markdown/Mermaid/` and `src/Nexaflow.Visuals.Text/Markdown/Mermaid/`; a
diagram's own code sits in a folder of its own under each.

### Reading

| To | Use |
|---|---|
| read a line: its keyword, words, space and tokens, and a `%%` comment closing it | `MermaidLine.Of`, `Keyword`, `Word`, `Token`, `Space`; `Spaced`, `Past`, `Sees`, `Next` to look ahead — `Keyword` and `Word` take what carries a word on, for one punctuation may close (`complex-->clear`) |
| read a title | `MermaidLine.Title` — every builder sets it over the diagram |
| read a name, bare or in quotes, or names with a separator between — each item more than its name where `item` says, a name still to write before `ends` | `Name`, `Names` |
| read a label in brackets — `[…]`, `(…)`, `{{…}}` — quoted or bare | `Label(open, close, role)` |
| read a number after a separator, still to come or written — to the end, `until` a character or a `stop` token ends it | `Room`, then `Amount` with `MermaidNumber.Positive` or `Where` |
| read what an option is set to — a word from a few, `true` or `false` | `Setting` |

| read or write a date in a day.js format, a length of time, or a date in a d3 axis format | `MermaidDate.Read`/`Write`, `MermaidDuration.Read`/`After`, `MermaidTimeFormat.Write` |
| say what is wrong with a piece as a whole — braces never closed | `Close(kind, role, trouble)` |
| read `key: value` properties — a style's, or metadata closed by a brace | `Properties(known, ends, what)`, and `MermaidStyle.With` in the model |
| read words to where they end — the rest of the line, `until` a character or a `stop` token | `Words` |
| hold the line, or the rest of it, as written with the reason | `Shown`, `Held` |
| try one reading and go back | `Save`, `Restore`, `Since` |
| read the tree the builder draws from | `MermaidParser.Read(source, holes)` |
| read the tree back in a stage or model | `MermaidParts`: `Stated`, `Indented`, `Fact`, `Inner`, `Hole`, `Words`, `Named`, `SaidNames`, `Number` |
| read the front matter | `MermaidConfig.Diagram`, `Theme`, `DiagramTheme`, `Swatches` (from `first`), `Size`, `Number`, `Flag`, `List` (in brackets, or the lines under the key) |
| escape what is typed where it cannot go as it is | `MermaidWriting.Escape` — quotes, bare names, labels in brackets |

### Drawing

| To | Use |
|---|---|
| set words: written (typed into), worked out (pressed), or a hole | `Written` / `Worked` on the builder → `DiagramWords`, placed with `Set` |
| colour anything | `Ink` (`DiagramInk`): `Written`, `Series`, `Over`, `Faded` — a colour nobody wrote is the theme's |
| draw a legend | `DiagramLegend` of `DiagramKey` rows; `Square` for its swatches' size |
| draw a closed shape through points, straight or rounded as Mermaid rounds it | `DiagramCurve.Closed` |
| draw an open curve between two points, bowed through a third | `DiagramCurve.Bowed` |
| set the title in the front matter's colour and size | override `TitleColour`, `TitleTextSize` |
| draw a node: a shape with words in it | `DiagramShapes.Draw` — its words in the middle, or several placed where the diagram puts them, less what else is drawn over it; `Around` sizes a shape for its words, `Edge` is where a line meets it, `Clear` is where a shape of your own stands with words over it |
| set words that wrap to a width, breaking where a `<br>` says to, each line typed into as the characters it holds | `Wrapped` |
| lay a tree out tidily — children beside their parent, the root's either side | `DiagramTree.Lay` |
| gather what a diagram reaches and move it inside the box it takes | `DiagramRoom` — `Reach`, then `At` and `Size` |
| set the lines of a wrapped label, against a side | `DiagramWords.Stack` |
| set words turned — an axis title read up the page | `DiagramWords.Set(…, degrees)`; a press, a caret and a wash come back through the turn (`Piece.Turned`) |
| read how far a line is indented, for a diagram nested by indentation | `MermaidParts.Indent` |
| read a diagram written as an outline of nodes — an id, a title in brackets, `::icon(…)` and `:::class` | `MermaidOutline.Node`, `Decoration`, `Escaping`, `Opening` |
| nest an outline's lines by their indentation — each under the nearest line indented less | `MermaidOutline.Nested` |
| draw an edge, a message, a relation | `DiagramConnector.Draw` with a `DiagramStroke` (`Dashed`, `Dotted`) and `DiagramHead`s; `Middle` places its words, `Band` is what a shape under it leaves out of its own |
| draw an axis and number it | `DiagramAxis.Draw` and `Room` with `DiagramTick`s — `line` and `tick` length as the config asks; `DiagramScale` for round-number ticks, `DiagramTime` for dates on round boundaries or every so many of a unit |
| show a block with nothing to draw | `AsWritten` |

**Only what draws is pressed.** A press lands on a leaf of the layout tree; a piece holding other pieces is pressed
through the leaves it draws. That is why `DiagramShapes.Draw` draws its outline as a `Shape` leaf beside its words — standing
in its outline less where the words are, so a press on them means them — and
why a region's circles and its words are layers of their own. Where shapes overlap, a press means the one seen: a shape
stands only in what the shapes drawn over it leave uncovered (a Venn circle less its unions' lenses, a radar curve less
the curves after it). A piece standing for a stretch nothing is written in yet stands for nothing — only its hole does.

## Converting a diagram

1. **Read Mermaid's syntax page for it** (`https://mermaid.ai/open-source/syntax/<type>.html`) and support all of it,
   not only what the legacy parser does. Read the legacy renderer only to see what the diagram draws today — never copy
   from it.
2. **The grammar and its kinds**, reading every line through `MermaidLine`, named in `MermaidDiagrams.Grammar`. Its
   tests derive from `MermaidGrammarContract` and list every construct, and what nobody means to write, in `Blocks`, and
   the documentation's examples in `DocumentedBlocks`.
3. **Stages** for what lines mean together, and `Holds` for where a hole stands while the diagram is written.
4. **The config and the model.**
5. **The builder**, named in `MermaidBuilders.For`: layers by how the diagram looks, each piece standing for the part it
   was drawn from. Its tests derive from `MermaidBuilderContract` and list what it draws in `Drawn`.
6. **Writing in place**: `Blank`, `Escaping`, `Names` and `Naming` on the grammar, with editing tests like
   `PieEditingTests` and `VennEditingTests`.
7. **Delete the legacy code** the diagram alone used — its parser, model and renderer, their tests, their dispatch arm in
   `MermaidDiagramHandler` — and those files' lines in `legacy-diagram-code.txt`.
8. **The rest of the product**: a section in `MarkdownSamples`, the help page's example and its figure
   (`MermaidFigureWriter`), the row in [MarkdownSupport.md](MarkdownSupport.md), and the product tree node with its
   snaplinks.

### When the kit lacks something

Add it to the kit — a file directly in the `Mermaid` folder, with its own tests — and use it from the diagram being
converted. What only one diagram will ever draw (a pie's wedges, the Venn layout) stays in that diagram's folder. A layout
the legacy code has, such as Sugiyama's, moves into the kit and takes the kit's own input. A mark the layout tree cannot
draw goes into the engine (`src/Nexaflow.Visuals.Text/Editing/`), not into a builder.

## What holds it

| Test | Holds |
|---|---|
| `MermaidGrammarContract` (Tests.Markdown) | Every block and every prefix prints as written; the grammar only copies; the documented blocks have nothing wrong; anything typed anywhere something is written still reads; every new line is one the grammar reads; a rename is written so its uses still read |
| `MermaidBuilderContract` (Tests.Visuals) | Every block draws read, written in, wide and narrow, and while every character is typed; everything drawn stands inside the source; words a caret goes into are the characters written; the Markdown renderer shows it on the shared tree |
| `MermaidKitRulesTests` (Tests.Markdown) | Every grammar is named in `MermaidDiagrams.Grammar` and tested against the contract |
| `MermaidBuilderRulesTests` (Tests.Visuals) | A diagram has a grammar and a builder, or neither; every builder is tested against the contract |
| `MermaidDiagramRulesTests` (Tests.Features.Architecture) | The legacy code is frozen — no file added, none grown, each marked; code on the shared tree never names it; a diagram's own reading and drawing go through the kit |
