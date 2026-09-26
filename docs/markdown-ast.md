# The content pipeline

Every piece of content Nexaflow draws — a markdown document, a formula, a diagram, a tune, a barcode — goes the same
way, and one engine takes it there:

```
source
  │
PARSE       the language's parser     → ContentNode   lossless: Print(Parse(s)) == s, and it only ever copies
  │
NEST        the engine                → ContentNode   every piece written in another language parsed by that
  │                                                   language and put in the piece's body, all the way down
  │
STAGES      the language's stages     → ContentNode   IAstStage actors, each handing back the same characters
  │
BUILD       the language's builder    → Laid          a layout tree, every piece saying what it was drawn from
  │
PAINT       ContentElement            → pixels        painting, hit-testing, the caret, selection, editing
```

**Each step belongs to one thing, and only the engine runs them.**

| | Does | Never |
|---|---|---|
| A **language** (`ContentLanguage`) | describes its parser, its stages, its builder, and what editing means in it | runs any of them, or asks for another language |
| The **engine** (`ContentEngine`) | parses, nests, runs the stages, makes the builder | draws |
| A **parser** | reads characters into a tree | works out what they mean |
| A **stage** | works out what the tree means and hangs the answer on it | changes a character |
| A **builder** | decides where everything goes | reads source, parses, chooses stages |
| The **element** | paints the layout and turns gestures into edits of the source | lays anything out itself |

A layout is made at a standard size, in the content's own units: text is measured at `LayoutText.Density` and the
element scales the finished tree as it paints (`Zoom`). The same source and the same room give the same tree on any
display, which is what makes a layout something a test can measure. The one size a builder is told is the **room** it
has, because where a line breaks is a layout decision; how big the content is set is a fact about the content.

## Languages

A language is a description (`ContentLanguage`): which fence words it answers to, how to make its parser, the stages
its parse is worked over by, how to make its builder, and `Editing` — an `IContentLanguage` saying what a key, a
gesture and a block's corner mean in it, where they mean something of their own. The ones that ship are
`Languages/Shipped`; `ContentLanguages` is the one table of them, and markdown is what content is written in where
nothing names a language. A language's description runs nothing and names no other language.

A language's stages are chosen from the tree and from the showing it is laid for (`ContentShowing`: what it is drawn
in, whether somebody is writing in it, the stretch shown as typed, where it starts in the document holding it, and what
the host said — pictures, links, what diagrams are bound against). So a diagram's stages follow the diagram its header
names, and a block being written in gets the stage that puts holes where something is still to be written.

## The engine

`ContentEngine` lays content out, and nothing else does:

1. **Parse** the content with its language — markdown where nothing names one.
2. **Nest.** Wherever the parser named another language (`Kinds.Language`), parse the piece's own characters with that
   language and put the tree in the piece's body (`ContentNested.Reading`); then the same inside that tree, until
   nothing is left unread.
3. **Stages.** Run the language's stages. A stage sees only its own language's nodes: it works over the characters of
   any piece in another language, and the piece's tree goes back into the body once the stages are done.
4. **Build.** Make the language's builder with the worked-over reading and lay it out at the room it was given.

A builder that meets a piece in another language asks for it with `Nested(part, room, style)` — the one thing a builder
may ask for. The engine works that piece's tree over with its own language's stages and lays it out with its own
language's builder, at the room and in the style the asking builder chose, and hands back a `ContentInset` to set down
where the builder decides. So the room a fence gets in a narrow table cell, the size a formula is set at in a sentence,
and where either goes all stay with the builder drawing the thing that holds them.

`ContentEngine.Read` does the first three steps and lays nothing out, for whatever wants to know what content says
rather than to draw it.

**One engine per showing of some content**, because what it keeps is that content's: the parse of a document read again
as it is written, what every nested piece read to (a keystroke in a paragraph does not read the diagram under it again),
the blocks that read as they did last time, and what the reader has opened in each diagram. What the source does not say
— which nodes of a diagram are open, what a binding is bound against — the host says has changed with `Forget`.

## The tree

