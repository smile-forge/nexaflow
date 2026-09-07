# The rendering pipeline

One syntax tree that owns its text, a pipeline of actors that re-read it, a builder that decides
geometry, and a layout tree that answers what a click meant. Four stages, and only the first and third
of them know what language they are looking at.

Everything markdown renders is the same four stages. Maths got there first
([docs/latex-parse-tree.md](latex-parse-tree.md)); music is the second; barcodes, diagrams and
markdown's own text blocks follow.

```
source
  │
PARSER      language-specific    → ContentNode      lossless. Print(Parse(s)) == s, and the
  │                                                 parser only ever copies.
PIPELINE    AstPipeline          → ContentNode      an ordered list of IAstStage actors, each
  │                                                 taking a tree and giving one back.
BUILDER     language-specific    → layout           reads the tree, decides where everything goes,
  │                                                 and says which part each piece was drawn from.
LAYOUT      ILayoutNode + marks                     painting, hit-testing, the caret, selection.
```

## Where it lives

`src/Nexaflow.Markdown/` — `net10.0`, no WPF, no dependencies.

```
Ast/         ContentNode, ContentPart, ContentReading, ISourcePart, Roles, Kinds, AstWrite
Pipeline/    IAstStage, AstPipeline, AstRewrite, Stages/ShowAsWritten, Stages/WithHoles
Music/Abc/   AbcParser, AbcTheory, AbcPipeline, AbcKinds, Stages/…
```

The layout tree is still in `src/Nexaflow.Visuals.Text/Editing/` because `ILayoutNode.Bounds` is a
`System.Windows.Rect` and a `LayoutMark` paints onto a `DrawingContext`. It moves here once it has
geometry primitives of its own.

## The tree owns its text

There are no offsets stored anywhere. A node knows how wide it is; an offset is worked out by a walk when
somebody asks. That is what makes the source a serialization format rather than the document — a tree
that has never been printed is still the whole truth, and an edit that replaces a subtree cannot leave a
stale position behind, because there were none to go stale.

**Two invariants, and the second is the one that holds the first up:**

1. `Print(Parse(s)) == s` — for every input, including malformed input. Nothing a reader can type is
   outside the tree.
2. **The parser only ever copies.** Every leaf's text appears in the source at the offset the tree puts
   it at; nothing is synthesized, normalized or inserted. Checked at every leaf of every parse.

The first is the headline and is the weaker claim: a parser that returned the whole input as one verbatim
leaf would pass it, and so would one that quietly repaired what it read. The second is what stops
recovery from inventing. The temptation on meeting `[CEG` is to close the bracket, and a parser that does
that round-trips everything except the half-finished input an editor spends its whole life holding.

**Anything unreadable is held, not lost.** Input the parser cannot make sense of becomes a verbatim node
carrying the reason, rather than an exception.

**`Kind` and `Role` are open strings.** A shared tree cannot hold an enum of every kind of thing every
language has, and it does not need to: nothing in this assembly switches on either. `Roles` and `Kinds`
carry what is genuinely shared — the punctuation every reader has, and the one role that stands for
nothing written.

## The pipeline is the architecture

Not the stages in it. Which actors a language runs, and in what order, is that language's business and
changes as it learns to read more; what is fixed is that they are actors, that they compose, and that
each one leaves the source alone.

```csharp
public interface IAstStage { string Name { get; } ContentNode Run(ContentNode tree); }
```

**The one rule: `stage.Run(t).Print() == t.Print()`.** A stage may nest differently, replace a piece with
another piece, or hang something underneath — as long as the characters coming out are the ones that went
in. `AstPipeline` checks it between stages in a debug build and names the stage that broke it, which is
the difference between a bug found where it was written and a round-trip test failing somewhere
downstream a week later.

Anything a stage wants to say that the characters do not say is a **derived** part (`Roles.Derived`):
zero width, printing as nothing, hung underneath the piece it is about. That is how a stage records what
a macro means, which accidental a note actually prints, or which syllable sits under it, without the
round trip having to be re-argued each time. `AstRewrite.Regrouping` holds a node's own derived facts
back from any regrouping of its children — sweeping an answer into a group made out of the node's
contents would move it somewhere it is not true.

**Nothing is incremental.** An edit can put anything anywhere — a closing brace that reshapes everything
after it, a bar line that re-bars a whole tune — so "is this edit contained in that piece" is not worth
answering cheaply. The tree prints itself back and the whole pipeline runs again: one path, always taken,
therefore always right.

A parser is not a stage. What a name is shorthand for, and where one token stops and the next begins, are
facts about the text that no later stage can change.

## ABC

Nine actors, and the order is the only thing about it that is an argument:

