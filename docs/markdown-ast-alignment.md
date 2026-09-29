# Aligning the content pipeline to its spec

[markdown-ast.md](markdown-ast.md) states the architecture as fifteen rules. This plan records where the code stands
against them and what aligning each part buys. It is the high-level shape only: each section below is picked up on its
own, planned in detail then, and finished before the next one starts.

## What this plan takes to be true

Every section below is an argument from the state of the code, so it is only as good as its reading of that state.
Each claim here was checked against the tree or the history at the time it was written; each is also the kind of
claim that has already been got wrong once, which is why they are listed rather than left implicit. A section built
on one of these should be re-checked before it is started, not after it has been half done.

**About the code.**

- **A language's splice needs nothing from the display.** What the source becomes, given a part, a caret and what was
  written, is a function of `ContentPart`, `int` and `string`. If a splice turns out to need a `Piece`, a measured
  width or anything else the layout knows, section 3's boundary is in the wrong place and its plan does not hold.
- **The tree is what an edit is expressed against.** Both implementations that work — Pie now, LaTeX in `v1.6.0` —
  change the tree, print it, and read the source back, rather than computing characters. Sections 3 and 4 assume this
  is the mechanism, not one option among several.
- **What a construct stands for is the parser's to decode, not a stage's and certainly not a builder's.** A macro is
  read as written with its expansion hung beneath it, so the tree carries both what was typed and what it means, and
  a stage that re-derived it would be doing the parse twice. `TexParser` does this for LaTeX and `TexMacros` is named
  by nothing else; a stage's job is the *meaning* laid over the shape (`ResolveCommands` assigning roles,
  arrangements, symbols and colours), and a builder's is placement. Where a plan below reads as if a stage should
  expand something, the plan is wrong.
- **Anything the parser devolved holds no source.** A derived part has `Length` zero — it prints as what it means and
  stands for none of what was written. So it is never the target of an edit: an edit landing in one belongs to its
  nearest written ancestor, and re-parsing re-derives the rest.
- **`Nexaflow.Markdown` stays WPF-free, and `Nexaflow.Visuals.Text` keeps referencing it and not the other way
  round.** Every "move it beside the parser" step depends on that direction. Nothing needs to police it: the project
  targets plain `net10.0` rather than `net10.0-windows`, so a WPF type there does not compile, and the reference
  direction cannot reverse without a cycle.
- **`Print()` round-trips.** `AstPipeline` checks Print-preservation between stages, so a tree that has been edited
  and printed is a faithful source for re-reading. Where a language's printer loses something, every plan that
  prints-then-reads loses it too.

**About what the tests measure.**

- **`DiagramEscapingTests` is the acceptance test for section 3, and its count moves.** It named sixty-two failing
  keystrokes when this plan was first written and names forty now, without anyone setting out to fix escaping. Treat
  the number as a reading taken on a date, never as a baseline carried forward: run it before quoting it.
- **A green suite here does not mean a working feature.** The corner buttons, the block picture and the button
  outline were each broken in the shipped app while every test passed, because the tests asserted engine state rather
  than what a reader could reach. A section is finished when the thing works in the running app, and the test that
  proves it is one that fails when the fix is reverted.

**About the history.**

- **Deleted is not the same as lost.** `aed21683` moved per-diagram escaping out of the grammars and into the view; it
  did not destroy it. `426e9b79` did drop `TexEdit`, and `v1.6.0` still has it. Read the `--numstat` before
  concluding either way: this document has previously said the opposite of both.
- **Only Pie's editing has ever been complete, and LaTeX's worked in `v1.6.0`.** Everything else has escaping and
  little more. Any estimate that reasons from "twenty-six handlers exist" to "editing broadly works" is wrong, and
  the file sizes say so — one handler is 524 lines and most of the rest are 13 to 65.

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

Planned in detail, with its recovery sources and stop conditions, in
[markdown-ast-section-3.md](markdown-ast-section-3.md).

**Goal.** Per language, the knowledge of *how source is spelled* sits beside the parser that reads it, and nothing in
the view holds any of it. An editing handler in the view names a gesture and an intent — type this here, flatten this
note, set this value, insert a row — and the language spells it. R9 becomes true.

