# The rendering pipeline

One syntax tree that owns its text, a pipeline of actors that re-read it, a builder that decides
geometry, and a layout tree that answers what a click meant. Four stages, and only the first and third
of them know what language they are looking at.

Everything markdown renders is the same four stages. Maths got there first
([docs/latex-parse-tree.md](latex-parse-tree.md)); music is the second; barcodes, the 2D codes and
chemical structures run on it; diagrams and markdown's own text blocks follow.

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

**Each arrow is somebody else's.** A builder is handed a `ContentReading` and gives back a `Laid`; it does not read
source, does not choose stages, and does not outlive the drawing. That is why every builder is the same shape — one
constructor taking what was read, what is being written, the colours and whether it is read-only — and why a
capability that needs the tree changed is a stage rather than something a builder works out while drawing. The shape
is held by `ContentBuilderRulesTests`.

**A layout is at a standard size, in the content's own units.** Nothing in it knows the screen: text is measured at
`LayoutText.Density`, and `ContentElement` scales the finished tree as it paints (`Zoom`, a transform on the drawing
context, with pointer input divided back through `Unscaled`). So the same source and the same room give the same tree
on any display, which is what makes a laid-out tree something a test can measure — and zooming costs a repaint rather
than a re-lay.

The one size that does reach a builder is the **room** it has, because where a line breaks is a layout decision and
only what measures the text can make it. How big the content itself is set — a formula's text size, a score's staff
size — is a fact about the content and reaches the builder as one.

## Where it lives

`src/Nexaflow.Markdown/` — `net10.0`, no WPF, no dependencies.

```
Ast/         ContentNode, ContentPart, ContentReading, ContentWords, ContentLink, ISourcePart, Roles, Kinds, AstWrite
             — a node also carries what a stage worked out that is not text (ContentNode.Held): a picture a name
             was resolved to, say. Untyped, because what a name resolves to is often something this assembly
             could not name — an image is a WPF object and nothing here knows about WPF.
Pipeline/    IAstStage, AstPipeline, AstRewrite, Stages/ShowAsWritten, Stages/WithHoles, Stages/WithBindings
Binding/     IDataContext, ReflectionDataContext, BoundText — what a {{…}} is read against
Music/Abc/   AbcParser, AbcTheory, AbcPipeline, AbcKinds, Stages/…
Matrix/      MatrixParser, MatrixKinds — the one grammar qr, aztec, pdf417, datamatrix and barcode share
Barcode/     BarcodeParser, BarcodeKinds, Stages/SpellValue — a barcode's value spelled out a character at a time
Chemistry/   SmilesParser, SmilesPipeline, Stages/…, Molecule, Elements, Depiction/… — smiles
Mermaid/     MermaidParser, MermaidBlock, MermaidDiagram, MermaidKinds, MermaidConfig — what every mermaid diagram shares
Mermaid/Pie/ PieGrammar, PieChart, PieConfig, Stages/ResolveSlices — what a pie says for itself
WordCloud/   WordCloudParser, WordCloudReader, WordCloudChart, WordCloudSettings, WordMask, WordCloudBoard, WordCloudShapes — wordcloud
Plot/        PlotParser, PlotPipeline, Stages/…, PlotReader, PlotSettings, PlotChart, PlotBins, PlotDensity, PlotFits — scatter, bubble, heatmap, density2d
```

The layout tree is still in `src/Nexaflow.Visuals.Text/Editing/` because `ILayoutNode.Bounds` is a
`System.Windows.Rect` and a `LayoutMark` paints onto a `DrawingContext`. It moves here once it has
geometry primitives of its own.

A piece stands in its box: a press means the piece whose box it lands in, and a selection washes boxes. Where
a box badly overstates the drawing — a pie's wedges share one square — the builder says which shape the piece
stands in (`LayoutBuilder.Occupies`), and pressing, marquee selection and the wash follow that shape. It is the
builder's to say rather than read off the marks, because a shape drawn is not always the shape meant: a note
head is drawn as an outline and is still pressed as its box.

**A run of text is one piece with a position between any two of its letters** (`LayoutWords`), rather than a piece
per character: the run answers which letter a press landed on, where the caret stands, what part of it a selection
covers and where a word ends, from the text it was set with. The caret keeper steps through a run a character at a
time, the way it steps through a stretch shown as its own characters, and a drag through one picks out characters
rather than being promoted to the whole piece — a run *is* the text, so there is nothing to promote it to.

A run says two things about itself. Whether what is drawn is what was written, which is what makes a caret possible
inside it; and whether pressing it shows what was written, which is what a formatted view of its own source does — a
number set to two decimal places, a title without the quotes it was written in. A run that says something *about* a
piece of source without showing it — the share of a pie a slice takes — is neither: it is nowhere to put a caret, so
stepping never stops in one, and a press on it means the slice it was worked out from.

