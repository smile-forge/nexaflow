# The LaTeX parse tree

A tree that owns the text, prints back exactly what was read, and answers what a construct's parts
*are* — so that editing a formula is an operation on the tree and the source is only how the tree is
written down.

## Why the current arrangement cannot go further

Three trees are in play today and only one of them is ours:

| Tree | Whose | What it is for |
|---|---|---|
| `TexFormula` / atoms | WpfMath | typesetting — what boxes to build |
| `LatexTree` / `ILayoutNode` | ours | the picture — where every piece landed, and which characters drew it |
| `Entity` | AngouriMath | algebra — what the maths *means* |

The layout tree is right and stays. The problem is the first one: **WpfMath's atom tree is a
typesetting tree being asked structural questions.** It was built to decide box sizes, so it discards
what it does not need — braces, spacing, comments, the difference between `x^2` and `x^{2}` — and it
cannot be printed back. It has no writer, and giving it one would mean a serializer over ~40 atom
types that would still be guessing at everything the parser threw away.

So every edit today is a **string splice**: read spans off the tree, cut the source, re-parse. That
works, and `LatexTree.Write` / `Move` do it carefully, but it puts a ceiling on the feature that we
have now reached:

- a matrix rewrite reformats the whole body, because the body is regenerated rather than edited
- `IsBraced`, `IsOneToken`, `EndsWithControlWord`, `Separated` all read raw characters around a span
  to re-derive facts the parser knew and dropped
- an `array` cannot be reordered at all, because its column spec is not in the tree
- what renders and what solves are parsed by two different parsers that disagree

Every one of those is the same defect: **the source is the document and the tree is a lossy summary of
it.** Turn that around and they all go.

## What replaces it

A concrete syntax tree, in the Roslyn sense — lossless, immutable, text-owning.

**Nodes own their text; the source is a projection.** A leaf carries characters. A node carries
children. There are no offsets stored anywhere. `Print` concatenates the leaves; an offset is computed
by a walk when the editor asks for one. That is what makes "the source is a serialization format"
literally true rather than aspirational: a tree that has never been printed is still the whole truth,
and printing is a fold.

**Two invariants, and the second is the one that holds the first up:**

1. `Print(Parse(s)) == s` — for every input, including malformed input. Nothing the reader can type is
   outside the tree.
2. **The parser only ever copies.** Every leaf's text appears in the source at the offset the tree puts
   it at; nothing is synthesized, normalized or inserted. Checked at every leaf of every parse.

The first is the headline and is the weaker claim — a parser that returned the whole input as one
verbatim leaf would pass it, and so would one that quietly repaired what it read. The second is what
stops recovery from inventing: the temptation when reading `\frac{a` is to close the brace, and a
parser that does that round-trips everything except the half-finished formulas the editor spends its
whole life holding.

**Round-tripping is a safety property, not a correctness one.** If `\frac` were mistakenly given one
argument instead of two, both invariants would still hold and the tree would simply be wrong. So the
arity table is checked separately, against the one oracle available: WpfMath already knows the answer
for every construct it can typeset, and the corpus has 230,000 formulas to ask it about.

**Anything unreadable is held, not lost.** Input the parser cannot make sense of becomes a verbatim
node rather than an exception. The editor is then never in a state where the reader has typed
something the tree cannot hold — which is what half-finished input always is.

## Where it lives

A new leaf: `src/Nexaflow.Maths/` — `net10.0`, no WPF, no dependencies. Tests in
`src/Nexaflow.Tests/Nexaflow.Tests.Maths/`, the same shape as `Tests.IO` and `Tests.Initiatives`:
plain `net10.0`, no desktop session.

Not in `Nexaflow.Syntax`, which is the tree-sitter engine — the solver would then need tree-sitter
grammars on its dependency path in order to solve an equation. Not in `Visuals.Text`, which is WPF and
which the solver reaches only for rendering. Both `Visuals.Text` and `Features.Solver` already sit
above a shared leaf like this one, and the endgame — one tree that both renders and solves — needs
exactly that position.

## Stages

Each stage stands on its own and leaves the app working.

