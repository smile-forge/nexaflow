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

1. **Tree, parser, printer.** No consumers at all. The only tests are the two invariants, over the
   construct table the layout tests already keep, and over the corpus.
2. **Roles and the command table.** Arity and what each argument is called (`numerator`, `degree`,
   `radicand`, …). Cross-checked against WpfMath's slots over the corpus — where both have an opinion
   they must agree on the span.
3. **Grids from the tree.** A matrix's rows and cells read off the environment node. `LatexGrid` is
   reshaped to build from nodes rather than from `(row, column, start, length)` tuples.
4. **Swap `LatexTree`'s questions over.** `RoleOf`, `IsComposite`, `IsSequence` and `GridAt` answered
   from the parse tree; the layout tree keeps geometry, which is all it was ever good for. The
   `IFormulaNode` projection comes off `LatexNode` once nothing asks it anything.
5. **Edits become tree operations.** `Write` and `Move` build a new tree and print it. The character
   peeking (`IsBraced`, `IsOneToken`, `EndsWithControlWord`, `Separated`) is deleted rather than
   ported, and the matrix body stops being reformatted, because untouched subtrees are reused as they
   stand.
6. **The solver bridge** — later, and out of scope here. Tree to `Entity`, so that what renders is
   what solves.

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
