# Aligning the content pipeline to its spec

[markdown-ast.md](markdown-ast.md) states the architecture as fifteen rules. This plan records where the code stands
against them and what aligning each part buys. It is the high-level shape only: each section below is picked up on its
own, planned in detail then, and finished before the next one starts.

## A class and a state diagram keep an address off the thing it applies to

Every language that lets a reader write an address marks it `Roles.Destination`, and what leads somewhere is a `Kinds.Link`
holding it — the shape prose writes a link in. Whoever draws one reads it off the tree and puts a `LayoutIntent` on what it drew
(`MarkdownBuilder.Linked`, `FlowchartBuilder.Answers`), so a press is answered by what it landed on rather than by walking the
tree again. Markdown answers because a link node holds its destination, and a reference-style link holds what the definition
said as a derived `Held` string. A flowchart answers because `ResolveChart` puts a `click` line's address on the node of the
graph it names.

Class and state diagrams have the same `click` line and no such reading, so the address stays on a statement of its own and
nothing that is drawn says where it leads. **A press on a class or a state link does nothing.** Their tests only assert what the
builder pinned to the piece, so they pass while the behaviour is gone.

Each needs what the flowchart has: match the node by the name it holds (`MermaidKinds.Name`), not by the role it was written
under — a link's ends name their nodes under roles of their own — and read the address from inside its quotes, where the words
carry the role.

## Nothing answers a fold

A chip drawn over what is past a diagram's frontier (`DiagramChip`, `DiagramSpill`) still pins `LayoutVerbs.Expand` or
`LayoutVerbs.Collapse` to itself, and `FlowchartBuilder` still pins `LayoutVerbs.Select` to a node where folds are
configured. Nothing answers any of the three, so pressing a chip does nothing.

Every part of the answer is already the engine's: the view state it would write the opening into (`Opened`), the bound
object it would then tell (`Expand`), and the reading again that draws what is now shown. What is missing is the way a
language says what a press on one of its own pieces comes to — a fold changes how content is shown rather than what it
says, and `IOnEdit` answers with source writes. `ContentChange.Asks` is the shape that fits: a chip's press would ask the
engine for the fold it already knows how to make.

## What already holds

Named first, because the list below is otherwise easy to read as "everything is broken".

- **R3, R4 — nothing reads the display.** No builder touches `DpiScale`, `PresentationSource`, `VisualTreeHelper` or
  `SystemParameters`. A layout is made in the content's own units and the element scales it, as specified.
- **R14 — no builder names the language table.** Held, and enforced by `MermaidDiagramRulesTests`.
- **R5, R6, R10 — the AST guards are in place.** `AstPipeline` checks Print-preservation between stages, and each
  language has its own pipeline (`TexPipeline`, `AbcPipeline`, `LilyPondPipeline`, `SmilesPipeline`, `PlotPipeline`)
  running through it. The corpora read bytes and never re-encode.
- **R8 — no builder writes to the AST.** The builders that mutate something during a build mutate a model of their
  own, not the reading they were handed.
- **R7's accessibility half.** Every language node type is `internal`.
- **One spelling for a link.** `Kinds.Link` and `Roles.Destination` are shared, so prose, a chart's node and a diagram's
  box all say where they lead the same way, and `MarkdownLinks.Goes` reads any of them.
- **The builder seam is enforced.** `Nexaflow.Analyzers.Content` holds builders to one shape and one way of being
  made, with `ContentBuilderAnalyzerTests` behind it.

## 1. The engine's stopgap

**Goal.** Nothing that throws anywhere in the pipeline reaches the element, and a failure costs only the part that
failed.

**Where it stands.** Held. `ContentEngine.LaidOut` wraps the whole of `Lay`, and what nothing could read is laid by
`UnreadBuilder` over a one-node error AST holding the source, with the reason as that node's trouble — a builder rather
than a call to `SourceShown`, so that the fallback's own failure lands in the catch every builder already has, over the
simplest tree there is. Every path into the pipeline goes through it, the picture included. Nested content is caught at
the block it is in, so a fence in a language that falls over is shown as written with why inside a document that still
draws as a document. The three places the engine asks a language something outside that catch — a block's corner, a
context menu, what a drop means — answer with nothing rather than throwing, which is the one place in the pipeline where
swallowing is right: there is nothing to show a reader about a button that failed to appear. A test stands at each place
that can throw: reading, working over, binding, being handed a builder that is not one, a nested reading, and a corner.

**What is left out, on purpose.** The edit path. `Edited`, `Made`, `Safe` and `ContentEngine.Read` run a language's code
with no catch, and a throw there loses a keystroke or leaves a document half-written rather than blanking a render. That
wants the opposite answer — refuse the edit and leave the document as it stands, not show it as source — so it belongs
to section 3, where the write half is designed. The message stays a stopgap too: `This could not be read: <message>` is
what a reader gets where nothing detected the fault, and a parser, stage or builder that says something useful about it
is still better.