1. ✅ **Tree, parser, printer.** No consumers at all. The only tests are the two invariants, over the
   construct table the layout tests already keep, and over the corpus.
2. ✅ **Roles and the command table.** Arity and what each argument is called (`numerator`, `degree`,
   `radicand`, …), held against the typesetter's own reading of the same formula: **the parse tree may
   see more, and may never see less.** More, because a fraction inside `\displaystyle` is wrapped in a
   style atom that names no parts at all — style, phantom, lap, cancel and bold atoms all name none, so
   nothing written inside one is in that tree to be found. Less would mean a construct the table has
   not been taught. Zero shortfalls over all 238,329 corpus formulas.
3. ✅ **Grids from the tree.** A matrix's rows and cells read off the environment node, and `GridAt`
   swapped over to it. This was where the exercise paid for itself: the typesetter's span for a command
   begins at the command's *name*, so a cell holding `\alpha` was named as `alpha`, and **every rewrite
   of a matrix took the backslash off every command in it** and handed back LaTeX that no longer
   parsed. `\begin{matrix} \alpha & \beta \\ \gamma & \delta \end{matrix}` came back from a column move
   as `\begin{matrix} beta & alpha \\ delta & gamma \end{matrix}`. It had shipped, and nothing caught
   it, because every grid test written until then had a single letter in each cell.
4. 🔄 **Swap `LatexTree`'s remaining questions over.** `RoleOf`, `IsComposite` and `IsSequence` answered
   from the parse tree; the layout tree keeps geometry, which is all it was ever good for. The
   `IFormulaNode` projection comes off `LatexNode` once nothing asks it anything. This is what makes a
   fraction inside `\displaystyle` selectable, draggable and copyable as a fraction — today it is not,
   because as far as the tree the editor asks is concerned it has no parts.
5. **Edits become tree operations.** `Write` and `Move` build a new tree and print it. The character
   peeking (`IsBraced`, `IsOneToken`, `EndsWithControlWord`, `Separated`) is deleted rather than
   ported, and the matrix body stops being reformatted, because untouched subtrees are reused as they
   stand.
6. **The solver bridge** — later, and out of scope here. Tree to `Entity`, so that what renders is
   what solves.

## The typesetter was ingested

Stage 4 finished with `Attribute` — one method matching each box to the parse-tree part it was drawn from,
by comparing the stretch of source each was named for. Every rule in it exists because the boxes were built
by *somebody else's* reading of the same LaTeX, and the only thing the two readings share is offsets.

Build the boxes from this tree and there is nothing to match: each box carries the part it came from. But
the atoms boxes are built out of are `internal` to the library, so that meant either a public façade
invented to get around the boundary, or a two-repo edit and a pin bump for every construct. Five such
changes in, with upstream dormant — no non-bot commit we lacked — the boundary had stopped protecting
anything.

So XAML-Math is no longer a dependency. Its source is here: `Nexaflow.Maths.Typesetting` (the engine),
`Nexaflow.Visuals.Maths` (the WPF fonts and renderer), and its own 762 tests as
`Nexaflow.Tests.Typesetting`. Verbatim, one edit aside — the font resource asks for its own assembly by
name now rather than the literal `WpfMath` — and the approvals prove it: the only difference in any of the
148 recorded formulas was that URI. Every geometry number identical.

**What we kept is why this was ingested rather than rewritten.** `Atoms/` and `Boxes/` are TeX's box model
— script positioning, fraction shifts, radical construction, delimiter growth, and the spacing that comes
from atom classes — and `Data/DefaultTexFont.xml` is ~198KB of Computer Modern metrics transcribed from
TeX. What we are removing is the *front* (its LaTeX parser); we had already replaced the *back* (its
renderer, with a capture that records where every box landed). What is left is the middle, which is the
part worth having.

## The rule: layout never names a point in the source

**The builder never touches the source, and nothing it builds names a point in it.**

The builder is handed a `TexReading` and never the string. There is no offset for it to take, because
nothing gives it one — every atom's `Source` is left null, and what a thing came from is its `Origin`,
a parse-tree part. *Where* that part is written is the reading's to answer, worked out by a walk when
somebody asks.

