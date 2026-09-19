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
| `src/Nexaflow.Markdown/Mermaid/<Type>/<Type>Grammar.cs` | `IMermaidGrammar`: what each line says, read through `MermaidLine`; what a new line starts as (`Blank`); what it writes across several lines rather than one (`Stretches`); what typing escapes (`Escaping`); the names a rename carries (`Names`, `Naming`); the stages it runs (`Stages`) and where holes stand (`Holds`) | `PieGrammar`, `VennGrammar`, `RadarGrammar` |
| `…/<Type>/<Type>Kinds.cs` | The kinds of the diagram's own lines, and their roles. The shapes lines are made of — names, labels, numbers, styles — are `MermaidKinds`' | `PieKinds`, `VennKinds`, `RadarKinds` |
| `…/<Type>/Stages/*.cs` | `IAstStage`s: what lines mean together, worked out and hung underneath as facts | `ResolveSlices`; `GroupRegions`, `ResolveRegions`; `ResolveCurves` |
| `…/<Type>/<Type>Config.cs` | The front matter's options, from `MermaidConfig.Diagram(name)`, `Theme`, `DiagramTheme(name)` and `Shared` | `PieConfig`, `VennConfig`, `RadarConfig` |
| `…/<Type>/<Type>Diagram.cs` (or `Chart`) | The model: the tree read back into what it describes, every part kept. `Of(MermaidBlock)`. The title is `MermaidBlock.Title` | `PieChart`, `VennDiagram`, `RadarChart` |
| `src/Nexaflow.Visuals.Text/Markdown/Mermaid/<Type>/<Type>Builder.cs` | `MermaidBuilder<TDiagram>`: `Of` reads the model, `Draw` draws it at the origin; a `<Type>Piece` class names its pieces | `PieBuilder`, `VennBuilder`, `RadarBuilder` |
| `MermaidDiagrams.Grammar` · `MermaidBuilders.For` | Where the diagram is named — both, or neither | |

**A type Mermaid reads as another shares its grammar and its model.** A swimlane is a flowchart laid out in lanes, and Mermaid reads
the two with one parser and draws them with one renderer; so `MermaidDiagrams.Grammar` names `FlowchartGrammar` for both,
`SwimlaneBuilder` derives from `FlowchartBuilder` and says only that its outermost subgraphs are lanes, and only what the lanes
themselves ask for is its own (`SwimlaneConfig`). Its tests are its own either way — the grammar contract over `swimlane-beta` blocks,
and a builder's over what it draws.

The builder's base draws everything round the diagram: the title (a `title` line, a header's title, or the front
matter's), what could not be read set beneath it, the card, and the element the block is shown and written in — which
is read-only where the host takes no edits (`DiagramRenderOptions.ReadOnly`, which a viewer sets and an editor does
not), leaving a diagram there looked at, selected and followed where it leads.
`MermaidDiagramHandler` asks `MermaidBuilders` first, so a diagram named there never reaches its legacy renderer.

## The kit

The kit's files sit directly in `src/Nexaflow.Markdown/Mermaid/` and `src/Nexaflow.Visuals.Text/Markdown/Mermaid/`; a
diagram's own code sits in a folder of its own under each.

### Reading

| To | Use |
|---|---|
| read a statement written across several lines — a note written until its `end note` | `MermaidStretch`, named in the grammar's `Stretches`: the parser finds the stretch, from the line that opens one to the line that ends it, and hands each line back to be read; the whole of it is one line of the tree |
| read a line: its keyword, words, space and tokens, and a `%%` comment closing it | `MermaidLine.Of`, `Keyword`, `Word`, `Token`, `Space`; `Spaced`, `Past`, `Sees`, `Next` to look ahead — `Keyword` and `Word` take what carries a word on, for one punctuation may close (`complex-->clear`) |
| read a title | `MermaidLine.Title` — every builder sets it over the diagram |
| read a name, bare or in quotes, or names with a separator between — each item more than its name where `item` says, a name still to write before `ends` | `Name`, `Names` |
| read a label in brackets — `[…]`, `(…)`, `{{…}}` — quoted or bare | `Label(open, close, role)` |
| read a number after a separator, still to come or written — to the end, `until` a character or a `stop` token ends it | `Room`, then `Amount` with `MermaidNumber.Positive` or `Where` |
| read what an option is set to — a word from a few, `true` or `false` | `Setting` |