**What it bought.** The largest reduction in blast radius for the smallest change in the plan. A document no longer
looks broken because one diagram in it is, the fallback can no longer take the window with it, and the rest of this plan
is safe to carry out: sections 2 and 3 move code that reads half-typed input, and during that work things will throw.

## 2. Reading out of the builders and into stages

**Goal.** Every semantic fact a builder needs is on the reading it is handed, said in its language's own nodes by a
stage. A builder walks a finished reading and decides only where things go. R12 and R13 become true, and become
enforceable.

**Where it stands.** This is the deepest divergence, and it is a pattern rather than a set of slips. Nine diagram
builders still carry a private reader model of their own — `ArchitectureBuilder`, `BlockBuilder`, `C4Builder`,
`ClassBuilder`, `ErBuilder`, `RequirementBuilder`, `SequenceBuilder`, `StateBuilder`, `VennBuilder` — five of them in a
`*Builder.Reading.cs` partial whose name says what it is doing. Those models are
built by walking the AST and working out what it means, which is stage work sitting in the wrong component. It shows
up in three concrete ways:

- **Source spans computed from offsets.** Seven builder files build `new SourceSpan(opening.Start, closing.End -
  opening.Start)` and its variants, while `Nexaflow.Markdown` — where spans belong — mentions `SourceSpan` three
  times in total. The builders are reconstructing which characters a construct covers because nothing gives it to
  them.
- **Identity derived from an offset.** `ErBuilder`, `RequirementBuilder` and `SequenceBuilder` name an unwritten thing
  by appending a source offset to a prefix, so a node's identity moves when text above it changes.
- **Relationships decided by comparing offsets.** Which side of a boundary a group marker was written
  (`ArchitectureBuilder`), whether a centre marker came before or after an arrow (`SequenceBuilder`), whether a part
  falls inside a cell (`LatexBuilder`), whether a part is inside the stretch shown as typed (`MermaidBuilder`) — all
  answered with `<` and `>` on offsets.

**The flowchart is done, and says what the shape of the rest is.** `ResolveChart` reads the chart as the graph it is —
every node once, what it says, the shape it is drawn as, the subgraph it belongs to, where it leads, and every
connection by the names of its two ends. `FlowchartBuilder` walks that reading: it keeps no index of its own, works
nothing out from a `click` line or an `id@{ … }` line, and decides nothing about what the chart holds.

What that taught, and what the shape of this section has to account for: **a stage has no positions**, because a
derived part stands for no characters at all. So "delete the `.Reading` partial" is not reachable as written. What a
stage can say is what exists, what it is called, what it says, what it amounts to and what joins what; what is left is
finding the characters each of those was written as — the mentions of a node by its id, the links in the order they are
written, the metadata about each. That is position work and not semantics, and it is all that
`FlowchartBuilder.Reading.cs` still does. The target for each of the remaining nine is the same: a `.Reading` partial
that resolves positions and decides nothing. Giving a reading a way to name written parts directly would close even
that, and wants its own section.

**Benefit.** The most of any section, and most of it compounds:

- Those facts become testable without WPF. A stage test asserts what a diagram *means*; today the same assertion needs
  a builder, a style, a room and a layout tree.
- They stop being recomputed on every lay. Block reuse (`WithUnchanged`, `LaidBlocks`) only pays when laying is cheap;
  a builder that re-reads its diagram from scratch each time spends the saving before it is made.
- Identity stops moving when unrelated text changes, which is what a saved reference, a selection and an undo step all
  depend on.
- R13 becomes checkable by an analyzer, because a builder that needs no offsets can be forbidden from having any.
- It is the prerequisite for section 5. A builder that does not read can be moved across an assembly boundary; a
  builder holding half of its language's semantics cannot.

**Shape.** One diagram at a time, each a self-contained change: name the facts its reader model works out, move them
into stages saying them in that diagram's own nodes, reduce the builder to resolving positions. Nine of these left,
plus `LatexBuilder`'s cell containment and `MermaidBuilder`'s raw-zone test, which is shared by every diagram and so
should come before the rest.

**Depends on.** Section 1, so that a part-migrated language still draws something.

## 3. The parser's write half

**Goal.** One component per language that both parses `string → AST` and splices `(AST node, change) → source edit`.
Editing handlers name an intent — flatten this note, set this value, insert a row — and the parser spells it. R9
becomes true, and syntax knowledge exists in exactly one place per language.