This is not tidiness. An offset stored beside a tree is a second copy of a fact the tree already holds,
and the two go out of step the moment anything is edited — which is the whole reason the boxes are being
built from a parse tree at all. Keeping both would rebuild, one construct at a time, the thing being
removed.

It cost nothing to adopt: the spans the builder used to thread through were never load-bearing. Every
formula sets in exactly the same place with all of them null, across the whole corpus. They were being
carried for the *parser's* sake, because the parser has nothing else to say where a box came from.

`TexBuilderTests.NothingItBuildsNamesAPointInTheSource` asserts it over every construct — on what comes
out, not on how it was written — and it caught two things on its first run that reading the code had
not: a `\sum` whose own glyph did not know it was the sum, and a bracketed matrix whose grid knew
nothing because only the fence round it had been told.

**The other half is not done.** `LatexNode` still carries `SourceStart`/`SourceLength`, and
`LatexTree`, `FormulaElement` and `LatexLayoutCapture` still work in them — about thirty sites. They
cannot move yet, because the offsets are what the *parser* path has, and the parser path is still the
one that runs. Marking them obsolete now would tell every site to use `Part` while `Part` is filled in
by `Attribute` — the span matching being deleted. The order is: wire the builder in, so `Part` arrives
from `Origin`; then the marker fires on a real to-do list rather than on code doing the only thing
available to it.

## The sweep is the inner loop, so it runs in forty seconds

Measuring a quarter of a million formulas took nine minutes, which is long enough that you stop doing
it and start guessing instead. Two things were wrong, and neither was the amount of work:

- **`WpfTeXEnvironment.Create` walks every font family installed on the machine**, to find the one
  `\text` would use, and builds the Computer Modern metrics beside it. `Settled` called it per formula.
  It is now made once per thread.
- **Every formula is self-contained** — read it, build it both ways, compare where the pieces landed,
  forget all of it — so the only reason it ran on one thread was that it was written that way.
  `UiThread.Across` runs it on as many STA threads as there are processors.

Nine minutes to forty seconds, and the whole loop with it.

**And then the corpus caught something better than a coverage number.** Running on thirty-two threads,
two identical sweeps reported 46 disagreements and 53, and even the count of formulas *built* moved:
the answer depended on the run. The cause was in the engine, not in the parallelism —
`TexPredefinedFormulaParser` drives shared, stateful parsers and reads the answer back off a field, so
two threads expanding `\quad` at the same moment take each other's. It had been latent since the
ingest and was only reachable once the builder started expanding macros at all.

Every one of those 46-to-62 "disagreements" was that race. With it fixed the sweep reports the same
number three runs running, and none of them disagree. **A flaky gate is worse than a slow one**: it had
begun to look like a real defect in the switch handling, and two rounds went into chasing it.

The 238,329-formula corpus is the oracle for almost everything here, and it is worth knowing where it
is silent, because a coverage number that does not move is easy to read as "nothing changed" when it
means "nothing was tested".

**It contains no apostrophes at all.** Primes are written `^{\prime}` in it — 22,653 formulas — and
never as `'`. So the whole of the mark handling, in the reader and in the builder, is invisible to it:
teaching the builder primes moved the number by exactly zero. Ties, by contrast, are in 21,478 of them
and genuinely covered.

Where the corpus is silent the hand-written list in `TexBuilderTests` is the *only* place the two
readings are held against each other, so for a construct it cannot reach that list has to be more than
a couple of shapes — braced both ways, inside a fraction, a root, a fence and a table.

## It is wired in

`LatexLayout.Build` tries the builder first and falls back to the recovering parser — which it still
needs, for a stretch being shown as written and for the holes an empty argument gets, both of which are
that parser's and neither of which the builder does. Seven formulas in ten now come out of our own
reading, and `Attribute` never runs for them: `Own` hands each piece the part its atom already carried,
which is what all of this was for. `Attribute` stays for the other three in ten and goes when they do.

Three things had to move for it, and each was the rule showing where it was not yet obeyed:

- **`LatexLayoutCapture.SourceOf` asks the atom's part** before falling back to the span. It names the
  span the way the other reading named it — a braced argument's contents, a cell's ink — because
  everything downstream still works in offsets and was written against that convention. Handing over
  the honest span instead re-braced an argument that was already braced. The shim goes when the editor
  asks the part.