`src/Nexaflow.Markdown/` — no WPF, no dependencies. `Ast/` holds the tree and what every language shares; each language
has a folder of its parser, kinds, stages and model.

**The tree owns its text.** No offset is stored anywhere: a node knows how wide it is, and where it stands is worked out
by a walk when somebody asks (`ContentReading`, `ContentPart`). An edit that replaces a subtree cannot leave a stale
position behind, because there were none.

**Two invariants, and the second holds the first up:**

1. `Print(Parse(s)) == s` — for every input, malformed included. Nothing a reader can type is outside the tree.
2. **A parser only ever copies.** Every leaf's text is in the source at the offset the tree puts it at; nothing is
   synthesised, normalised or inserted.

The first alone is weak — a parser returning the whole input as one leaf passes it, and so does one that quietly repairs
what it read. The second is what stops recovery from inventing: the temptation on meeting `[CEG` is to close the
bracket, and a parser that does round-trips everything except the half-finished input an editor holds all day. Input a
parser cannot make sense of is held as written, carrying the reason.

**`Kind` and `Role` are open strings.** A shared tree cannot hold an enum of every language's kinds and needs none;
`Kinds` and `Roles` carry only what is genuinely shared.

**Every parser makes the same node, and the stages make it their language's own.** A parser's `ContentNode` is what
every tree shares: what a piece is, its characters or its parts, and so where it stands in the source. A stage may put
a node of its language's own type in its place (`PieSliceNode : ContentNode`) — standing for exactly the same
characters, and carrying, typed, whatever was worked out about them. What such a node holds is between that language's
stages and its builder; nothing shared reads it, so the type is internal to the assembly the stages are in, seen only by
the builders and the stages' tests. Nor is one needed: a node of its own is for something the stages work out that the
characters do not say, and a builder reading what was written in the order it was written needs none. A rewrite keeps a
node's type (`ContentNode.Reshaped`), so a later stage, or the engine putting nested content back, never loses what an
earlier one said.

**What stands for no characters at all is a derived part** (`Roles.Derived`): no width, printed as nothing, hung under
the node it is about — a hole, what a macro means, which picture a name resolved to (`ContentNode.Held`, untyped
because what a name resolves to is often something this assembly cannot name).

**A tree the builder has laid out is finished.** Nothing rewrites it: editing works from where its parts stand in the
source, writes the source, and the engine reads it again.

## Content in another language

The parser reading the outer content says only that a piece is written in another language and which: a derived
`Kinds.Language` part holding the word — a fence by the word after it, a formula by being one (nobody writes a language
after `$$`), a diagram label by the word after its own fence. The piece's `Roles.Body` holds its characters. Once the
engine has read them, the body is a `Kinds.Nested` node holding that language's tree and, where the characters had one,
the line ending that closed them — which belongs to the line the closing delimiter stands on, and is not the other
language's (`ContentNested.Own`). `ContentNested` answers the questions about all of this: which language a node holds,
the holders from a part upwards, what was read in one, and the parts of a tree that are its own language's
(`OwnParts` — what a builder reports trouble from, since a nested language answers for its own).

## Stages

```csharp
public interface IAstStage { string Name { get; } ContentNode Run(ContentNode tree); }
```

**The one rule: `stage.Run(t).Print() == t.Print()`.** A stage may group pieces whose characters sit side by side, split
one, replace one with a node of its language's own type, or hang derived parts underneath, as long as the characters
coming out are the ones that went in. What belongs together but was written apart is gathered under a parent of the
language's own rather than into one node. `AstPipeline` checks the rule between stages in a debug build and names the
stage that broke it. `AstRewrite.Regrouping` keeps a node's own derived parts from being swept into a group made of its
children, where they would no longer be true.

Which stages a language runs, and in what order, is that language's and changes as it learns to read more; that they
are actors, compose and leave the source alone is fixed. A parser is not a stage: where one token stops and the next
begins is a fact about the text no later stage can change.

**Nothing is incremental.** An edit can reshape everything after it — a closing brace, a bar line — so the whole content
is parsed and worked over again: one path, always taken, therefore always right. What is saved is reading and laying out
what did not change (see *Markdown*), which is where the time goes.