**Where it stands.** The read half exists everywhere, and the splice interface now exists too: `ITranspile`, in
`Nexaflow.Markdown/Editing/`, which a parser implements to be asked about a change before it is written. Three languages
implement it — `MermaidParser` and `AbcParser`, the pair this section asks the interface to be designed against, and
`FlowchartParser` — and the engine puts every write marked `ContentWrite.Meant`, words as the reader means them rather
than as the language spells them, to the parser, instead of only words pasted into a part. `ContentChange.Asks` lets a
handler decline to spell an edit and ask the engine for the one it would make anyway, which is the first step of a
handler shrinking to an intent.

**A language owns its own parser, and borrows what it needs.** The flowchart reads its own block — `FlowchartParser`,
named by `Shipped.Diagram` for a flowchart and a swimlane — rather than being read by the kit's shared one, and reuses
`MermaidParser`'s internals for the frame every Mermaid block shares: the fences, the front matter, the header, the
accessibility lines, the bindings, a line and its trivia. What it does not share is how a statement ends, because a
flowchart's does not end at a line: a quoted string spans lines, so `a@{ label: "File\n Handling" }` is one statement
and the words in it one run. Read line by line it was silently a node called `Handling`. The shape to copy for the next
language is this one — its own `Parse` and its own `Rewrite`, over shared helpers — not one parser every diagram has to
fit.

**The copied frame loop is deliberate.** A diagram with its own parser copies that loop rather than sharing one, and
that duplication is the point: it is what lets one diagram's editing change without touching any other. Editing is
where the diagrams genuinely differ — a statement, an id, a label and a style line each mean something different per
diagram — so a diagram owns the reading of its own statements and pays about sixty copied lines for the frame around
them. Those lines are not debt, and consolidating them is not an improvement. Rendering is the opposite case: shared
structure works there, and stays.

**What is shared is the check, not the code.** `MermaidDiagrams.ParserFor` is the single place naming the parser a
diagram is read by; `Shipped.Diagram` and the test helpers both ask it. `MermaidGrammarContract` reads each block
through it, so every parser — not only the shared one — is held to printing back exactly what was written, and a
diagram takes its own parser into its tests the moment it has one.

What has not started is the migration this section is actually about. The twenty-four `IOnEdit` handlers still compose
syntax themselves, and `PieEdits.Configured` is still the clearest example: it assembles front-matter syntax by
concatenating delimiters and newlines, and calls `Print()` on the AST to re-emit the rest of the block.

What that costs is already measured, and red, and has not moved.
`DiagramEscapingTests.WhateverIsTypedWhereSomethingIsWrittenTheDiagramStillReads` types every key into every written
part of every sample diagram and names sixty-two that leave a line the diagram can no longer read — a quote in a C4
value or a class stereotype, a bracket in a C4 name, a quote in a Sankey label. It is the acceptance test for this
section: sixty-two when this plan was written, sixty-two now.

**Benefit.** Escaping and delimiter bugs stop being a thing each handler can get wrong and become a property of the
language. `ITranspile`'s rule — where the part cannot hold what was asked for, nothing is written — applies to every
edit rather than to pasted words in one language. The twenty-four handlers shrink to gesture-to-intent
maps, which is both less code and code that can be tested without a parser. And a new gesture, or a new ribbon
option, stops being a new opportunity to corrupt a document.