- **`LatexTree.GridDropAt` read `Formula.Source`** — an offset, on an atom that deliberately has none —
  so every grid gesture silently stopped working. It asks `Origin` first now.
- **`FencedAtom` never gave its delimiter boxes their atom.** Every other box gets one in
  `Atom.CreateBox`; a delimiter is built by hand from a name and a height, so a bracket knew nothing
  about what it came from. Invisible for as long as an offset was enough, and it cost `\right]` its
  selectability the moment one was not.

**And `TypesettingUnchangedTests` hashed the spans along with the geometry**, so a change of *naming*
read as "the typesetting moved" — the one thing that guard exists to say. It hashes where the pieces
landed and nothing else now; what each piece is named from is checked by every selection and caret test
there is. Only three of its thirteen constructs are built by the builder at all, and for those three
the geometry is identical, which is how the re-baselining was justified rather than assumed.

## What the builder still declines

`TexFormulaBuilder` is all-or-nothing per formula: anything it does not handle comes back null and that
formula goes through the engine's own parser instead, which is the path three formulas in ten take. So a
decline costs coverage and nothing else — **nothing renders differently because of one.**

There are two kinds, and they should not be confused.

### Not written yet

Known work, and the coverage number moves when each lands.

| | |
|---|---|
| `\text`, `\mbox`, `\textbf` … | words rather than maths: every character as written, *spaces included*, and the spaces are exactly what this reading drops on the way to an atom. A different job, not a harder one |
| `\textcolor`, `\colorbox` | colour, which the parser carries on the formula |
| unknown commands | anything the table has never heard of |
| `\hline` | a rule between rows, so it is read off the grid and never off a cell — an `array` holding one falls back whole |
| counted alignments | `\begin{alignat}{2}` and its family, whose count is written where the reading expects a cell |

**Environments landed**, which was the one that mattered most for editing, since grids are what the
table gestures act on: every matrix, `cases`, the aligned and gathered blocks, `smallmatrix`, and
`array` with a preamble of `l`/`c`/`r`/`|`. Rows and cells come off the reading, which knows a table is
a table, and the arrangement — the column gaps, the brackets, the size — is asked of the engine's own
table rather than copied, so a padding cannot come to mean two things.

A cell nobody wrote is the one atom the builder makes that carries no part, and deliberately: squaring
off a short row invents positions so that "the third column" means the same thing in every row, and
there is nothing in the reading for those to be. A cell written *empty* — the one a trailing `&` leaves
— does have a part, because the reader wrote the `&` that closed it.

**Space that was typed is not built; space that was asked for is.** The gaps TeX puts between symbols
come from atom classes, so `a+b` and `a + b` are set identically and the spaces in the reading produce
no atom. `\,`, `\;`, `\:`, `\!`, `\quad` and `\qquad` are the writer overriding that, so they do. They
turned out to be **macros rather than commands** — `\quad` is a formula in `PredefinedTexFormulas.xml`,
so the symbol table never sees it — and they are 20% of the corpus on their own: `\,` alone is in
48,431 formulas. That one gap was worth twenty-one points of coverage, and the builder's own comment
had been claiming it was handled.

**A switch takes the rest of the group it stands in, not an argument.** `{\cal L M}` sets both letters,
and nothing says where the scope ends except the closing brace — so it is read where the rest of the
run is, and it produces no atom of its own: an alphabet switch reaches the letters, a size switch wraps
them. Its *scope* is one thing even so, and splicing that back into the row it stands in sets
`c \bf{1} .` differently from `\bf{1}` alone.

**A style is not an atom.** `\mathrm{abc}` sets three roman letters and wraps them in nothing, because
which alphabet a letter is drawn from is a property of the letter. So the style is carried down the
build and reaches the characters; it is not a case in the switch. That is the shape every future
*context* takes — a size, a colour, a cramped script — and it is a third kind of thing beside the two
trees: not a fact about the reading and not a fact about the drawing, but about the descent between
them.

## Where the tree nests, and where it does not

