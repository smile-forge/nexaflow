# Aligning the content pipeline to its spec

[markdown-ast.md](markdown-ast.md) states the architecture as fifteen rules. This plan records where the code stands
against them and what aligning each part buys. It is the high-level shape only: each section below is picked up on its
own, planned in detail then, and finished before the next one starts.

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

**Where it stands.** This is the deepest divergence, and it is a pattern rather than a set of slips. At least ten
diagram builders carry a private reader model of their own — `ArchitectureBuilder`, `BlockBuilder`, `C4Builder`,
`ClassBuilder`, `ErBuilder`, `FlowchartBuilder`, `RequirementBuilder`, `SequenceBuilder`, `StateBuilder`,
`VennBuilder` — four of them in a `*Builder.Reading.cs` partial whose name says what it is doing. Those models are
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
into stages saying them in that diagram's own nodes, reduce the builder to placement, delete the `.Reading` partial.
Roughly ten of these, plus `LatexBuilder`'s cell containment and `MermaidBuilder`'s raw-zone test, which is shared by
every diagram and so should be done first of the ten.

**Depends on.** Section 1, so that a part-migrated language still draws something.

## 3. The parser's write half

**Goal.** One component per language that both parses `string → AST` and splices `(AST node, change) → source edit`.
Editing handlers name an intent — flatten this note, set this value, insert a row — and the parser spells it. R9
becomes true, and syntax knowledge exists in exactly one place per language.

**Where it stands.** The read half exists everywhere. The write half is `ContentLanguage.SafeFormatText`, which is
narrower in three ways: it is implemented by one language (`MermaidParser`), called from one place
(`ContentEngine.Editing`), and only for the case of words pasted into a part. Everything else is composed by the
handlers, of which there are twenty-seven. `PieEdits.Configured` is the clearest example: it assembles front-matter
syntax by concatenating delimiters and newlines, and calls `Print()` on the AST to re-emit the rest of the block.

What that costs is already measured, and red.
`DiagramEscapingTests.WhateverIsTypedWhereSomethingIsWrittenTheDiagramStillReads` types every key into every written
part of every sample diagram and names sixty-two that leave a line the diagram can no longer read — a quote in a C4
value or a class stereotype, a bracket in a C4 name, a quote in a Sankey label. It is the acceptance test for this
section.

**Benefit.** Escaping and delimiter bugs stop being a thing each handler can get wrong and become a property of the
language. `SafeFormatText`'s best behaviour — where the part cannot hold what was asked for, nothing is written —
applies to every edit instead of to pasted words in Mermaid. The twenty-seven handlers shrink to gesture-to-intent
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

## Order

1. **§1, the engine's stopgap** — held. Nothing waited on it, and everything else is safer for it.
2. **§2, reading out of the builders** — the deepest, and everything except §3 waits on it. `MermaidBuilder`'s shared
   raw-zone test first, then one diagram at a time; §4 rides along with Pie.
3. **§3, the parser's write half** — can run beside §2 once the splice interface is designed.
4. **§6's R13 analyzer** — as soon as §2 makes it true for a language, so the next language cannot regress.
5. **§5, a language in one place** — after §2, and after the WPF-free layout tree is measured and designed.