| read or write a date in a day.js format, a length of time, or a date in a d3 axis format | `MermaidDate.Read`/`Write`, `MermaidDuration.Read`/`After`, `MermaidTimeFormat.Write` |
| say what is wrong with a piece as a whole — braces never closed | `Close(kind, role, trouble)` |
| read `key: value` properties — a style's, metadata closed by a brace, or options written one after another with only space between them | `Properties(known, ends, what, spaced)`, and `MermaidStyle.With` in the model |
| read words to where they end — the rest of the line, `until` a character or a `stop` token | `Words` |
| hold the line, or the rest of it, as written with the reason | `Shown`, `Held` |
| try one reading and go back | `Save`, `Restore`, `Since` |
| read the tree the builder draws from | `MermaidParser.Read(source, holes)` |
| read the tree back in a stage or model | `MermaidParts`: `Stated`, `Indented`, `Fact`, `Inner`, `Hole`, `Words`, `Named`, `SaidNames`, `Number` |
| say which group each line is in — a timeline's sections, a journey's, a Cynefin diagram's domains | `MermaidGrouping.Under`, hung as a fact the model reads back |
| say what each line is inside where groups nest and close with a word of their own — a block diagram's composites | `MermaidNesting.Inside`, hung as facts naming the group a line is in and the one it opens |
| read a node as an id and a label in the brackets that say its shape | `MermaidOutline.Node` with `MermaidShapes.Brackets` — `spaced: false` where a diagram writes several nodes to a line, `ends` where a rule of the diagram's own says where the id stops — then `MermaidShapes.Of` for the shape that was written, or `MermaidShapes.Named` for one `@{ shape: … }` names |
| read a link between two nodes, and what its characters draw | `MermaidLinks.At` — whether one is written there at all, and whether it is the whole of one or the opening of a labelled one — then `MermaidLinks.Of` for its heads, its line and how many ranks it reaches |
| read the `classDef`, `class` and `style` lines, and work out what everything is styled with | `MermaidStyling`, given the diagram's own roles: `Defined`, `Applied`, `Styled`, then `Styles` in the model and `Resolve` from a stage |
| read words to where a rule of the diagram's own says they end, rather than to a character | `MermaidLine.Words(role, end)` |
| lay a style over the classes something is given | `MermaidStyle.Over` |
| read the front matter | `MermaidConfig.Diagram`, `Theme`, `DiagramTheme`, `Swatches` (from `first`), `Size`, `Number`, `Flag`, `List` (in brackets, or the lines under the key) |
| escape what is typed where it cannot go as it is | `MermaidWriting.Escape` — quotes, bare names, labels in brackets; `MermaidWriting.Only` where a name cannot be quoted at all and what it cannot hold is dropped |
| read a value in quotes that may hold a quote of its own, written twice | `MermaidLine.Quoted` with `doubled` — a CSV field |
| read what a value in quotes says, entity codes and all | `MermaidText.Bare`, `Decode` |

### Drawing