**Shape.** Design the splice interface once, against two languages that stress it differently (Mermaid's front matter,
ABC's four-part note). Then per language: move its handler's syntax into its parser, reduce the handler to intents.

**Depends on.** Section 1. Independent of section 2, though doing 2 first makes some handlers simpler to move.

## 4. One reader per language node type

**Goal.** A language's node types are written only by its stages and read only by its builder, as R7 says.

**Where it stands.** Held everywhere except one place: `PieEdits` reads `PieSliceNode`. Every other node type is
touched only by its language's stages, its builder and the stages' tests.

**Benefit.** Small on its own, and worth doing anyway: it keeps the contract narrow, so a stage and its builder stay
free to change what they say to each other without an edit handler breaking. It also removes a coupling that would
otherwise have to be carried across the boundary in section 5.

**Shape.** One change. The fact `PieEdits` wants comes from the part or the intent, not from the stage's node.

**Depends on.** Nothing, but it is cheapest done alongside Pie's share of sections 2 and 3.

## 5. A language in one place

**Goal.** A language is one folder: parser, stages and builder together, with the layout tree and the renderer a
shared thing it uses. `InternalsVisibleTo` stops being load-bearing.

**Where it stands.** The layout tree needs WPF and `Nexaflow.Markdown` is WPF-free, so every builder lives apart from
the language it belongs to, in `Nexaflow.Visuals.Text`, reaching its language's `internal` node types across an
assembly boundary. R7's "internal to the assembly its stages live in" is read across that boundary rather than
enforced by it.

**Benefit.** A language becomes reviewable in one place — the thing that makes a new one cheap to add and an existing
one safe to change. Layout becomes testable without WPF, which brings the layout snapshot guards into the fast suite.
And the boundary starts enforcing R7 instead of documenting it.

**Shape.** Two pieces, in order: a layout tree that is WPF-free and still fast enough to paint at scroll speed, in
`Nexaflow.Visuals`; then the builders move back beside their languages. The first is a substantial piece of work in its
own right and needs measuring before it is designed.

**Depends on.** Section 2 — a builder that still reads cannot move. Largest in the plan, and last.

## 6. Making the spec enforceable

**Goal.** Each rule is checked by the build, not by review. "Aligned" becomes a test result.

**Where it stands.** Three rules are enforced: the builder's shape and how it is made (`Nexaflow.Analyzers.Content`),
that no builder names the language table (`MermaidDiagramRulesTests`), and Print-preservation between stages
(`AstPipeline`, in Debug). R1, R2, R12 and R13 rest on review.

**Benefit.** The one section that stops the others being undone. Everything else on this list is a correction; this is
what makes the correction hold, and it is what turns a disagreement about whether a change is aligned into a build
failure with a rule number on it.

**Shape.** An analyzer per mechanical rule, added as its rule becomes true rather than before. R13 is the valuable one
and the easiest to state: a builder may not read an offset. R12 follows from it. R1 and R2 are judgements and stay
review's, but R2's half — that a layout piece may point nowhere — can be held by a test.

**Depends on.** Each analyzer depends on its rule already holding, so this section is spread through the others rather
than done at a point.

## 7. A tune is typed into where it is drawn

**Goal.** What [markdown-ast.md](markdown-ast.md#7-nothing-ever-fails-to-draw) says of a tune: its title and the words
under its staff written into as text, its notes written into as notes. Typing `a` to `g` in the staff adds that note,
`_` and `#` flatten and sharpen the note before it, Page Up and Page Down move it an octave, `+` and `-` make it longer
and shorter, and Space puts a pause in.

**Where it stands.** The handler's half is held, and it is the worked example of the single edit path. `AbcEdits` maps each key to a gesture
and `Shipped.Abc` declares it as ABC's `Editing`, alongside `Writable` and `Transpile`. `AbcEdit` answers `NoteAt` (what
a letter typed at the caret spells, carrying the octave and length of the note before it), `Octave`, `Accidental` and
`Length`, each given the notes as the engine read and laid them and answering with a `ContentChange` naming the stretch
of source each note was written in. `AbcParser` says how words are written back into a tune (`ITranspile.Rewrite`): a
break becomes a space, a percent on a field's line is held by a backslash, and a character that would close an
annotation or a decoration is refused.

Three things make that one path rather than two:

- A gesture is told the **staged** tune, standing where it stands in the document, so a note knows both what it sounds
  and which characters it was written with. Flattening an F in G major writes `=F` only because a stage put the key
  signature on the note; on a bare parse it would write a flat the key would sharpen straight back.
- A gesture answers with the stretches to write and never with a tree. The engine writes them and reads the tune again,
  so nothing has to hold a tree whose stages no longer describe it. `AstWrite` — a tree handed back to be printed and
  read — is gone, and with it the second path.
- What is words as the reader means them goes through `ITranspile` before any of it is written; what a gesture spelled
  itself is already ABC and goes as it stands.

What is left is the layout's, not the handler's: nothing yet proves a caret put on a title or on the words under a
staff lands against a piece and so reaches the engine's default at all. `AbcEdits` says nothing about either, which is
what hands them over — `AbcEditsTests` asserts that much — but being handed over is only half of it.

**Benefit.** The keys a musician expects, and the shape every other language's handler is written against. It is also
the cheapest test of whether the engine's default and a language's own handler compose: the title and the words under
the staff are the default's, the staff is the handler's, and neither needs to know about the other.

**Depends on.** Nothing.

## Order

1. **§1, the engine's stopgap** — held. Nothing waited on it, and everything else is safer for it.
2. **§2, reading out of the builders** — the deepest, and everything except §3 waits on it. The flowchart is done;
   `MermaidBuilder`'s shared raw-zone test next, then one diagram at a time; §4 rides along with Pie.
3. **§3, the parser's write half** — the splice interface is designed and proved against two languages, so this can
   run beside §2 now. What is left is the handlers, one language at a time.
4. **§6's R13 analyzer** — as soon as §2 makes it true for a language, so the next language cannot regress.
5. **§5, a language in one place** — after §2, and after the WPF-free layout tree is measured and designed.