### The boundary, named first

Not all of an edit can move, and knowing where the line falls is what keeps this section finishable. `ContentEdit`
carries `Piece`, `Landing` and `Laid` — layout-tree types that need WPF and live in `Nexaflow.Visuals.Text`, which
references `Nexaflow.Markdown` and so cannot be referenced back. So an edit splits in two:

| Half | Asks | Lives |
|---|---|---|
| **Gesture** | which part the pointer or caret is in, what Enter means here, what a drop lands on | the view — it needs a `Piece` |
| **Splice** | given a part, a caret and what was written, what the source becomes | beside the parser — it needs only `ContentPart`, `int`, `string` |

Only the splice half moves. A plan that says "one component per language that both parses and splices" without naming
this is a plan that discovers the layout-tree dependency half-way through, which is where the previous attempts stopped.

### Where it stands

Much further along than "the write half does not exist", and mostly on the wrong side of that line.

- `MermaidWriting` and `MermaidWriting.Escape`, the type a splice returns and the shared escape, are already in
  `Nexaflow.Markdown/Mermaid/IMermaidGrammar.cs`. `MermaidParser.SafeFormatText` is already there too, reached from
  the view through `ContentLanguage.SafeFormatText`.
- Twenty-five of the twenty-six diagram handlers carry a real
  `Escaping(ContentPart part, int caret, string text) → MermaidWriting?` with its own per-role rules — quoted labels,
  bare ids, digits-only indices. Every one of them is WPF-free in its signature and sits in
  `Nexaflow.Visuals.Text/Markdown/Mermaid/<Type>/<Type>Edits.cs`. `IshikawaEdits` passes `escaping: null`.
- `EditState` is WPF-free data — source, caret, selection, raw zone — with a complete splice API on it (`Type`,
  `Write`, `Insert`, `Wrap`, `Backspace`, `Delete`). It lives under `Editing/` for reasons of history, not of need.
- Two languages splice correctly today, and they agree on the mechanism: **edit the tree, print it, read the source
  back**. Pie writes to the AST nodes it was handed and never re-reads the source. LaTeX did the same through
  `TexEdit`, whose own words are the clearest statement of why: *an edit expressed against the tree knows what it
  touched; an edit expressed against the characters knows only that they changed, and cannot afterwards say which
  unterminated brace is the new one.*

What is missing is not the idea and largely not the code. It is that the code sits in the view, that only one diagram
has anything beyond escaping, and that LaTeX's share of it was dropped.

### What was dropped, and where to get it back

`426e9b79` ("Move the LaTeX reader into Nexaflow.Markdown") brought the reader across and left the writer behind.
`TexEdit` — 303 lines in the parser's own project, with nine tests in `Tests.Maths` — is in `v1.6.0` and in no commit
since. It offered `Replace` / `Remove` / `Insert` over the tree, `Write(reading, caret, text) → TexWrite` as the
splice, and `Columns` / `Rows` for matrix structure, returning
`TexWrite(TexNode Tree, int Start, int Length, bool Reshaped)`. `LatexEdits` today answers `Typing` and `Settling`
and nothing else, which is the shape of that loss.

`aed21683` ("Every diagram answers its own edits") is often read as the same kind of loss and is not. It deleted a
shared `MermaidEdits` and about a thousand lines from the grammars, and added the twenty-six per-diagram handlers —
the per-role escaping moved rather than went. `FlowchartEdits` holds the `Escaping`, `Bared`, `Quoted` and `Following`
that `FlowchartGrammar` used to. Its direction was out of the parser's project and into the view's; this section
reverses that direction while keeping its per-diagram shape.

### The acceptance test

`DiagramEscapingTests.WhateverIsTypedWhereSomethingIsWrittenTheDiagramStillReads` types every key into every written
part of every sample diagram. It is red, and it names **forty** cases that leave a line the diagram can no longer
read. They are four rules in three diagrams, not a spread across twenty-four:

| Cases | Role |
|---|---|
| 23 | `mermaid-quoted` / `c4-value` |
| 12 | `mermaid-name` / `sequence-id` — C4 reuses Sequence's roles |
| 4 | `mermaid-label` / `class-kind` |
| 1 | `mermaid-name` / `sankey-target` |

### Shape

Ordered so that each step is provable on its own and the contract is *extracted from* working code rather than
designed ahead of it. Steps 1 to 4 are the design work; 5 is mechanical; 6 is separable and can go at any point.

1. **LaTeX splices again, beside its parser.** Recover `TexEdit` and `TexWrite` from `v1.6.0` into
   `Nexaflow.Markdown/Latex/`, against the current `TexNode` / `TexPart` / `TexReading`, with the nine tests. Point
   `LatexEdits` at it and give back the gestures it answered in `v1.6.0`. This is recovery of shipped, tested work on
   the one language where the regression has a witness.
2. **Pie says the same thing in the same place.** Pie's splice is correct and lives in the view. Move the
   source-and-AST part of `PieEdits` into `Nexaflow.Markdown/Mermaid/Pie/`, leaving `PieEdits` a gesture-to-intent
   map. Its existing tests are the guard; they must not change.
3. **The contract, extracted.** What steps 1 and 2 turn out to have in common is the per-language splice contract —
   two languages that disagree structurally being the only honest basis for naming it. The expectation, to be
   tested against rather than assumed: `Write(reading, caret, text)` returning a tree, a caret and whether the shape
   changed, plus `Escaping(part, caret, text)`.
4. **One diagram that does not edit yet.** Steps 1 and 2 prove the contract can *describe* code that already works,
   which is not the same as proving it can *carry* work that does not exist. Flowchart stresses names and links,
   Sequence stresses stretches; either is a fair test and both are better. Nothing after this step is worth starting
   until one of them edits.

   It has a prerequisite, pulled forward from section 2: **the identity of an unwritten thing must stop being its
   source offset.** `ErBuilder`, `RequirementBuilder` and `SequenceBuilder` each name one as `Unwritten + name.Start`
   (`ErBuilder.cs:327`, `RequirementBuilder.cs:320`, `SequenceBuilder.Reading.cs:413` — the same line three times).
   An edit that targets an unwritten thing therefore breaks whenever text above it changes, which is most of the time
   while somebody is typing. Three sites, and Sequence is one of the two diagrams this step is for.
5. **The twenty-five `Escaping` functions come back across.** Each moves from `<Type>Edits` in
   `Nexaflow.Visuals.Text` to beside its grammar in `Nexaflow.Markdown`. Behaviour is unchanged and the existing
   per-diagram tests say so. A ratchet over the handlers that still declare syntax — one that can only shrink, as
   `automation-ids-without-a-journey.txt` does — keeps the boundary from leaking back. Given the history here, a
   build-enforced floor under this area is worth more than it usually would be.
6. **The four escaping rules.** `c4-value`, `sequence-id`, `class-kind`, `sankey-target`, until
   `DiagramEscapingTests` is green. A bug fix inside whatever structure is current, so it can go before, during or
   after the rest — but **not bundled with** steps 1–5, because a commit that both moves code and changes behaviour
   cannot be checked by either test.

### Where this section ends

When the splice is per-language and beside its parser, the escaping test is green, no handler in the view spells
syntax, and three languages have proved the contract — two that already worked and **one diagram that did not**. That
last one is the load-bearing part of the test: a contract shown only against working code has been fitted to it.

It does **not** end with every diagram answering every gesture. What Enter means in a Gantt chart, what a drop means
in a Sankey, is a per-diagram feature with a per-diagram spec, and folding twenty-four of those into an architectural
change is how the previous attempts became unfalsifiable. They are tracked as their own work, on top of this, and
each one is cheap or the contract is wrong.

**Benefit.** Escaping and delimiter bugs stop being a thing each handler can get wrong and become a property of the
language. `SafeFormatText`'s best behaviour — where the part cannot hold what was asked for, nothing is written —
applies to every edit instead of to pasted words in Mermaid. The handlers shrink to gesture-to-intent maps, testable
without a parser. And a new gesture, or a new ribbon option, stops being a new opportunity to corrupt a document.

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
