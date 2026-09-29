# Section 3 in detail — the parser's write half

> **Freshness:** written 2026-09-29, against `origin/main` at the merge of #340. Every fact below was read from
> the tree or from git at that point, and each says how to re-check it. This is the detailed plan for section 3 of
> [markdown-ast-alignment.md](markdown-ast-alignment.md); that document holds the shape, this one holds the work.

The goal restated: **per language, the knowledge of how source is spelled sits beside the parser that reads it, and
nothing in the view holds any of it.** A handler in the view names a gesture and an intent; the language spells it.

## The boundary

Not all of an edit can move, and the line is what makes this finishable.

`ContentEdit` (`src/Nexaflow.Visuals.Text/Editing/IOnEdit.cs`) carries `Piece`, `Landing` and `Laid` — layout-tree
types that need WPF. `Nexaflow.Visuals.Text` references `Nexaflow.Markdown`, and `Nexaflow.Markdown` targets plain
`net10.0`, so the reference cannot reverse and a WPF type there does not compile. Therefore:

| Half | Asks | Lives |
|---|---|---|
| **Gesture** | which part the caret is in, what Enter means here, what a drop landed on | the view — it needs a `Piece` |
| **Splice** | given a part, a caret and what was written, what the source becomes | beside the parser — it needs only `ContentPart`, `int`, `string` |

Only the splice moves. A plan that promises "one component that both parses and splices" without saying this is one
that meets the layout-tree dependency half-way through.

## What this rests on

Each of these is load-bearing, and each is the kind of claim that has been got wrong here before.

1. **The LaTeX reader is on the shared AST.** `TexReadingTests` reads
   `ContentReading.Of(TexParser.Parse(latex))` — `ContentPart`, not a LaTeX-private part type. `TexPart` and
   `TexReading` do not exist. *Re-check:* `nfi ask 'search TexPart'` finds only prose.
2. **The parser devolves macros; the stages assign meaning; the builder places.** `TexParser.Parse` hangs a macro's
   expansion under it as a `Roles.Derived` child, recursively. `TexMacros` is named by `TexParser.cs` and by nothing
   else — no stage, no builder. `ResolveCommands` (a stage) assigns roles, arrangements, symbols and colours, which
   is meaning and belongs there. *Re-check:* `TexPipelineTests.AMacroIsReadAsWritten_WithWhatItStandsForHungBeneathIt`
   and `AnExpansionThatNamesAMacroIsExpandedInTurn`; `nfi ask 'grep "TexMacros" | files'`.
3. **A derived part holds no source.** `TexReadingTests.AndWhatAMacroStandsForKnowsItIsNowhere` asserts
   `part.Length == 0` for every derived part. This is why surgery never targets one — see *The rule about derived
   parts* below.
4. **`ContentNode` is immutable and already has what surgery needs.** `With(IReadOnlyList<ContentNode>)`, `Print()`,
   `Same(other)`. `ContentPart` has `Parent`, `Children`, `Order`, `Ancestors()`. *Re-check:*
   `nfi ask 'search ContentNode | members'`.
5. **`Print()` round-trips.** `AstPipeline` checks Print-preservation between stages, so a tree that has been edited
   and printed is a faithful source to read back. Where a language's printer loses something, every step here loses
   it too.
6. **Two languages splice correctly, and they agree on the mechanism** — edit the tree, print it, read the source
   back. Pie writes to the AST nodes it was handed and never re-reads the source. `TexEdit` said why: *an edit
   expressed against the tree knows what it touched; an edit expressed against the characters knows only that they
   changed, and cannot afterwards say which unterminated brace is the new one.*

## Where the recoverable code is