**The tree nests where the *writing* says it nests.** `\frac{a}{b}` is a construct with a numerator and
a denominator, because the braces are in the source saying where each one stops; reading them is
copying. `a+b` stays three things standing beside each other, because nothing in the writing groups
them — making `+` the root needs precedence, and precedence is knowledge about mathematics rather than
about the text. That is the seam between this tree and the expression tree.

Adjacency is written down too, which is why scripts and marks are on the nesting side: TeX binds `_` to
the atom before it, and `x^2_3` is one x carrying both. So is `x''_{i}` — the primes and the subscript
are all on the x, and a node per attachment would nest them and land the subscript on the prime.

**There is one definition of atomic, and it belongs to the tree.** The layout tree powers selection,
and what it points at has to *be* a selectable thing: a unit scattered across siblings is not one. So
`f''` is one node. Both builders then group upward from these atoms; neither redefines them — the
expression tree wants bigger units (`a+b` is one node there) and gets them by grouping, not by
re-cutting.

**Grouping is not interpreting**, which is what keeps this consistent with the rule that a token never
carries a compounded interpretation. Making `f''` one node says *this is one thing to select*. It does
not say that `'` means a superscript prime rather than a derivative, a transpose or a minute of arc.
The children stay `f`, `'`, `'`; every token is still itself; the round trip is untouched. What the
mark *draws as* is the layout builder's answer, and what it *means* is the expression tree's.

Its role is named `mark` for exactly that reason — where it sits, not what it draws.

**One forward pass is enough, though some constructs cannot be resolved until enough has been read.**
The second half sounds like an argument for a merging pass and is not one: `x^2_3` reads as a
superscript until the `_` arrives, and `Script` handles it by peeking at what follows and re-parenting
the item it was just handed — one pass, no second walk. Marks joined the same loop, and a tie joined
the rule that already refused `Space` and `Comment` as bases: a script attaches to the atom before it,
and a tie is a space written as a character.

The one thing genuinely shaped like a first pass is **raw text**: the contents of `\text{…}` are not
maths and must not be lexed as maths at all. That is a lexer mode switch, not a lookahead, and it is
what still stands between the builder and the `\text` family.

### An adornment is a node, and fusing it is the selection layer's call

`\overline` is a prefix that takes the next thing, braced or not: `\overline f`, `\overline{f}` and
`\overline\alpha` all read as a command with a `base`, differing only in what the base *is*. The
temptation is to make the adornment a *property* of what it adorns instead, so that `\overline{w}` is
one adorned `w` — and for a single letter that is exactly how it behaves to a reader. Deleting the `w`
should take the bar with it; nobody wants to delete twice.

But the adornment stays a node, for two reasons:

- **A property cannot own text.** `\overline` is nine characters, plus whether braces were written,
  plus the spacing in `\overline  f`. Storing all that in a property rebuilds the node with less
  structure and breaks the rule that every leaf's text is in the source where the tree puts it. Here,
  anything that owns text is a node.
- **Fusing at parse time is a decision that cannot be revisited without re-parsing**, and it cannot be
  made uniformly anyway: `\overline{w}` and `\overline w` are the same picture and different writing,
  so a tree-level rule gives one picture two editing behaviours.

The behaviour belongs a layer up. **The selection layer decides granularity**: an adornment whose base
is a single atom is entered as one unit — one click takes the whole thing, one delete removes it — and
one whose base is a written group is enterable, because a group is a region the writer made. Same tree,
one rule, and it can change without anything being re-read. That is stage 5 work, not the builder's.

**But which constructs fuse is declared, not worked out.** It takes two facts, and only one of them is
in the tree:

- *is this an adornment?* — `\overline`, `\vec`, `\hat`, `\tilde` yes; `\frac`, `\sqrt` no. A fact about
  the command, so it belongs in `TexCommands` beside its arity and its role names.
- *is its base a bare atom or a written group?* — a fact about this instance, already in the tree.

Shape alone cannot answer it: `\frac12` has a bare-atom argument too, and there you want to enter it
and change the numerator. And a node kind cannot answer it either — a wrapper marking "this is a unit"
would print as nothing, so every walk would gain an empty rung, and it still would not know which of
`\overline{w}` and `\frac12` it should be wrapping.