## Builders

A builder is made by the engine and by nothing else, always the same way — what was read, what is being written, what it
is drawn in, whether it is read-only, and `Nesting` — and gives back a `Laid` from `Build`. It exposes nothing else.
`ContentBuilderRulesTests` holds the shape; `MermaidDiagramRulesTests` holds that no builder names the table of languages.

**A builder never touches the source.** It works in tree parts and layout pieces: it never reads characters, prints the
tree or works out which characters a part came from — structure it needs belongs in a stage. Every piece it lays says
which part it was drawn from, or nothing where nothing anybody wrote was drawn.

**The room is the builder's to use.** `Within` says how much of the room it is handed it lays out within: a plot keeps to
a panel a reader can take in, a symbol is drawn at the size its modules say, and a score takes four fifths of a page and
sits in the middle of it.

**Laying out always produces a layout.** `ContentBuilder.Lay` catches: a builder is handed half-typed content all day,
and "the content vanished" is never the way to say so. So there are three answers, all a `Laid`: what the reader meant,
where it can be read; the source shown as written, where it cannot; and the same with the reason, where drawing threw.

### What cannot be drawn

The most a builder can say about something it cannot draw is which part it was given and why —
`AsSource(parts, reason)`, or `Diagnostic.Of(part, reason)` for something wrong in what it did draw. Showing content as
written is one helper's (`SourceShown`): it prints the tree back, puts a piece standing for each blamed part over its
characters, and writes each reason beneath. The same helper shows a builder that threw, and whatever the element falls
back to.

1. **The reading makes no sense** — it is not the language: shown as written, the error marked.
2. **Something is missing** — not yet written: a hole where it goes, while the content is being written only.
3. **A rule of the language is broken** — it read, but means something that cannot be: drawn with a wave under the part
   where the content is being written and that part is drawn as words the reader types into; otherwise shown as
   written, the part marked.

A diagram that draws nothing asks to be shown as written, blaming what it could make nothing of. A formula keeps its
typesetting while it is written, and something it read but cannot draw is a warning in place. A tune, a structure, a 2D
code, a plot and a word cloud are never typed into where they are drawn, so anything wrong shows them as written. A
barcode's value is typed into where it is printed, so a value that will not encode keeps a faint symbol with a wave
under it while it is written.

**Nothing drawn is never an answer.** Where a language lays nothing out, what goes there is the characters typed, in a
box ruled in the colour of trouble, with the reason under them — still where it was written and still somewhere the
caret can go. The seam asks `Laid.Draws` rather than whether a tree exists, because a tree can hold nothing visible.
`ContentLanguageDrawingTests` sweeps every language against content written right and written wrong.

## The layout tree

`src/Nexaflow.Visuals.Text/Editing/`, because a mark paints onto a WPF `DrawingContext`.

A piece stands in its box: a press means the piece whose box it lands in, and a selection washes boxes. Where a box badly
overstates the drawing — a pie's wedges share a square — the builder says which shape the piece stands in
(`LayoutBuilder.Occupies`).

**A run of text is one piece with a place between any two of its letters** (`LayoutWords`): it answers which letter a
press landed on, where the caret stands and what a selection covers, from the text it was set with. A run says whether
what is drawn is what was written, which is what makes a caret possible in it, and whether pressing it shows what was
written — a number set to two places, a title without its quotes. A run saying something *about* the source without
showing it — the share a slice takes — is neither, and stepping never stops in it.

**A block keeps the picture it was painted as** (`LayoutKept`), so a keystroke paints the block typed in and a caret
blinking paints nothing. Only the blocks near the screen are painted (`ContentElement.OnScreen`), and the picture of a
block scrolled far away is let go.

## The element and the surface

`MarkdownSurface` is the one control a page hosts, as many times as it shows content: it owns the scroller, the
keyboard, the history, what a search turned up and the buttons in a block's corner. Inside it one element
(`MarkdownElement`, a `ContentElement`) owns the laid tree, the caret and the selection, and lays out through its engine.
A whole document is one element — the prose, the diagrams and the tunes are pieces of one tree, so a drag runs from a
word into a chart with nothing forwarding gestures between controls.