| Stage | What it works out |
|---|---|
| `ResolveContext` | the key, meter, unit note length and voice in force on each line |
| `GroupTuplets` | a `(3` marker and the events it covers, and what its numbers mean |
| `GroupBeams` | the events written together with no space between them |
| `GroupBars` | what is between two bar lines |
| `ResolveNotes` | what each note sounds and how long each event lasts |
| `AlignLyrics` | which syllable is sung on which note |
| `CheckDrawable` | what is written correctly and still cannot be drawn |
| `ShowAsWritten` | the stretch under the caret, shown exactly as typed *(shared)* |

Each needs the answers of the ones before it. Nothing can work out a length without the unit note length;
nothing can group a tuplet without the meter that gives its default; a tuplet beams as one group, so it
has to be a group before the beams are found; a beam never crosses a bar line, so the beams have to be
found before the bars close; and an accidental lasts a bar, so the notes cannot be resolved until the bars
exist.

**A measure never crosses a source line.** A bar that runs on comes back as two measures, the first saying
it is unfinished. A line ending in ABC is a suggested system break, so the two halves are drawn on
different systems anyway, and a node that spanned the break would have to hold the line ending in the
middle of itself and would still draw as two.

**A note is four leaves** — an optional accidental, the letter, optional octave marks, an optional
length — and that is what makes the note gestures cheap. Sharpening replaces the accidental; moving an
octave replaces the marks and the letter's case; lengthening replaces the length. Each is an edit to one
leaf and nothing else, so a tune somebody lined up by hand still reads that way afterwards.

## The oracle

Two things, because there are two questions.

**Is the reading right?** A hand-written construct list — `AbcConstructs` in `Nexaflow.Tests.Fixtures` —
covering every construct the reader knows. It is the record of what is supported and it is what a new
construct is added to.

**Does it hold up on input nobody here wrote?** Ten thousand real tunes, opt-in via `NEXAFLOW_ABC_CORPUS`
(default `D:\Datasets\abcmusic\zenoob`). Both invariants and the pipeline's own rule run over all of them
in about four seconds; no fonts, no desktop, no rasteriser.

It earns its keep. The construct list had nothing like `|  |  |  | E4E2E2 |` — three bar lines at the head
of a line that open nothing — and `GroupBars` was dropping the first two of them. Six tunes in ten
thousand showed it; the shape is in the construct list now, so the cheap run catches it too.

**Is the drawing right?** The corpus ships a picture beside every tune, made by an engraver that was never
ours, and `AbcPictureSweepTests` holds our page against it. Four things had to be settled before that
number meant anything:

- **A control, or the mean is unreadable.** Every page is also scored against a *different* tune's
  picture. Two pages of music share most of their ink just by both being pages of music, so 0.52 against
  your own reference is only worth knowing beside 0.38 against a stranger's. The gap, and how often a page
  beats its own control, is the whole of what the sweep can see.
- **Ours is given the reference's own width, because that is what a window does.** A score is not a
  picture: its width is whatever it is asked to fit into, and everything else follows from that. So the
  reference's ink is measured, our page is given those pixels plus a couple of air, and what comes out is
  compared. Handing the engraver a width and asking what it does with it is a test of the thing rather
  than a way around it.
- **And at their size, which is measured off each reference rather than chosen once.** Every reference is
  fitted to an 800×600 box (2,397 of 2,400 sampled touch an edge exactly), so a thumbnail was scaled by
  whatever it took to fit and its pixels are not a page's pixels. The corpus is therefore drawn at as many
  sizes as it has tunes: over 720 sampled the staff space runs from 4.0px to 6.5px, asking our notation for
  anything from half its natural size to four fifths. A mean is the one answer guaranteed to fit none of
  them. So each reference is asked how big its own staff is — `GrayImage.StaffSpace` — and ours is drawn at
  exactly that. `NEXAFLOW_ABC_ZOOM` still pins every tune to one size, which is occasionally what you want
  to look at and never what you want to measure.
- **Measured off the empty stave at the right end of the systems**, which is the only clear staff on the
  page: past the last note the lyrics and the note heads have stopped and five lines are all that is left.
  A profile of the whole page measures note heads instead and returns half the true spacing, because heads
  sit on lines *and* in spaces. The template is five taps at the spacing, minus the four between them and
  two just outside — the outer pair are what stop it locking onto the pitch between one staff and the next,
  which scores just as well on five taps alone and is wrong by a whole system.
- **Shape is read, not aimed at.** Aspect ratio is not a property of a score anybody wants. But given the
  same width at the same size, a page half the other's shape has twice its systems — so the number is kept
  as a diagnostic that separates a low score meaning *we drew it differently* from one meaning *we broke
  it differently*.
- **Detail chosen by measurement, not taste.** Swept over 24/56/96/160, how often a page beats a
  stranger's picture peaks at 56 and falls away above it. Two engravers agree about where the music is and
  disagree about every pixel of it, so asking at pixel scale is asking a question neither can answer.