**The same declaration is what the solver needs**, which is the argument for the table rather than
anywhere else: `\overline{w}` means one symbol *w̄*, not an operation applied to `w`. Selection asking
"is this one thing to select" and the expression tree asking "is this one symbol" are the same question
about `\overline`, and it should be answered once. Nothing reads it yet, so it is not there yet.

**What the builder still reads as a run** is only what the writing left as a run — a row of things
side by side. It does not compose: `a+b` comes out as three atoms in a row, because deciding otherwise
is the other tree's job.

### Settled, and we differ on purpose

Shapes somebody has looked at — the parse tree, both box trees, both renderings, and the picture the
published paper shipped — and decided ours was the one to keep. The corpus sweep counts these and names
the reason instead of failing on them, because a difference nobody has examined is indistinguishable
from a defect and a difference somebody *has* examined is not one at all. They are matched on the
reading rather than on the text: "a fence whose body holds a fence" is the shape that was ruled on;
"contains two `\left`" is a search that would also catch things nobody looked at.

**The gate asks two questions, not one, and that is what four rulings taught it.** Every ruling but one
came back "identical rendering, ours is the better structure" — so the sweep compares the *ink* (the
boxes that draw something, and where) separately from the *tree*. Same ink and a different tree is a
structural choice: counted, named, not a failure. Different ink is a disagreement about the picture and
fails until somebody looks. That one distinction took 959 unexplained differences down to 40, and it
needs no per-shape predicate to do it — the earlier list of shapes was a suppression file waiting to
happen.

| | Why ours |
|---|---|
| a fence inside a fence | The parser collapses the `^{4}` of `\left( \left( \tfrac12 \right)^{4}, 0^{4} \right)` into one atom; ours keeps the group holding one thing, which is what was written and what a substitution has to reach. *Reviewed 2026-08-27* |
| a script on a construct, inside a fence | The parser follows TeX's rule that what comes after modifies what came before, and flattens the two together. Right for setting type, wrong for selecting: the thing scripted and the script are separate things a reader points at. *Reviewed 2026-08-27* |
| a row written first in a row | The parser splices such a row into the row it is starting, but only where it is written first — put anything before it and it nests, as ours always does. Ours respects the grouping that was written; the parser's depends on where the group happens to sit. *Reviewed 2026-08-27* |
| `\left\|` | **The one ruling that moves ink.** Ours drew no bar at all: `\|` strips to a symbol called `\|` that no table has, so the norm bars were simply missing. It is TeX's own spelling of `\Vert`, and that is what it asks for now. *Reviewed 2026-08-27* |

### Still to be settled

Cases where our reading and the parser's disagree about the picture and **it is not yet established
which is right**. These are not bugs to fix by matching — the parser is a reference, not a
specification, and it is the thing being replaced. Each needs looking at properly: the parse tree, both
layout trees, and both renderings beside the corpus's own reference image from the published paper.

The first of these has been decided and moved above; what follows is what has not.

**Almost everything left here is a fence, which is itself the finding.** Three separate-looking
disagreements turned out to be three shapes of one: what is written between `\left` and `\right` is
boxed before the delimiters are grown to fit it, and the two readings box it differently. Outside a
fence nothing disagrees any more. So when this is picked up, **treat the old parser's `\left`/`\right`
handling as the suspect** rather than ours.

| | What differs |
|---|---|
| a script on a construct, inside a fence | `\left( \frac{f}{g}_{i} \right)`, and the same with an `\overline`. Written anywhere else the two agree exactly. This decline used to sit on `\overline` itself, where it was both too narrow — it never caught `\frac` — and too broad, costing every unfenced `\overline{J}^{a}` its coverage for nothing |
| `\left\|` | the double bar. Stripping the backslash draws a single bar; naming it `Vert` instead does not agree either. Every norm in the corpus is written with it |
| a row written first in a row | `\mathrm{vol}(10)` — the parser splices the style's row into the row it is starting; `A \mathrm{vol}(10)` nests it, exactly as we do. Identical geometry either way, and an artefact of the accumulator (the first atom handed to `TexFormula.Add` *becomes* the row) rather than a rule. **Ours is the consistent one**; it was declined because it differed |
### Prefix scripts