| To | Use |
|---|---|
| set words: written (typed into), worked out (pressed), or a hole | `Written` / `Worked` on the builder → `DiagramWords`, placed with `Set` |
| colour anything | `Ink` (`DiagramInk`): `Written`, `Series`, `Over`, `Faded` — a colour nobody wrote is the theme's |
| draw a legend | `DiagramLegend` of `DiagramKey` rows; `Square` for its swatches' size |
| draw a closed shape through points, straight or rounded as Mermaid rounds it | `DiagramCurve.Closed` |
| draw an open curve between two points, bowed through a third | `DiagramCurve.Bowed` |
| set the title in the front matter's colour and size | override `TitleColour`, `TitleTextSize` |
| draw a node in the shape Mermaid's brackets say | `DiagramShapes.For` — the drawn shape a `MermaidShape` comes to |
| say what a piece stands in where other pieces are drawn over it | `build.Occupies` of its shape less `DiagramShapes.United` of theirs — a group of them would stand wrong, a line's band being wound the other way round from a rectangle |
| write what is said on a connector over the middle of it | `DiagramConnector.Room` for the room it takes, worked out before anything is drawn so what is under it does not stand there, then `DiagramConnector.Says` to draw it on a patch of the card's colour |
| draw a node: a shape with words in it | `DiagramShapes.Draw` — its words in the middle, or several placed where the diagram puts them, less what else is drawn over it; `Around` sizes a shape for its words, `Edge` is where a line meets it, `Clear` is where a shape of your own stands with words over it |
| set words that wrap to a width, breaking where a `<br>` says to, each line typed into as the characters it holds | `Wrapped` |
| lay a tree out tidily — children beside their parent, the root's either side | `DiagramTree.Lay` |
| lay nodes joined by lines out in ranks — a flowchart, a state chart | `DiagramLayers.Lay` of `DiagramCell`s and `DiagramJoin`s: ranks by how far the links reach, an order that keeps few lines crossing, boxes laid out in their own space and run their own way, and a route for every line — turning in the air between two ranks rather than between the middles of what it joins, running alongside a rank it passes rather than across what is in it, and bowed aside from any other line joining the same pair |
| lay the same out in lanes — a swimlane | `DiagramLanes` of `DiagramLane`s, given to `DiagramLayers.Lay`: each cell keeps to the band its `DiagramCell.Lane` names, a lane's cells come one to a rank, and a link handed between two lanes goes across rather than on. A lane is not a cell — it is the band its cells are laid out in, and comes back its `Bounds` and the `Strip` at the near end where its name goes |
| set a shape's words turned — a lane's name read up its band | `DiagramShapes.Draw` with `degrees`, which stands the words in the room the turn leaves them |
| draw a link written one of Mermaid's ways | `DiagramConnector.Headed` for what each end draws, `DiagramConnector.Stroked` for its line, `DiagramInk.Dashes` for a `stroke-dasharray`. Curved, the line passes through its route with each handle held to its own run and the runs at either end straight, so a corner is filleted and the line goes into a head along the head's own axis |
| gather what a diagram reaches and move it inside the box it takes | `DiagramRoom` — `Reach`, then `At` and `Size` |
| set the lines of a wrapped label, against a side | `DiagramWords.Stack`, `Placed` for a shape's own words, `Taken` for how much room they take |
| set words that may hold an entity code — drawn as what the code says, and so pressed rather than typed into | `Says` on the builder |
| put a band over each run of things sharing a group | `DiagramBand.Runs` |
| set words turned — an axis title read up the page | `DiagramWords.Set(…, degrees)`; a press, a caret and a wash come back through the turn (`Piece.Turned`) |
| read how far a line is indented, for a diagram nested by indentation | `MermaidParts.Indent` |
| read a diagram written as an outline of nodes — an id, a title in brackets, `::icon(…)` and `:::class` | `MermaidOutline.Node`, `Decoration`, `Escaping`, `Opening` |
| nest an outline's lines by their indentation — each under the nearest line indented less | `MermaidOutline.Nested` |
| draw an edge, a message, a relation | `DiagramConnector.Draw` with a `DiagramStroke` (`Dashed`, `Dotted`) and `DiagramHead`s; `Middle` places its words, `Band` is what a shape under it leaves out of its own |
| draw an axis and number it | `DiagramAxis.Draw` and `Room` with `DiagramTick`s — `line` and `tick` length as the config asks; `DiagramScale` for round-number ticks, `DiagramTime` for dates on round boundaries or every so many of a unit |
| round a panel between two axes — the room their numbers and titles take, the panel left inside it, the titles along it | `DiagramPanel.Room` for the axes' own room and `DiagramEdges` added for a key, a title band or a caption; `Round` for the panel, held to an `aspect` and shrunk to what was drawn; `Titles` for the turned upright title and the flat one under its numbers |
| draw gridlines across a panel | `DiagramGrid.Draw`, or `DiagramGrid.Lines` into a shape of your own where a diagram draws more than one set of them |
| say a piece leads somewhere | `build.Links` of a `LayoutLink` — a press on it means the link rather than a place for the caret, and the pointer is a hand over it. Held in a table beside the pieces rather than a slot on each, since almost nothing drawn is a link; followed by `LinkedElement`, which is what `MermaidBuilder.Host` shows a diagram in |
| show a block with nothing to draw | `AsWritten` |

**Only what draws is pressed.** A press lands on a leaf of the layout tree; a piece holding other pieces is pressed
through the leaves it draws. That is why `DiagramShapes.Draw` draws its outline as a `Shape` leaf beside its words — standing
in its outline less where the words are, so a press on them means them — and
why a region's circles and its words are layers of their own. **A piece that holds other pieces stands for the whole stretch they
were written in**: a swimlane's lane stands for its whole `subgraph … end`, not the line that opened it, so what is drawn in the lane
stands for a stretch of what the lane itself stands for. Where shapes overlap, a press means the one seen: a shape
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
| `MermaidBuilderContract` (Tests.Visuals) | Every block draws read, written in, wide and narrow, and while every character is typed; everything drawn stands inside the source; words a caret goes into are the characters written; the Markdown renderer shows it on the shared tree. It hands a diagram's own tests `Pieces`, `Written`, `Middle` and `Fill` to ask what it drew |
| `MermaidEditing` (Tests.Visuals) | What writing in a diagram is tested through: the block in a real editor, `PressPast` some words it draws, `Write` a keystroke |
| `MermaidKitRulesTests` (Tests.Markdown) | Every grammar is named in `MermaidDiagrams.Grammar` and tested against the contract |
| `MermaidBuilderRulesTests` (Tests.Visuals) | A diagram has a grammar and a builder, or neither; every builder is tested against the contract |
| `MermaidDiagramRulesTests` (Tests.Features.Architecture) | The legacy code is frozen — no file added, none grown, each marked; code on the shared tree never names it; a diagram's own reading and drawing go through the kit |