- **Bucketed by how much there is to get wrong.** A two-bar jig and a five-system hymn are different
  problems, and a mean over both says which we are bad at only by accident.

**Both pictures are cropped to their ink first**, which is not tidying. The corpus fits every picture into
a box, so the file's own width is the box's rather than the page's — and the page's is the number the whole
comparison is built on.

**And the buckets say the sweep is blind on the easy half.** Under 48 notes it separates by 0.07 and beats
its control 66% of the time. Over 320 notes it separates by 0.33 and beats it 92%. That is not the engraver
being better at hard tunes: it is that any two short pages look alike, so there is nothing for a fuzzy
comparison to catch hold of. The long tail is where the evidence is.

**Where it stands.** Over all ten thousand: 0.525 against its own picture against 0.382 against a
stranger's, and 75% of pages beat their own control. 6,221 of them match the reference's shape to within
3%, and those separate by 0.196 and beat their control 84% of the time. The 2,232 that come out below 0.70
of the reference's shape separate by 0.018 and beat it 53% — a coin — so that band is a line-breaking
difference rather than a drawing one, and it is where the next look belongs. Six tunes engrave to nothing,
and all six are a header with no music under it.

**The corpus is read as bytes and never re-encoded.** It was built by something that read UTF-8 as
Latin-1, so a good third of it carries mojibake (`AntÃ­fona`). That is not a defect in the fixture — it is
exactly the input a claim of "the parser only ever copies" has to survive, and a test that normalised it
first would be testing a tune nobody has.

## Editing

An edit is an operation on the tree, and the source is how the tree is written down.

**Part in, new whole root out, print, re-read, rebuild.** What an edit hands back is *provisional*: the
stages between the parser and the builder do not re-derive themselves when a tree changes underneath
them — a note whose accidental has just moved still carries the pitch worked out for the old one — so an
edit prints, and the source it prints as is read back and built from. One path, always taken, therefore
always right. It is also why editing a tree is worth the trouble: an edit expressed against a part knows
what it touched, so trouble afterwards can be blamed on the keystroke that caused it rather than guessed
at by diffing a string.

ABC makes this unusually cheap. **A note is four leaves** — an optional accidental, the letter, optional
octave marks, an optional length — so each gesture rewrites one leaf and nothing else. A tune somebody
lined up by hand still reads that way afterwards.

| Gesture | What it writes |
|---|---|
| `A`–`G` | a note in the octave the one before it was in |
| Page Up / Page Down | `C,` → `C` → `c` → `c'`, case and marks together |
| `#` / `_` | one semitone up or down, **from what the note sounds** — see below |
| `+` / `-` | double or halve the written length; dots survive |

**The accidental gesture asks what the note sounds, not what is written in front of it**, and that is the
difference between a gesture that works and one that surprises. A bare `F` in G major is an F sharp, so
flattening it must write `=F`: taking an accidental away would leave the key signature to sharpen it
again. The stage worked the sounding pitch out already, and this is what those facts are for.

Plain typing is not an edit operation. A letter inserted at the caret needs no reshaping — ABC has no
construct that must be bracketed when it grows — so the element splices it, exactly as the formula editor
splices a character its own tree did not have to reshape.

### The seam

`IEditableBlock` is the whole of what a document needs to drive rendered content: its source, its layout,
what is selected, what could not be read, a caret that can be handed in at an edge and handed back out,
and the keys that change it. A block that implements it is selected across, arrowed into, typed in and
spliced back by host code that knows nothing about what it holds.

What is *not* shared is declared on the same interface rather than recognised by type:
`HandleKey`, `MoveCaretVertically`, `Commit`, `SelectNextPlaceholder`, `BuildRibbon` — all defaulted to
declining. A formula claims Space, Enter and Tab; a score claims Page Up, Page Down and the
sharpen/lengthen keys; a barcode claims none, and says nothing. These used to be `is FormulaElement` tests
in the host, which was honest while a formula was the only block with keys of its own and stopped being so
at the second.

## What a new language has to bring

1. A parser producing `ContentNode`s, obeying both invariants, and a `Kinds`/`Roles` set of its own.
2. Whatever stages it needs, each obeying the pipeline's rule.
3. A builder that walks a `ContentReading` and emits `ILayoutNode`s, each carrying the `ContentPart` it
   was drawn from — or nothing at all where it was drawn from nothing anybody wrote.
4. A `FrameworkElement` implementing `IEditableBlock`, which is what the caret, selection and the prose
   seam are written against.

Everything else — `LayoutQuery`, `ContentSelection`, `CaretPlace`, `DocumentSelection`, and the whole of
`InlineMarkdownEditor.Blocks.cs` — it gets for nothing.