**Who answers a key.** From the piece holding the caret up the layout to the first piece naming a part of the tree, then
up the tree to the first part holding another language (`ContentNested.Holders`): that language's `Editing.OnEdit` is
asked what the key means (`IOnEdit`: typing, settling, taking back, and what an edit came to). Where it says nothing the
key does to the characters what a key does; no such part means markdown's own source, answered by `MarkdownEdits`. A
language is told in the document's offsets (`ContentEdit`), because it is laid at the offset its source starts at; a key
taking back characters stops at the edges of its source, and takes the whole construct once nothing is left inside.
LaTeX spells a command as itself and settles it on Space or Enter (`LatexEdits`); Mermaid escapes what a place cannot
hold, starts its next line on Enter and carries a rename to where the name is used (`MermaidEdits`); markdown writes
typed markup behind a backslash, continues a list on Enter and joins two paragraphs on backspace.

**What a key means where the caret is, is the content's** (`IContent.Typing`, `Settle`, `Erasing`, `Edited`). Space and
Enter both arrive at `Settle`, so content made of lines starts another on Enter while a formula settles what is
half-written. A diagram starts its next line under the one the caret is on, as its grammar starts one there
(`IMermaidGrammar.Blank`), with the caret in its first hole; what a place cannot hold is escaped as it is typed
(`IMermaidGrammar.Escaping`); a name renamed where it is declared is renamed where it is used (`IMermaidGrammar.Names`),
where it is declared once.

**Whatever the edit came to, the whole content is read again from its source.** An edit to the tree is provisional — the
stages do not re-derive themselves underneath it — so it prints, and what it prints is parsed and laid again. An edit
against a part knows what it touched, so trouble afterwards is blamed on the keystroke that caused it.

**The rest is shared.** Shift chooses from where the choosing started and Ctrl adds or takes back what is pressed
(`ContentElement.BeginPointerSelect`); up and down go to the nearest line with somewhere to stand, at the place nearest
the column (`LayoutQuery.StepVertical`); undo takes back a stretch of writing, each step the whole document as it stood
(`EditHistory`); the pointer is a bar only over what can be written in (`LayoutQuery.Writable`). A block's corner offers
what its language says (`Editing.Corner`, `Editing.Offers`): code no picture of itself, prose no corner at all.

## Markdown