**Settled 2026-08-27, and bigger than the case that raised it.** A script written where nothing before
it can carry one belongs to what comes *after*: the `_{\wedge}` of `\int C ~ _{\wedge} d T` is the
`dT`'s. That looked like an oddity of ties until the general case: **prefix sub- and superscripts are
ordinary notation in chemistry** — `{}^{14}_{6}\mathrm{C}` — and this parser is meant to serve more
than one reader of maths. So it is not a tie special-case; it is a general rule with a tie as one
occasion for it.

Three parts, and the middle one is what the first attempt got wrong:

1. **The parser decides while it builds**, from whether the token in hand can take a script at all. That
   knowledge already exists as `Carries` — a space cannot, a tie cannot, a mark cannot — and this is the
   other half of the same question, so it belongs in the same place rather than in a second pass.
2. **The reading nests it, and the children stay in written order.** `Script[ name '_', subscript,
   base ]`, with the base last because that is where it was typed. The round trip needs no flag saying
   "this one comes first": a node's *role* says what it is and its *position* says where it was
   written, and this is the one construct where those two answers differ. Verified across all 238,329.
3. **The builder must lay a prefix out in front.** This is where the first attempt moved ink in 281
   formulas: the reading said prefix and the builder still made `ScriptsAtom(base, sub, sup)`, which
   sets the scripts *after* the base. TeX writes a prefix as an empty box wearing the scripts followed
   by the real base — `{}^{14}_{6}` then the `C` — so that is two atoms in a row, not one wearing two.

**The reading half is done and on.** `{}^{14}_{6}\mathrm{C}` parses as one thing with its scripts in
front, round-tripping across all 238,329, which is what chemistry needs and what an editor has to hold.

**The drawing half is written and declined.** Building the prefix in front took the disagreement from
281 formulas to 17, which is the measure of how much of it was the layout rather than the parse. The
seventeen are all a prefix with a tie or another space beside it, and they are unexamined — so they are
not built. `Scripts` in the builder is the piece that puts them in front when they are.

**Most of these are not about the picture.** In the `\overline` family every geometry number was
identical — the pieces land in exactly the same places and only the box holding them differs — and the
styled row is the same. Which has a consequence for the test: `Settled` compares every box, so a purely
structural difference fails it even when the rendering is identical. That is the right gate for
*selection*, where the tree shape **is** the answer, and too strict for *"will switching the builder in
change what the reader sees"*, where only the ink matters. Worth separating the two questions before
settling any of these.

**A decline is never exercised, so it goes stale without anything saying so.** *A script on `\overline`*
sat here recorded as one formula in 238,329 that set the script at a different height. It was at least
ten, the height was not what differed, and it only ever happened inside a fence — so the decline was
costing every unfenced `\overline{J}^{a}` its coverage to no purpose. Re-check what is parked here
whenever something it touches changes; lifting a decline to look costs one corpus run.

**Two entries have gone, and the same move settled both.** *What may carry a script* (`x'_{i}` binding
its subscript to the prime rather than to the x) and *what a construct read out of a run points back at*
(`f''` being three siblings that no node covered, so `Origin` had nothing true to name) were one problem
seen twice. Making the run one node — `Script` with `mark` children — fixed the binding and gave
`Origin` something to name. Whether `Origin` should be able to name a *run* simply stopped being asked.

## Considered and not taken

**A tree-sitter LaTeX grammar.** It would fit the existing grammar machinery and round-trips by
construction. But its maths-mode granularity is the weak part of that grammar — it gives generic
groups where this needs `numerator` and `cell(2,3)` — so the role layer would have to be written
anyway, on top of a tree whose shape we do not control, plus a native grammar build. The parser is the
small half of this work; the roles are the point of it.

**Teaching WpfMath's atoms to print themselves.** Rejected because the information is not there to
print. The parser discards braces and spacing before an atom is ever built, so a writer would be
inventing formatting, and every formula would come back reformatted — which is precisely the defect
being fixed.