**What a key means where the caret is, is the content's** (`IContent.Typing`, `IContent.Settle` and
`IContent.Erasing`). Space and Enter both arrive at `Settle` carrying the character that was pressed, so content made
of lines starts another one on Enter while a formula, which is one expression, settles whatever is half-written and
puts a space after it. A diagram starts its next line under the one the caret is on — never through the middle of a
label — written as its grammar starts one after that line (`IMermaidGrammar.Blank`, told what the line says: a pie's
is always `"" : `, and a Venn diagram's an item under a set, a union or an item, and a set anywhere else) with the
caret in its first hole. Backspace and delete (`IContent.Erasing`, told which side) take the characters of what a diagram's reader
wrote and never what holds it together: at the edge of a label, a value or a title, and in a hole, the key does
nothing — except in a line nothing has been written on, which is taken back whole.

**What a place cannot hold is escaped as it is typed** (`IContent.Typing`, asking `IMermaidGrammar.Escaping`), so a
character never stops a line reading. A quote typed inside quotes is written as Mermaid's entity code, `#quot;`
(`MermaidText`); a bare Venn name given anything but a word, or a bare bracketed label given a quote, a bracket or a
comment, is put in quotes to hold it. Text written with an entity code is drawn as what it reads, and shown as written
for as long as the caret is in it, the stretch shown growing as it is typed at the end of — so the caret always has the
characters it stands between.

**A name renamed where it is declared is renamed where it is used** (`IContent.Edited`, which every edit passes
through however it was made, asking `IMermaidGrammar.Names`). A Venn set is used by the unions overlapping it, the items
naming it as their region and the styles styling it, and an item by a style naming it alone; each use is written as the
grammar writes a name there, bare or in quotes. Only a name declared once is carried, and only onto one nothing else is
declared as — two sets of one name, or a rename onto another's, are left alone rather than guessed at.

**Shift and Ctrl choose as they do anywhere else.** A press is handed the modifier keys held
(`ContentElement.BeginPointerSelect(point, modifiers)`). Shift chooses from where the choosing started — the caret,
where nothing is chosen yet — to the press, as a drag there would; Ctrl adds what is pressed to what is chosen, or takes
it back out. A selection of several stretches is what `EditState` already holds for a matrix's column.

**Up and down go to the line above or below** (`LayoutQuery.StepVertical`): out through what the caret stands in to
the nearest row with somewhere to stand, then the line of that row nearest the one left, at the place nearest the
column — between two letters where that is a run. A fraction's numerator goes to its denominator past the bar; a
legend's row goes to the row under it; a title goes into a legend beside the chart, and the legend's top row back up.

**Undo takes back a stretch of writing** (`EditHistory`, held by the surface). A step is the whole document as it
stood, so taking one back is that document read again — an edit inside a diagram and an edit to a paragraph are the
same thing to take back, and nothing has to know how to reverse either. Writing that carries on from where the last
edit left the caret joins its step; putting the caret anywhere else starts the next, from where it was put.

**The pointer is a bar only over what can be written in** (`LayoutQuery.Writable`): on a piece that takes a caret, or
within half a letter's height of one, or inside a construct that is itself somewhere to write, such as a fraction. A
wedge, a swatch, a worked-out share and the card a diagram is drawn on show an arrow. Inside such a card only what is
on it counts: the page round it has nothing to say about a diagram's empty corner.

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
therefore always right. What is saved is the laying out, which is where the time goes — see *a block that reads as
it did is laid as it was*, under Markdown.

A parser is not a stage. What a name is shorthand for, and where one token stops and the next begins, are
facts about the text that no later stage can change.

## What cannot be drawn

**A builder never touches the source.** It works in tree parts and layout pieces, nothing else: it never reads
`Source`, never prints the tree, never works out which characters a part came from. Structure it needs belongs in a
stage. So the most a builder can say about something it cannot draw is *which part it was given* and *why* —
`ContentBuilder.AsSource(parts, reason)`, or `Diagnostic.Of(part, reason)` for something wrong in what it did draw.

**Showing a block as written is one helper's** ([`SourceShown`](../src/Nexaflow.Visuals.Text/Editing/SourceShown.cs)).
Given the tree, the blamed parts and why, it prints the tree back to its characters, walks it as the print does to find
where each blamed part's characters fall, puts a piece standing for the part over them, and writes each reason beneath
in the error colour. The waves under the blamed parts are the host's, drawn from the diagnostics handed back. Inline and
block content look the same. The same helper is what a builder that throws is shown as (`ContentBuilder.Lay`), and what
the element shows when the reading falls over before any builder has a tree (`ContentElement`). A document shows a
nested block that came back as its source through it too, as the whole of what the language is written in — fences
and all — which is the part holding the language: `ContentNesting.Holders`, the same climb the edit routing makes.

**Which way something goes, by why it cannot be drawn:**

1. **The reading makes no sense** — what is written is not the language: it is shown as written, the error marked.
2. **Something is missing** — not yet written: a placeholder where it goes, while the block is being written only.
3. **A rule of the language is broken** — it read, but means something that cannot be:
   - where the block is being written in and the part to put right is drawn as words the reader types into, it is
     drawn, with a wave under that value in place;
   - otherwise — the block is only read, or the part is not one typed into where it is drawn — it is shown as written,
     the broken part marked.

Mermaid carries this out for every diagram (`MermaidBuilder.Build`): a diagram that draws nothing asks to be shown as
written (`AsWritten`, blaming the parts it could make nothing of — or, where it says nothing, every line after its
header), and trouble in one that drew is kept in place only where each wrong part is drawn as typed words and the block
is being written in. A formula keeps what is written wrong in place while it is being written, and is shown as written
where it is only read; something it read but has no drawing for is its own shortcoming, not the writer's, so it stays a
warning in place. A tune, a structure, a 2D code, a plot and a word cloud are only ever read where they are drawn —
nothing in them is typed into there — so anything wrong in one shows it as written, marked. A barcode's value is typed
into where it is printed, so a value that will not encode keeps its faint, struck-through symbol with a wave under the
value while it is being written and printed; anywhere else, and for anything else wrong with the block, it is shown as
written with the piece at fault marked. A document shows a nested
block that came back as its source through the same helper, fences and all.

## Markdown

**A document is a list of blocks, and each block is its own content.** `MarkdownParser` says only where each
block starts and which of them it is — Markdig decides the boundaries and nothing else, because where one
block stops and the next begins is a question with a decade of corner cases behind it. What a block *holds*
is settled by whoever reads that kind, when it is read (`WithBlocks`): a paragraph, a heading and a table
cell by `MarkdownInline`, a quote's and an alert's body by the block reader again, a list by `MarkdownList`,
a table by `MarkdownTable`. That is what lets a keystroke re-read one paragraph rather than a thousand-line
file, and what lets a kind nothing can read yet be shown exactly as it was typed.

**A fence is a delimiter, another language, and a delimiter**, and so is a formula: `$$ … $$` is the same
shape with the language implied by the marks instead of written after them, and `$x$` is that shape again,
small. So `MarkdownKinds.Math` and `MarkdownKinds.Formula` are read exactly as `MarkdownKinds.Fence` is —
an opening token, a verbatim body, a closing token — and nothing downstream has a third case to learn.

| Stage | What it works out |
|---|---|
| `WithBlocks` | what each block holds, read by the parser its kind names |
| `WithGroups` | what pieces read side by side make together, in one walk: the pairs a definition list is, the marker an alert's `[!`, name and `]` are, and the display formula a paragraph of nothing but `$$ … $$` is |
| `WithNested` | which language reads what is written inside a piece, and how big it is set |
| `WithImages` | the picture an `![alt](where)` names, where this showing of the document can find one |
| `WithLinks` | how this showing of the document wants each link to look |
| `WithUnchanged` | which blocks read exactly as they did the last time the document was read |

A reader is also asked, while it still knows, the things the characters do not say: which way a table's
column is set, how many squares a cell covers, whether a cell holds blocks or a run of words, and which
alphabet a list counts in — <c>i.</c> being the roman numeral one or the ninth letter depending on what
stands above it. Each is hung on its node as a derived part, which draws and is not source.

**A reading that learned nothing is not kept.** Some blocks read as themselves when read alone: the `:`
line of a definition is a definition item, and a footer's line is a footer. `WithBlocks` compares what came
back against what it handed over and keeps the source where they are the same, so nothing reads itself for
ever — and `MarkdownParser.Inside` goes through a block that covers the whole of what it was given, because
that block *is* the one being read rather than something inside it.

**What a construct means is not always in its characters.** An abbreviation's meaning is written on a line
of its own somewhere else in the document, and a block's words are read from the block's own source — so a
definition three paragraphs up is not in front of the reader when the sentence is read. The word draws as
itself and nothing is invented. Giving the block reader what the document already worked out is a change to
the seam between the two, and it is the one thing markdown's own constructs do not yet reach.
| `WithTokens` | what a grammar made of a stretch of code, where it has read one *(code fences)* |

**`WithNested` hangs a language, never a picture.** It answers the one question a builder has no business
asking — which of the languages the host assembled reads this — and hangs the answer on the node as a
derived part (`ContentNesting`). It settles one more thing, because the tree is where facts about content
live: **how big the content is set**. A formula on its own line is display maths and drawn half as big again
as the words around it; one in the middle of a sentence is drawn at the size of the sentence. Neither is a
fact about the room it lands in, which is the only size a builder is given.

**The builder keeps every decision that was its.** By the time it sees the node the language is on it, and
it asks for a `ContentInset` at a room *it* chose — so a fence in a narrow table cell and the same fence
across the page are laid out differently, and the walk order, the placement and what has to sit around it
never left the builder. `ContentInset.Set` grafts the child's tree in whole, so a tune inside a document is
still every piece it was drawn as and a drag across the page picks up its bars.

**A whole document is one element, not one per block.** `MarkdownSurface` owns the scroller, what a search
turned up and the buttons a block offers in its corner; `MarkdownElement` owns the tree, the caret and the
selection. Because the prose, the diagrams and the tunes are all pieces of one laid tree, a drag runs from a
word into a chart with nothing forwarding gestures between controls.

**A block written as it was is read as it was.** A document being written is read by a reader kept for it
(`MarkdownParser.Rereading`): `WithBlocks` hands back what it read of every block written exactly as last time beside the
same definitions, because reading a block is a function of those two and nothing else — so a keystroke reads the block it
was typed in. `RereadingTests` holds a reading made that way to the same document read from nothing.

**A block that reads as it did is laid as it was.** `WithUnchanged` is the last stage, and one reads one document for as
long as the host keeps it: it remembers the last reading's blocks and says which of this reading's are the same one —
the same characters and everything the stages before it hung on them, so a paragraph whose link was defined again three
paragraphs away is not the same. What the builder laid for such a block is set down again rather than laid again
(`LaidBlocks`), and only what its pieces stand for is moved along, by the one amount everything after an edit moves: a
part is found again in the new reading by the way down to it (`ContentPart.Order`), and what is kept then names the new
reading's parts, so no reading outlives the one after it. Each
block of the document is a piece of its own at the top of its own frame and keeps the picture it was painted as
(`LayoutKept`), so a keystroke lays and paints the block typed in, and a caret blinking paints nothing. Only the blocks
near the part on screen are painted (`ContentElement.OnScreen`, which the surface sets as it scrolls), and the picture of
a block scrolled far away is let go, so a long document holds about a screen of pictures. What the
characters do not say — which nodes of a diagram are opened — the host says with `IContent.Forget`, after which no
block is the one it was. `LaidBlocksTests` holds every sample, typed into and taken back, to the same source laid from
nothing.

**What a block offers is the language's to say**, asked through `IContentLanguage.Corner`: code offers no
picture of itself, because a picture of code is a worse copy of the code, and prose has no corner at all
(`BlockCorner.None`) — it is read rather than handled, and copying it is what selecting it is for. A block's corner
answers for the whole width of the page from the block's top to its bottom, since the corner stands at the page's edge. What
a reader may do *there* — the things to add, behind one Insert button, and the things to do to what is
already there, standing on their own — is `IContentLanguage.Offers`, asked of whatever language is being
shown at that point. Which language that is was settled by a stage and is on the node, so nothing looks one
up.

**Which block a point is in is asked of the tree that was read**, never of the one that was drawn. A piece
knows the characters it came from but not always as a part of this document's tree — a code fence's runs
carry plain spans, because what drew them was reading code. An offset is an offset whatever drew it.

**Finding a place is three questions with one answer each.** A search reads the **source**, never the
drawing: the parser only ever copies, so the source is the one place the words are whole — in the drawing a
label is broken wherever whatever drew it needed a break, a lyric is split by the notes it is sung on, and a
wrapped word is two runs. The offsets come back out of the source and `LayoutQuery.RangeRects` turns them
into places on the page, which works through every nested language already because each is laid at the
offset its body starts at. Anything **drawn as something other than what was typed** — an entity, an escape,
a renumbered marker, an alert's label — already says so on its run, because that is what makes a caret
possible inside it, so those are found centrally too and nothing had to be told which constructs they are. A
line number is arithmetic on the same source. A language is asked (`IContentLanguage.Finds`) only for what
neither would catch, and nothing in the table has needed it yet.

**A saved reference says what a thing is, not where it sits.** `heading:getting-started/list/item#2` — the
shape a snaplink names a declaration with, because it is the same question asked of a different tree. It
survives editing, reaches `table/row#1/cell#2` and `pie/slice:Chrome` with no code added for either since
the kinds are open strings, and lands as far as it still goes where it no longer goes all the way: a deep
link into a section somebody has reorganised should still land in the section.

**A link into the document is never the host's.** A heading is given the name a link points at while the
whole document is being read — which heading `#notes-1` means is settled by the order they were written in,
and nothing looking at one heading's characters could see it. Following such a link is answered by the
element and not offered onwards, because a host handed `#getting-started` has no way to know what it means
or where it went. A name the document has no heading for is still not the host's: it does nothing, which is
what a reader sees when they follow a link to a section somebody deleted.

**Nothing drawn is never an answer.** A language either lays the content out or says it cannot — with why, as
trouble on a layout that draws nothing — and what goes there then is the characters somebody typed, in a box ruled
in the colour of trouble with the reason written under them: `Unknown barcode format 'NOTAFORMAT'`, and the formats
it could have been. So a block nothing could make sense of is still on the page, still where it was written, still
somewhere the caret can go and repair it, and says what to repair. The seam asks `Laid.Draws`
rather than `Laid.Exists`, because a tree can be built and hold nothing visible, and a caller that took the
one for the other would put an empty box on the page where a block should be. What is written is wrong most
of the time — half a diagram is what every diagram looks like on the way to being one — so this is the
common path and not the corner case. `ContentLanguageDrawingTests` sweeps every language in the table
against both.

**Words are gathered into runs, broken into lines, and joined back up.** A line is set by collecting the
constructs a writer spelled with punctuation into runs, cutting them where a line may break, and joining
everything on one line that is set the same way and stands for the same part — which is how `a **bold**
word` comes out as three pieces rather than eleven. A run whose content is another language is one of those
runs: it is measured by its inset rather than its glyphs, never broken, and sat centred on the middle of the
words, because a formula has no baseline a sentence could share.

**A picture is one mark, and where it came from is the host's.** `WithImages` asks this showing of the
document what an `![alt](where)` names — the host first, then a file beside the document, one chain in
`MarkdownPictures` because both surfaces ask the same question — and hangs the answer on the image. The
builder fits it down to 600 either way and never up, and draws it as a `PictureMark` inside a piece standing
for the characters it was written as, so it sits in the sentence it was written in and a drag across the line
picks it up. An image nothing was found for draws the words written instead of it, which is what alt text is
for.

**Trouble is answered differently in the two places maths is written.** A display formula keeps its
typesetting whatever is wrong with it — maths under a caret is invalid most of the time, since every command
is unreadable until its last letter is typed, so a formula that turned into a box of source as it was written
would spend most of its life as a box of source. An inline one falls back to its own source in a monospaced
accent, because half a display formula still tells a reader where they are and a sentence with a wave through
the middle of it does not.

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

## 2D codes

A parser and four builders, with nothing between them. `MatrixParser` reads a `qr`, `aztec`, `pdf417` or
`datamatrix` body into lines of fields — one parser, because the four share one grammar — and the tree goes
straight to that code's builder. The pipeline is where a tree is changed to say what the text means, and a
symbol has nothing in it anybody edits, so there is nothing for a stage to do.

Each builder reads the fields, encodes, and lays the symbol out as the parts it is made of: a QR code's
finders and timing lines, an Aztec code's bullseye, mode message and reference grid, PDF417's start, row
indicator, codeword and stop columns, Data Matrix's finder and clock on every region. What they share —
the module geometry, the quiet zone, and showing a block that will not read or encode as it is written —
is `MatrixBuilder`. No piece carries a part, because nothing drawn was typed.

## Barcodes

The 2D codes' parser and one stage. A `barcode` body is the same field a line, and `SpellValue` then spells its `value:`
out a piece per character — structure, because a barcode prints its value back a character at a time, and each character
it prints stands for one it was given. Where the block is being written in, `HoldValue` gives a `value:` with nothing
after its colon a piece holding a hole: `WithHoles` puts a hole inside a piece that holds nothing, and a value never
written has no piece to be inside. `BarcodeBlockReader` reads the tree into a `BarcodeBlock`, saying what stops it
against the piece at fault, and `BarcodeBuilder` encodes the value and hands each printed `Character` the piece of the
tree it counts to along the value — never a position of its own working out.

## Mermaid

One parser for every diagram type, because they all open the same way. `MermaidParser` reads what they share: the
front matter between two `---` fences (a `key: value` line as its key, colon and value, anything else held as
written), `%%` comments, `%%{ … }%%` directives over however many lines they take, the header — its keyword and
whatever follows — and the `accTitle` / `accDescr` lines any diagram may carry. Every other line is a statement,
held whole: what its characters mean is its diagram's grammar.

**The header is the first line that says anything.** Front matter, blank lines, comments and directives come
before it; whatever is next names the diagram, and a line there that names nothing is still the header, held with
the reason. Which keyword names which type is `MermaidDiagrams` — most by prefix (`stateDiagram-v2`,
`xychart-beta`), the rest by the whole word, so `flowchart-elk` is not a flowchart.

`MermaidBlock` reads the tree back into what a diagram is handed: its type, the front matter's top-level title as a
part, the YAML between the fences, and where the body starts. `MermaidConfig` reads that YAML as what it is — keys
with values, and sections of more of the same — and each type reads its own options out of it with its own defaults.

**What a type says for itself is its grammar** (`IMermaidGrammar`), which the parser hands the header's arguments and
each line once the keyword is known. A type without one keeps its lines whole. A line is handed over to the end of
its row, space and all, and what the grammar reads is as much as its node prints — the rest is the line's own. Pie has
one: `showData` and a title after the keyword, a `title` line, and a slice per line as its label in quotes, its colon
and its value — a value that is not a number greater than nought keeps its slice and carries the reason on the number,
because the label is what the reader is looking at. A value is written in its own place (`MermaidKinds.Amount`) and one
not yet written is empty and no complaint: it stands after the space left for it, which is where typing it puts it.

**Every diagram on the shared tree is built from the Mermaid kit.** Its grammar reads each line through `MermaidLine`
into the shapes every diagram shares (`MermaidKinds.Name`, `Label`, `Amount`, `Properties`), names the stages it runs
(`IMermaidGrammar.Stages`), and its builder sets words, colours, shapes, connectors, axes and legends through the kit's
own. How a diagram is converted, and what the kit holds, is [mermaid-diagrams.md](mermaid-diagrams.md).

**Where somebody is writing, a pie has holes** (`MermaidParser.Read(…, holes: true)`, the shared `WithHoles` stage where the grammar's `Holds` says): a
label with nothing between its quotes and a value with nothing after its colon each get one, drawn by
`LayoutText.Hole` as a formula draws its own. A chart being written also keeps a legend row for every slice written,
drawn or not — the row with a hole in it is where the reader is typing — and shows the value of any row whose value
is still to come or is wrong, however `showData` is set.

**A pie's colours are written nowhere near its slices**, as `pie1`…`pie12` in the front matter, by position. So which
colour a slice takes is a fact about the order and the front matter rather than about the line, and `ResolveSlices`
works it out and hangs it underneath the slice — naming the key it came from as well as the colour, which is what
lets a restyle know where to write. A slice the config does not colour says nothing, leaving the theme to decide.
`PieChart` reads it all back: the title, the slices in order with their shares, and what the front matter asks for, and
`PieBuilder` draws that on the layout tree: three layers — the wedges, the shares written on them, the legend — each a
subtree of its own, with what belongs together said by the source they all point at rather than by the shape of the
layout. The room the block is given is what the chart is fitted into; where it sits on the page is the document's. On the builder side, `MermaidBuilder` is what every diagram's builder shares: the block read
once, the title — the diagram's own or the front matter's (`MermaidBlock.Title`) — set over a diagram drawn at the origin in the ink its front matter asks for (`TitleColour`), trouble anywhere in the block set beneath, and
the element it is shown in; `MermaidBuilder<TDiagram>` reads the block into its model and draws that. A header naming no type is `UnknownDiagramBuilder` — the block as written, a
wave under the word in the header's place, and the reason.

**Venn has a grammar and two stages.** `VennGrammar` reads a `title`, `set` and `union` lines with their names — bare,
or in quotes — labels in brackets and sizes after a colon, `text` items, and `style` lines setting `fill`, `color`,
`stroke`, `stroke-width` and `fill-opacity`; a `%%` comment may close any line. What a line cannot say for itself is the
pipeline's. `GroupRegions` gathers each set or union with the items indented under it into one `VennKinds.Region`, so
the tree holds what the diagram is made of — regions holding their items — rather than only the lines it was written
on: a comment between items goes with them, and an item written anywhere else stays where it is, because a stage only
re-nests what is already side by side. `ResolveRegions` then hangs a key under each region — a set's name, or a union's
names sorted, so `B,A` and `A,B` are one overlap — under each item standing on its own the region it names or else the
one written last, and under each style what it styles; and says so where a union names a set not written above it, an
item has no region or breaks Mermaid's indentation rule, or a style names nothing. `VennDiagram` reads it back, with
Mermaid's sizes where none is written, and `VennConfig` the front matter.

**The syntax tree and the layout tree are shaped by different things.** A region is one node of the syntax tree and
several pieces of the layout: `VennBuilder` draws three layers — the circles, the overlaps the unions name, and the
words — so a set is a circle in one and a label and its items in another, every one of them pointing at the region it
was written in. The circles are placed by area (`VennLayout`): each circle's area is its set's size and each pair
overlaps by what the two share, the distance for that found by halving and the whole settled greedily, as Mermaid's
venn.js does — a union of three sets or more implying an overlap for each pair it covers, so it has a region to sit in.
Where a size is written for what three sets or more share, the circles are then moved together until every overlap,
pairs and those regions alike, covers as near its size as the rest allow (`VennLayout.Shared`, venn.js's loss).
Words go at the point of their region furthest from any edge. A union's overlap is a piece standing only where its
circles meet and no other covers, and each circle stands in what is left of it, so a press in a lens means the union.

**Radar has a grammar and one stage.** `RadarGrammar` reads a `title`, `axis` lines listing axes — a name, and a label in
brackets after it — `curve` lines listing curves, each a name, a label and its values in braces, and options — `max`,
`min`, `ticks`, `graticule`, `showLegend` — several to a line; each axis and each curve is a piece of its own in its
line's list (`MermaidLine.Names` with an `item`). A value is a number, or the axis it is for, a colon and a number.
`ResolveCurves` hangs under each value the axis it is for — the axis in its place in the order the axes are written,
anywhere in the block, or the axis it names — and says so where a curve does not give each axis one value, names an axis
not written, or mixes the two kinds; braces never closed are read as far as they go, with the reason. `RadarChart` reads
it back, and `RadarBuilder` draws four layers — the graticule's rings, the spokes, the curves and the labels — with the
legend beside or under them; a spoke and its label point at their axis, a curve and its legend row at the curve.

## SMILES

Three actors, each needing the one before:

| Stage | What it works out |
|---|---|
| `ConnectAtoms` | which atom each ring-closure digit reaches |
| `CountHydrogens` | the hydrogens each unbracketed atom carries, and the atoms with more bonds than their element makes |
| `Kekulize` | where each aromatic ring's double bonds go, and the rings that cannot have them |

A ring closure is a bond like any other to the atom it closes on, so hydrogens cannot be counted before
the rings are closed; and an aromatic atom carrying a hydrogen has no bond to give its ring, so the double
bonds cannot be placed before the hydrogens are known.

**Facts name atoms by number.** A bond between two atoms written side by side has no characters, so there
is nothing to hang "this bond is double" under. Atoms are numbered in the order written — the numbering
every SMILES reader uses — and a fact under an atom names its partner by that number: the atom a ring
closure reaches, the atom an aromatic atom shares its double bond with. `Molecule` reads the tree and its
facts back into a graph, and each stage reads the graph the stages before it left.

**Where the atoms go is not the builder's.** `StructureLayout` works out 2D coordinates in this assembly,
with a geometry primitive of its own, so the layout is tested over the corpus without a desktop. A cage is
worked out in three dimensions and seen from its clearest side (`CageLayout`), and the layout says how near
the reader each of its atoms is, so the builder knows which of two crossing bonds to break. The
builder decides how a chemist draws those coordinates, and every atom and written bond it draws carries its
part.

**The oracle is RDKit.** `SmilesCorpusTests` reads SmilesDB's 5,481 molecules beside a reference made by
RDKit — whether it read each one, the hydrogens on every atom, how many atoms its own depiction overlaps —
and holds the stages and the layout to it.

## Word clouds

A parser and a builder with nothing between them — the 2D codes' shape, for the 2D codes' reason.
`WordCloudParser` reads a `wordcloud` body into lines of pairs, one grammar doing two jobs: the settings are
the lines above the words, and the first word closes them. Which a line is, is settled in the parser because
it is settled by what a reader can see — the key, and whether any word has been written yet — rather than by
anything worked out later; the quotes that force a word are held beside it as `Roles.Open` and `Roles.Close`,
so the word drawn has no quotes and the line still prints back as it was written.

**Nothing is hung on the tree, because there is nothing to hang.** A pipeline is where a tree is changed to say
what the text means, and nothing about a cloud's line means anything its own characters do not say. The two
facts that come from outside a line are neither of them the tree's. The size a weight comes to is read off the
block, as a pie's share is read off `PieChart` rather than resolved into it. The colour a word takes belongs to
whoever is drawing and would go stale the moment the theme changed — which is the rule `ResolveSlices` states
for a slice the config does not colour.

**The layout tree is flat on purpose.** A cloud has no grouping in it: every word is placed absolutely, so the
pieces are one run of text per word, directly under the picture. Grouping them by line or by entry would be
grouping by the source's shape rather than the drawing's, and such a group's box would be the union of words
scattered right across the picture — which is the box a press, a marquee and the wash would then be answered
with.

**A shape is two different things.** `WordCloudShapes` is a function of an angle: it pulls the search's rings
in, and the words run out at the outline. `WordCloudStencil` is a grid of cells taken before a word is placed,
which is the only way to pack a cloud into a letter — a letter's edge is not a function of its angle. Both are
in this assembly; what the builder adds is the one thing a stencil needs from a desktop, which is the outline
of a letter or the pixels of a picture.

**Where a word goes is not the builder's.** `WordMask` and `WordCloudBoard` do the packing in this assembly,
against a grid of cells and geometry of their own, so it is tested over the shapes without a desktop. What only
a type engine knows — the shape of the letters, which is what makes a cloud interlock rather than stack — the
builder contributes, filling the outlines it is handed onto that grid. Every word carries the part it was drawn
from and nothing else in the picture carries one, because nothing else in it was typed.

## Correlation plots

Four fences and one grammar. `scatter`, `bubble`, `heatmap` and `density2d` differ in what is drawn
rather than in what is written, which is the division ggplot2 makes — so a bubble plot is a scatter plot
with one more column mapped, not a language of its own.

**The parser decides only the shape of a line.** `PlotParser` splits the block into settings, comments,
blanks, the `data` keyword and rows of cells, and copies. Whether the first row names the columns,
whether the table is a long list of points or a matrix, what a cell reads as and which channel it feeds
are facts about the block as a whole — and every one of them changes as the next line is typed, which is
exactly why none of them is settled while the characters are being read.

| Stage | What it works out |
|---|---|
| `ResolveShape` | which row names the columns, and whether the rows are a long list or a matrix |
| `ResolveColumns` | each column's name and place, and which one each cell stands in |
| `ResolveValues` | the number a cell reads as, where it reads as one |
| `ResolveAesthetics` | which channels each column feeds, or the constant a setting names instead |
| `ResolveCorrelations` | for `geom: corr`, the coefficient between each pair of numeric columns — the one plot whose marks nobody wrote |

**A header is the first row no cell of which reads as a number.** That is the rule a reader already has
in their head and it needs no keyword; `header:` settles only the two cases it cannot reach. **A matrix
is a header with every row one cell wider than it**, the extra leading cell naming the row — which is
how anybody writes a correlation matrix, and reading it needs no setting at all because the shape says
it.

**A setting that names a column is a mapping; anything else is a constant.** `size: pop` and `size: 4`
are the same key, and which one a value is cannot be known from the characters — `4` is a perfectly good
column name — so it is settled once the columns are known, and only a mapping leaves a fact behind. A
column may feed several channels, which is how a chart tells its groups apart in colour and in print at
once; so a cell carries a fact per channel rather than one.

**What is worked out is not what is drawn.** `PlotBins`, `PlotDensity` and `PlotFits` are WPF-free
beside the model — the same division that keeps a molecule's layout and a word cloud's packing out of
their builders — so binning, a kernel density estimate and a regression are all tested without a
desktop, against R's published numbers for `mtcars` rather than against our own opinion of them.

**Two readings of the same rows, and they are not interchangeable.** A fitted line is worked out on the
panel, so it follows a log axis where there is one and can simply be drawn. The coefficients are worked
out from the values, because down the page is the way a screen counts and not the way a number does — a
correlation fitted on the panel comes out with its sign turned about.

**Only what was typed is pressed.** A mark carries the row it was drawn from and a value drawn on its
own tile is the characters it was written with, so a caret stands in it and typing into the picture
edits the block. A tick's number, a coefficient, a gridline, a bin and a contour carry nothing, because
nobody typed any of them — and stepping never stops in one.

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

### Who answers a key

**Editing is shared, and a language may say otherwise for its own source.** From the piece holding the caret, up the
layout to the first piece that names a part of the syntax tree, then up the tree to the first part another language was
written in — which `WithNested` hung there, so nothing is looked up — and that language's `IContentLanguage.OnEdit` is
asked what the key means (`IOnEdit`: what typing, settling and taking back mean, and what an edit came to). Where it
registered nothing, or says nothing, the key does to the characters what a key does. No such part means markdown's own
source, and markdown's rules answer (`MarkdownEdits`, the root's hook in `MarkdownContent`).

A language is told in the document's offsets (`ContentEdit`), because it is laid at the offset its source starts at: the
caret, a hole and a run of words all agree without anything being moved, and `ContentEdit.Local` is there for what reads
the language's own source from the top. That source stops before the line ending its closing delimiter stands after
(`ContentNesting.Own`) — written into, that ending would carry what was typed onto the delimiter's line. A key taking back
characters stops at the edges of it, and takes the whole construct once nothing is left inside.

What each says: LaTeX spells a command as itself and settles it on Space or Enter (`LatexEdits`); Mermaid escapes what a
place cannot hold, starts its next line on Enter and carries a rename to wherever the name is used (`MermaidEdits`);
markdown writes typed markup behind a backslash — the document is written as a word processor's is, and kept as
markdown — continues a list on Enter and joins two paragraphs on backspace. Whatever the edit came to, the whole
document is read again from its source, because a bracket typed anywhere can change how everything after it nests.

**The surface is what a host holds** (`MarkdownSurface`): one control, told whether it is only read and whether it is
one block of a language. It drives the keyboard, keeps the history, raises copying and pasting as events the application
answers once for every window (`MarkdownSurface.Copying`, `Pasting`), and adds cut, copy, paste and markdown's formatting
to the context ribbon. A block's corner offers what its language says (`IContentLanguage.Corner`); Save goes to the host
with the block pressed on, whose `Picture` is painted from the page's own tree.

## What a new language has to bring

1. A parser producing `ContentNode`s, obeying both invariants, and a `Kinds`/`Roles` set of its own.
2. Whatever stages it needs, each obeying the pipeline's rule.
3. A builder that walks a `ContentReading` and emits `ILayoutNode`s, each carrying the `ContentPart` it
   was drawn from — or nothing at all where it was drawn from nothing anybody wrote.
4. Where a key means something in its source other than its characters, an `IOnEdit`, offered as
   `IContentLanguage.OnEdit` and asked only for keys landing in that source.

Everything else — `LayoutQuery`, `ContentSelection`, `CaretPlace`, the caret, choosing, undo and the clipboard — it
gets for nothing.