**A document is a list of blocks, and each block is its own content.** `MarkdownParser` reads in two passes: where each
block starts and which it is, and which language every fence and formula is written in (Markdig decides the boundaries
and nothing else); then each block in turn (`MarkdownBlocks`) — what it holds, by the reader for its kind
(`MarkdownInline`, `MarkdownList`, `MarkdownTable`, the block reader again for a quote's body), with every piece finished
as it is read: what pieces side by side make together (`MarkdownGroups` — a definition list's pairs, an alert's marker,
the display formula a paragraph of nothing but `$$ … $$` is) and the line ending closing a block's last line
(`MarkdownClosingLines`). A kind nothing reads is shown exactly as typed. A reader also records
what the characters do not say — a column's alignment, the squares a cell covers, which alphabet a list counts in — as
derived parts.

**A fence is a delimiter, another language, and a delimiter**, and so is a formula: `$$ … $$` is the same shape with the
language implied, and `$x$` is that shape small, framed by words instead of by lines.

| Stage | What it works out |
|---|---|
| `WithImages` | the picture an `![alt](where)` names, where this showing can find one (`MarkdownPictures`) |
| `WithLinks` | how this showing wants each link to look |
| `WithUnchanged` | which blocks read exactly as they did last time |
| `ShowBlocksAsWritten` | the block somebody is changing the markup of, as the characters it is written with |

**A block written as it was is read as it was.** The engine keeps a parse per document (`MarkdownParser.Parsing`) that
hands back every block written exactly as last time beside the same definitions as the very block it handed out then,
finished, so a keystroke reads and walks only the block it was typed in. `RereadingTests` holds such a reading to the
same document read from nothing.

**A block that reads as it did is laid as it was.** `WithUnchanged` says which of this reading's blocks are one the last
reading had — the same characters and everything worked out about them, so a paragraph whose link was defined again
elsewhere is not the same. What the builder laid for such a block (`LaidBlocks`, by where the block comes among the
document's parts) is set down again, only what its pieces stand for moved along by the amount the edit moved everything
after it. A block shown as written is never kept. What a block means on the day it is read — a Gantt chart's today — is
part of its reading, so it is laid again when it is read again. `LaidBlocksTests` holds every sample, typed into and
taken back, to the same source
laid from nothing.

**The builder sets the size of what it holds.** A formula on a line of its own is drawn half as big again as the words
round it and one in a sentence at the sentence's size, so the markdown builder hands that style with the room when it
asks for the formula. A run holding another language is measured by its inset, never broken, and sat centred on the
middle of the words. An unreadable display formula keeps its typesetting with a wave under what it could not read; an
inline one that could not be set is shown as written, dollars and all, with the sentence reading on round it.

**Words are gathered into runs, broken into lines, and joined back up**, so `a **bold** word` comes out as three pieces
rather than eleven. **A picture is one mark** in a piece standing for the characters it was written as, fitted down to
600 either way and never up; one nothing was found for draws its alt text.

**Finding a place asks the source**, where the words are whole: a search reads the source and `LayoutQuery.RangeRects`
turns its offsets into places on the page, through every nested language, since each is laid at the offset its body
starts at. What is drawn as other than what was typed — an entity, a renumbered marker — says so on its run and is found
the same way. **A saved reference says what a thing is, not where it sits** (`heading:getting-started/list/item#2`), and
lands as far as it still goes. **A link into the document is the element's**: which heading `#notes-1` means is settled
by the order the headings were written in, so following one is never handed to the host.

## The languages

**LaTeX** — its own document: [latex-parse-tree.md](latex-parse-tree.md).

**Mermaid** — one parser for every diagram, because they all open the same way: front matter, comments, directives, the
header (the first line that says anything, naming the diagram — `MermaidDiagrams`), accessibility lines; every other
line a statement its diagram's grammar reads (`IMermaidGrammar`). The grammar names the diagram's stages, and every
diagram's grammar and builder are built from the kit. How a diagram is added and what the kit holds:
[mermaid-diagrams.md](mermaid-diagrams.md).

**ABC** — the order of its stages is the argument:

| Stage | What it works out |
|---|---|
| `ResolveFields` | what each field's value says for its letter: a key and clef, a meter and its sign, a unit length, a voice |
| `ResolveContext` | the key, meter, unit note length and voice in force on each line |
| `GroupTuplets` | a `(3` marker and the events it covers |
| `GroupBeams` | the events written together with no space between them |
| `GroupBars` | what is between two bar lines |
| `ResolveNotes` | what each note sounds and how long each event lasts |
| `AlignLyrics` | which syllable is sung on which note, and where it was written |
| `ResolveMarks` | the mark each decoration names, however it is spelled, and whether a quoted run is a chord or placed text |
| `CheckDrawable` | what is written correctly and still cannot be drawn: a decoration naming no mark |
| `ShowAsWritten` | the stretch under the caret, shown as typed |

A length needs the unit note length; a tuplet's default needs the meter; a tuplet beams as one group; a beam never
crosses a bar line; an accidental lasts a bar. A measure never crosses a source line, because a line ending is a
suggested system break. **A note is four leaves** — accidental, letter, octave marks, length — so each gesture rewrites
one: `A`–`G` writes a note in the octave of the one before, Page Up and Down move the octave, `+` and `-` double and
halve the length, and `#` and `_` move a semitone **from what the note sounds**, so flattening a bare `F` in G major
writes `=F`. Each stage says its answer in the tune's own nodes (`AbcFieldNode`, `AbcLineNode`, `AbcTupletNode`,
`AbcEventNode`), and a mark or words written against a note in the ones every notation shares (`MusicMarkNode`,
`MusicAnnotationNode`), so `AbcBuilder` only walks the tune. LilyPond is the same engraving, walked by a builder of its
own: the two notations think about music differently, and share `MusicBuilder` for what a score is drawn with.

**2D codes** — `MatrixParser` reads a `qr`, `aztec`, `pdf417` or `datamatrix` body into lines of fields, one grammar for
all four, and each builder encodes and lays the symbol out as the parts it is made of (finders, timing lines, a
bullseye, row indicators). What they share is `MatrixBuilder`. No piece carries a part, because nothing drawn was typed.

**Barcodes** — the same grammar, the value spelled out a piece per character by the parser, because each character
printed stands for one written; while the block is written in, `HoldValue` gives a value not yet written a hole.

**SMILES** — `ConnectAtoms`, `CountHydrogens`, `Kekulize`, `DepictStructure`, each needing the one before, and each saying
what it works out in the molecule's own nodes (`MoleculeNode`, `AtomNode`). A bond between two atoms written side by side
has no characters, so a molecule's bonds name atoms by the order written. Where the atoms go is a stage's answer too
(`DepictStructure`, through `StructureLayout` and `CageLayout`): it is a fact about the molecule, not the room, so the
builder only scales it to its room and draws it.

**Word clouds** — a parser and nothing between it and the builder: nothing about a cloud's line means anything its
characters do not say. The layout tree is flat on purpose — every word placed absolutely, one run each — and the
packing (`WordMask`, `WordCloudBoard`) is worked out beside the model; the builder brings only what a type engine knows,
the outlines of the letters. A `mask:` picture is found by the host (`WithPictures`).

**Correlation plots** — `scatter`, `bubble`, `heatmap` and `density2d` differ in what is drawn, not in what is written.
The parser decides only the shape of a line; the settings (`ResolveSettings`), which row names the columns
(`ResolveShape`), each column (`ResolveColumns`), what a cell reads as (`ResolveValues`), which channel a column feeds
(`ResolveAesthetics`), for `geom: corr`, the coefficients (`ResolveCorrelations`) and what a `stats:` line reports
(`ResolveStatistics`) are stages, because every one of them changes as the next line is typed; each is said in the
block's own nodes (`PlotBlockNode`, `PlotRowNode`, `PlotCellNode`). Binning, densities and fits are the builder's to ask
for, because they are worked out on the panel it lays out, and are tested against R's published numbers.

## The oracles

A construct list says what is supported (`AbcConstructs` and its peers in `Nexaflow.Tests.Fixtures`), and a corpus says
whether it holds up on input nobody here wrote: ten thousand tunes (`NEXAFLOW_ABC_CORPUS`), SmilesDB against RDKit
(`NEXAFLOW_SMILES_CORPUS`), LaTeX against its own renderings. A corpus is read as bytes and never re-encoded — mojibake
included, because "the parser only ever copies" has to survive it.

`AbcPictureSweepTests` holds our page against the engraving each tune ships with, and the number means something only
because of how it is measured: every page is also scored against a *different* tune's picture, since two pages of music
share most of their ink by being music; ours is given the reference's width and drawn at the staff size measured off the
reference's own empty stave; both are cropped to their ink; detail is compared at the scale where two engravers still
agree; and results are bucketed by how much music there is, because short pages all look alike.

Refactoring guards are opt-in and run beside any change to how content is reached: `LayoutSnapshotTests`
(`NEXAFLOW_LAYOUT_SNAPSHOTS`) holds every piece of every sample to a recorded layout, and `DiagramSnapshotTests`
(`NEXAFLOW_DIAGRAM_SNAPSHOTS`) every diagram to a recorded picture.

## What a new language brings

1. A parser producing `ContentNode`s under both invariants, with kinds of its own — naming the language of anything
   written in another one.
2. The stages it needs, each keeping the characters it was handed.
3. A builder that walks a `ContentReading` and lays out pieces each carrying the part it was drawn from, asking
   `Nested` for anything in another language.
4. A `ContentLanguage` describing the three, in the table — and, where a key means something other than its
   characters, an `IOnEdit` offered through its `Editing`.

The caret, selection, choosing, undo, the clipboard, search and painting come with the element.