`TexEdit` and `TexWrite` are in `v1.6.0` and in no commit since. `426e9b79` ("Move the LaTeX reader into
Nexaflow.Markdown") brought the reader across and left the writer behind.

```
git show v1.6.0:src/Nexaflow.Maths/Latex/TexEdit.cs                          303 lines
git show v1.6.0:src/Nexaflow.Maths/Latex/TexWrite.cs                         the record
git show v1.6.0:src/Nexaflow.Tests/Nexaflow.Tests.Maths/Latex/TexEditTests.cs  9 tests
git show v1.6.0:src/Nexaflow.Visuals.Text/Markdown/Latex/LatexTree.cs        782 lines; the last ~250 are the editing
```

`LatexTree` called `TexEdit.Write` for typing, drops and paste, and `TexEdit.Columns` / `Rows` for matrix structure
(six call sites, `LatexTree.cs:536–788`). Most of its other 530 lines were layout, which `LatexBuilder` now does on
the shared layout tree.

`aed21683` ("Every diagram answers its own edits") is a different kind of change and is often misread as the same
loss. It deleted a shared `MermaidEdits` and about a thousand lines from the grammars, and added twenty-six
per-diagram handlers: the per-role escaping **moved** rather than went. `FlowchartEdits` holds the `Escaping`,
`Bared`, `Quoted` and `Following` that `FlowchartGrammar` used to. Its direction was out of the parser's project and
into the view's, and this section reverses that direction while keeping its per-diagram shape. Read the `--numstat`
before concluding otherwise.

## The rule about derived parts

Because devolution is the parser's and a derived part holds no source, surgery has one rule and needs no special case:

> **A derived subtree is never a surgery target.** It has no characters to rewrite. An edit that lands in one is an
> edit to its nearest non-derived ancestor — which is the macro that produced it, and which does have source.
> Re-parsing re-derives the expansion.

Assert it rather than assume it: the pre-flight below is that assertion.

## Pre-flight

Cheap, and either one failing invalidates what follows.

- **P1 — surgery is faithful.** For every construct in `LatexConstructs.Everything`, take each **non-derived** part,
  `Replace` it with itself, print, and assert the source is byte-identical. Then assert that no derived part is
  reachable as a target. If the first fails, step 1's algorithm is wrong before it is written.
- **P2 — do not re-add covered tests.** Three of the nine recovered tests
  (`ATreeIsTheSameAsItselfAndAsAReadingOfItsOwnSource`, `PrintingAlikeIsNotBeingTheSameTree`,
  `ADifferenceAnywhereIsADifference`) exercise tree identity, and `ContentNode.Same` already exists. Check
  `Tests.Markdown` for existing coverage first.

## Step 1 — `AstEdit`: the surgery, and it is not LaTeX's

**Where.** `src/Nexaflow.Markdown/Ast/AstEdit.cs`. The name is free.

**Why here.** Once ported onto `ContentPart` / `ContentNode`, `Replace` / `Remove` / `Insert` contain nothing about
LaTeX. They are spine-rebuilding over any reading, so every language gets them.

**What.** Port `Replace`, `Remove`, `Insert` and the private `Swap` / `Index` from
`v1.6.0:src/Nexaflow.Maths/Latex/TexEdit.cs`. Roughly 90 lines. The mapping is determined:

| v1.6.0 | now |
|---|---|
| `TexPart` | `ContentPart` |
| `TexNode` | `ContentNode` |
| `TexNode.With(children)` | `ContentNode.With(children)` — exists |
| `TexKind.Sequence` | `Kinds.Sequence` (shared, `Ast/Roles.cs`) |

**Tests.** `Tests.Markdown` — the subject is `Nexaflow.Markdown`. Four recovered:
`RemovingAPartTakesItsCharactersAndLeavesTheRest`, `WhatTheEditDidNotTouchIsTheObjectItWas`,
`APieceThatStandsForCharactersHoldsNoParts`, `PuttingSomethingInAndTakingItBackOutLeavesTheTreeAsItWas`.

**Proof it bites.** Make `Swap` rebuild the whole tree instead of only the spine —
`WhatTheEditDidNotTouchIsTheObjectItWas` goes red. Structural sharing is the claim, so that is the test that matters.

**Stop if.** P1 fails, or `Insert` cannot name a position from what the reading gives.

## Step 2 — LaTeX's splice

**Where.** `src/Nexaflow.Markdown/Latex/TexEdit.cs` and `TexWrite.cs`. Both names are free at HEAD.

**What.** `Write(reading, caret, text) → TexWrite` with `Argument`, `IsArgument`, `Braced`, `Point`, `Apart`,
`EndsWithControlWord`; `Columns` / `Rows` with `Ordered`, `Split`, `Spanning`; and
`TexWrite(ContentNode Tree, int Start, int Length, bool Reshaped)`. Roughly 210 lines.

This is where LaTeX's real syntax knowledge sits: which caret positions are argument slots that need braces, and when
a space must separate a control word from what follows it. That is "how source is spelled".

**The mapping is closed.** Every kind and role `Write` switched on still exists, with unchanged strings:

```
TexKind.Group / Row / Cell              →  TexKinds.Group / Row / Cell
TexKind.Sequence / Token                →  Kinds.Sequence / Kinds.Token          (shared)
TexRole.Open / Close / Element / Separator → Roles.Open / Close / Element / Separator  (shared)
TexRole.Superscript, Subscript, Numerator, Denominator, Degree, Radicand, Over, Under  (unchanged in TexRole)
```

**Tests.** `Tests.Maths`. Two recovered — `WhatIsWrittenLandsWhereItWasMeantTo`, `AndLandsInRealFormulasToo` — plus
the matrix reorder cases.

**Stop if.** `Write` needs a fact `ContentPart` does not carry. The mapping above says it does not, but writing it is
the real test.

## Step 3 — `LatexEdits` uses it. This is the step with an unknown in it

**Where.** `src/Nexaflow.Visuals.Text/Markdown/Latex/LatexEdits.cs`, 58 lines today.

**Keep what is there.** It answers `Typing` (the control-word raw zone) and `Settling`, and both are correct. The raw
zone is a view concern: it is about what is *shown as written*, not about what the source becomes.

**Add.** `Pasting`, `Dropping`, `Breaking`, `Erasing` / `Deleting` and the matrix gestures, each shaped as
*intent → `AstEdit` / `TexEdit` → `Print()` → `ContentChange`*. `LatexTree.cs:536–788` in `v1.6.0` is the reference
for which gesture used which call.

**Acceptance is the running app**, not the suite: a formula typed into, pasted into, a matrix column dragged. In this
area a green suite has repeatedly not meant a working feature — the corner buttons, the block picture and the button
outline were each broken in the shipped app with every test passing, because the tests asserted engine state rather
than what a reader could reach.

**This step cannot be sized honestly in advance.** Steps 1, 2 and 4 are ports with determined mappings. This one
depends on whether the shared `ContentEdit` / `ContentChange` path carries what `LatexTree` used to take directly
from the layout.

**Stop if.** A gesture needs a `Piece` fact `ContentEdit` does not carry. That means the boundary above is drawn
wrong — and finding it here costs three files rather than twenty-six.

## Step 4 — Pie says the same thing in the same place

Move the source-and-AST half of `PieEdits` (524 lines,
`src/Nexaflow.Visuals.Text/Markdown/Mermaid/Pie/PieEdits.cs`) into `src/Nexaflow.Markdown/Mermaid/Pie/`, leaving
`PieEdits` a gesture-to-intent map. Pie's existing tests are the guard: they must not change, and must stay green
throughout.

## Step 5 — the contract, extracted

What steps 2 and 4 turn out to have in common is the per-language splice contract. Two languages that disagree
structurally are the only honest basis for naming it, which is why it is named here and not at step 1.

The expectation, to be tested against rather than assumed: `Write(reading, caret, text)` returning a tree, a caret and
whether the shape changed, plus `Escaping(part, caret, text) → MermaidWriting?`. `MermaidWriting`,
`MermaidWriting.Escape` and `MermaidParser.SafeFormatText` are already in `Nexaflow.Markdown`, so the return side of
the contract exists.

**Done when** both languages compile against it without either being bent to fit.

## Step 6 — one diagram that does not edit yet

Steps 1 to 5 prove the contract can *describe* code that already works. They do not prove it can *carry* work that
does not exist. **Flowchart** stresses names and links; **Sequence** stresses stretches. Either is a fair test, both
are better, and nothing after this step is worth starting until one of them edits in the app.

**Prerequisite, pulled forward from section 2: the identity of an unwritten thing must stop being its source
offset.** The same line appears three times:

```
src/Nexaflow.Visuals.Text/Markdown/Mermaid/Er/ErBuilder.cs:327
src/Nexaflow.Visuals.Text/Markdown/Mermaid/Requirement/RequirementBuilder.cs:320
src/Nexaflow.Visuals.Text/Markdown/Mermaid/Sequence/SequenceBuilder.Reading.cs:413

    var id = words is { Length: > 0 } said ? said.Text : Unwritten + name.Start;
```

An edit that targets an unwritten thing therefore breaks whenever text above it changes, which is most of the time
while somebody is typing. Sequence is one of the two diagrams this step is for, so this is on the path rather than
beside it.

## Step 7 — the twenty-five `Escaping` functions come back across

Each moves from `<Type>Edits` in `src/Nexaflow.Visuals.Text/Markdown/Mermaid/<Type>/` to beside its grammar in
`src/Nexaflow.Markdown/Mermaid/<Type>/`. Behaviour is unchanged and the existing per-diagram tests say so.
`IshikawaEdits` passes `escaping: null` and is the twenty-sixth.

Add a ratchet over the handlers that still declare syntax — one that can only shrink, as
`automation-ids-without-a-journey.txt` does. Given the history in this area, a build-enforced floor is worth more
than it usually would be.

## Step 8 — the four escaping rules

`DiagramEscapingTests.WhateverIsTypedWhereSomethingIsWrittenTheDiagramStillReads` types every key into every written
part of every sample diagram. It is red, and the failures are four rules in three diagrams:

| Cases | Role |
|---|---|
| 23 | `mermaid-quoted` / `c4-value` |
| 12 | `mermaid-name` / `sequence-id` — C4 reuses Sequence's roles |
| 4 | `mermaid-label` / `class-kind` |
| 1 | `mermaid-name` / `sankey-target` |

**Run it before quoting the number.** It named sixty-two when the alignment plan was written and forty at the date on
this document, with nobody setting out to fix escaping in between. The count is a reading taken on a day.

This is a bug fix inside whatever structure is current, so it can go before, during or after the rest — but **not
bundled with steps 1–7**, because a commit that both moves code and changes behaviour cannot be checked by either
test.

## Where this section ends

When the splice is per-language and beside its parser, the escaping test is green, no handler in the view spells
syntax, and three languages have proved the contract — two that already worked and **one diagram that did not**. That
last is the load-bearing part: a contract shown only against working code has been fitted to it.

It does **not** end with every diagram answering every gesture. What Enter means in a Gantt chart, what a drop means
in a Sankey, is a per-diagram feature with a per-diagram spec. Folding twenty-four of those into an architectural
change is how earlier attempts became unfalsifiable. They are their own work, on top of this, and each should be
cheap — or the contract is wrong.

## Why this is shaped the way it is

An argument to check rather than a promise:

- **Steps 1 and 2 are ports, not designs.** Every type, kind and role they touch has been verified present. There is
  no blank page before step 5.
- **The contract is extracted after two working languages**, so it cannot quietly be Mermaid-shaped.
- **Step 3's acceptance is a person using it.** That is the standing lesson here.
- **Step 6 is the falsification point and it is deliberately early.** If the contract cannot carry a diagram that
  does not edit, that is where it shows — and steps 1 to 5 are still worth keeping, because LaTeX editing works again
  either way.
- **Every step reverts alone.** Nothing changes a shared contract before step 5.
