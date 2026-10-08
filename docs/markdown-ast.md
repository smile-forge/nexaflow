# The content pipeline

Every piece of content Nexaflow draws — a markdown document, a formula, a diagram, a tune, a barcode — is source text
in some language, and one engine draws all of it. There is no markdown renderer with diagrams bolted on: markdown is
the language you get when nothing names one.

This document is the specification. It states rules, what each rule is for, and what enforces it. Where the code does
not yet meet a rule, that is recorded in [markdown-ast-alignment.md](markdown-ast-alignment.md) with what aligning it
buys — not softened, and not written into this document as though it were the design.

```
source
  │
PARSE       the language's parser     → AST      lossless: Print(Parse(s)) == s, and it only ever copies
  │
NEST        the engine                → AST      every piece written in another language parsed by that
  │                                              language and put in the piece's body, all the way down
STAGES      the language's stages     → AST      IAstStage actors, each handing back the same characters
  │
BUILD       the language's builder    → Laid     a layout tree, every piece saying what it was drawn from
  │
PAINT       ContentElement            → pixels   painting, hit-testing, the caret, selection, editing
```

## Ownership

Each step belongs to one thing, and **only the engine runs them**. The prohibitions are the architecture; the
permissions are unremarkable.

| | Owns | Must never |
|---|---|---|
| A **language** (`ContentLanguage`) | a description: its parser, its stages, its builder, and what editing means in it | run any of them, hold state, or name another language |
| A **parser** | the syntax, in both directions — the only thing that knows how the language is spelled | work out what anything means; synthesise, normalise or repair a character |
| A **stage** | semantics: what the AST means, hung on the AST | change a character; know about another language |
| A **builder** | turning the AST into a layout tree | read source, print the AST, decide anything from an offset, name the language table |
| The **engine** | orchestration: running the pipeline, the lifetime of a render, every cache, all editing state | draw |
| The **element** | painting the layout tree and turning gestures into edits of the source | lay anything out itself |

## 1. The two trees

The AST and the layout tree are different structures with different shapes, and confusing them is the most expensive
mistake available here.

- The **AST** says *what we are talking about*. `src/Nexaflow.Markdown/` — `Ast/` holds the tree and what every
  language shares; each language has a folder of its parser, kinds, stages and model.
- The **layout tree** says *what will be on the screen*. `src/Nexaflow.Visuals.Text/Editing/`, because a mark paints
  onto a WPF `DrawingContext`.

A pie chart shows the difference. Its AST holds data: a list of names, values and percentages. Its layout tree holds a
pie graph containing slices, and a legend containing rows and columns. Nodes in both point back at the same AST nodes,
from different places in a different hierarchy.

**R1 — A builder must not mirror the AST's shape into the layout tree.** Reshaping is the builder's whole job. "Every
AST node got a layout node" is not a sign of a correct builder; it is a sign of one that has not done its work.

**R2 — A layout piece says which AST part it was drawn from, or says nothing.** The layout tree holds visuals the AST
has no concept of — rules, axes, decorations, connectors — which stand for nothing anybody typed, carry no meaning of
their own, and are not selectable. A piece must be allowed to point nowhere; nothing may require otherwise.

**R3 — A layout is made at a standard size, in the content's own units.** Text is measured at `LayoutText.Density` and
the element scales the finished tree as it paints (`Zoom`). The same source and the same room give the same tree on any
display, which is what makes a layout something a test can measure.

**R4 — The only size a builder is told is the room it has.** Where a line breaks is a layout decision; how big the
content is set is a fact about the content. A builder that reads the display, the DPI or a scale factor breaks R3.

## 2. The AST

**The AST owns its text, and every piece of it says where that text was read from** (`ContentNode.Offset`). Only a
parser says it, because a parser is the only thing that reads source; a stage that works something out says nothing,
and what it hangs is derived and stands for no characters at all. Where a part sits in the document is still worked out
by a walk when somebody asks (`ContentReading`, `ContentPart`), which is what positions a language read from its own
slice inside the document holding it.

An offset means **where it was read from**, and nothing else. It is not a print position, not a hint, and not
something an edit updates: an edit names the characters it changes, the engine writes them, and the content is read
again (§8). What it buys is that a stage may regroup, reorder and share the pieces it was handed and the source still
comes out as it was written — and that whether a character was copied or made up is a question with an answer.

**R5 — `Print(Parse(s)) == s` for every input, malformed included.** Nothing a reader can type is outside the AST.

**R6 — A parser only ever copies, and says where from.** Every leaf standing for characters says where it was read
from, and what the source holds there is what it prints as; nothing is synthesised, normalised or inserted.

R6's second half is what makes the first half checkable. A round trip cannot tell a piece that knows its own place
from one that does not, because a tree printed in the order it was built comes out the same either way — so every
language's corpus is put to an oracle that asks each piece where it was read from and holds the answer against the
source, and then reverses the parts of every piece and prints it again (`AstOracle`).

R5 alone is weak: a parser returning the whole input as one leaf passes it, and so does one that quietly repairs what
it read. R6 is what stops recovery from inventing. The temptation on meeting `[CEG` is to close the bracket, and a
parser that does round-trips everything except the half-finished input an editor holds all day. Input a parser cannot
make sense of is held as written, carrying the reason.

**`Kind` and `Role` are open strings.** A shared tree cannot hold an enum of every language's kinds and needs none;
`Kinds` and `Roles` carry only what is genuinely shared.

**Every parser makes the same node.** A parser's `ContentNode` is what every tree shares: what a piece is, its
characters or its parts, and so where it stands in the source.

**R7 — A node type of a language's own is a private contract between that language's stages and that language's
builder.** A stage may put a node of its own type in a node's place (`PieSliceNode : ContentNode`), standing for
exactly the same characters and carrying, typed, whatever was worked out about them. Such a type has exactly one writer
— that language's stages — and exactly one reader — that language's builder. It is `internal` to the assembly its
stages live in, seen only by the builders and the stages' tests. Nothing shared, no other language, not the engine and
not the element may read it.

A node of its own is for something the stages *work out*. A builder reading what was written in the order it was
written needs none. A rewrite keeps a node's type (`ContentNode.Reshaped`), so a later stage, or the engine putting
nested content back, never loses what an earlier one said.

**What stands for no characters at all is a derived part** (`Roles.Derived`): no width, printed as nothing, hung under
the node it is about — a hole, what a macro means, which picture a name resolved to (`ContentNode.Held`, untyped
because what a name resolves to is often something this assembly cannot name).

**What a binding supplied is derived too** (`Roles.Supplied`): a binding standing where content would be
(`Kinds.BoundContent`, a line that is only `{{Path}}`) is held as written, and what it comes to is read into its place
by the language before the stages run — lines of the block's own that nobody wrote here. It is kept apart from
`Derived` because it is read-only in a way worked-out content is not: it is picked out whole, and there is nowhere in
it to put the caret (`ContentPart.Supplied`).

**A derived part stands for no characters, and that is what it costs.** It prints as nothing, no offset finds it, and
it and everything under it begin where the piece they were hung under begins and are no characters long
(`ContentPart`). So nothing derived can be picked out, hit-tested or typed into. A reading of what content *amounts to*
is therefore hung beside what was written rather than put in its place, and whoever draws it resolves each thing it
names back to the written parts, which are what a caret, a selection and an edit all address.

**A language whose subject is not a tree keeps a derived reading of it.** A flowchart is a graph, and a graph does not
fit a tree: one node is named from several places and each mention is its own characters. So the written tree stays as
written, in source order, and beside it a derived reading holds the nodes of the chart, each once, and the connections
between them, each naming its two ends (`FlowchartKinds.Graph`, `ResolveChart`). An entry says which of its mentions
holds the words drawn, so whoever draws it finds the characters without searching for them; and where a node leads is
on the node as a `Kinds.Link` holding a `Roles.Destination`, which is the shape prose writes a link in, so whoever
follows one needs to know nothing about charts.

**R8 — An AST the builder has laid out is finished.** Nothing rewrites it. Editing works from where its parts stand in
the source, writes the source, and the engine reads it again.

## 3. The parser

**R9 — The parser owns the syntax in both directions.** It is the only component that knows how the language is
spelled, so it is the only component that may read source *or* produce source. Reading and writing are two halves of
one job: `Parse` turns characters into an AST, and the write half turns a change to the AST into the characters that
change means.

Nothing outside the parser may compose syntax. An editing handler names an intent — flatten this note, set this
slice's value, make this run bold — and the parser spells it. Escaping, delimiters, quoting and entity codes are the
parser's and appear nowhere else. The write half is `ITranspile`, which a parser implements to be asked about a change
before it is written. `MermaidParser` spells a quote as its entity code and a line break as `<br>` between quotes, a
line break as a space in a title, only a number in a value; `FlowchartParser` spells the same, and a line break inside
a quoted value as the characters that hold one there, and drops from a name whatever a name cannot hold — a space,
unless the name is a subgraph's own, which is written in words. Where the part cannot hold what was asked for, nothing
is written — and a language whose parser implements nothing has nothing written into it, because a key that does
nothing is better than characters spliced into a syntax nothing vouched for.

A language within a kit may own its parser: a flowchart is read by `FlowchartParser` rather than by the shared
`MermaidParser`, reusing its internals for the frame every Mermaid block shares and reading its own statements, because
a flowchart's statement does not end where a line does — a quoted value spans lines and is one run of words.
`MermaidDiagrams.ParserFor` names the parser a diagram is read by and is the only place that choice is made, so the
shipped language and the tests read a block the same way.

**A parser is not a stage.** Where one token stops and the next begins is a fact about the text that no later stage may
change.

**A language may have a second, slower parser.** `Parser` is fast; `SlowParser`, where a language has one, reads the
same characters more fully. Code is the example: read as written at once, and by its grammar (tree-sitter) in the
background, where every stretch becomes a piece of the kind the grammar called it. The engine runs it — see
[§8](#8-the-engine).

## 4. Stages

```csharp
public interface IAstStage { string Name { get; } ContentNode Run(ContentNode tree); }
```

A stage gives the AST semantic understanding. It regroups pieces whose characters sit side by side, inserts parent
nodes, merges neighbours, splits one piece into several, replaces a node with one of its language's own type, or hangs
derived parts underneath. What belongs together but was written apart is gathered under a parent of the language's own
rather than flattened into one node.

**R10 — `stage.Run(t).Print() == t.Print()`.** The characters coming out are the characters that went in.
`AstPipeline` checks this between stages in a Debug build and names the stage that broke it.
`AstRewrite.Regrouping` keeps a node's own derived parts from being swept into a group made of its children, where
they would no longer be true.

**R11 — A stage sees only its own language's nodes.** It works over the characters of any piece in another language as
characters; the piece's own AST goes back into the body once the stages are done.

**Which stages a language runs is chosen per showing.** A language's `Stages` delegate is handed the AST and the
`ContentShowing` — what it is drawn in, whether somebody is writing in it, the stretch shown as typed, where it starts
in the document holding it, and what the host said (pictures, links, what diagrams are bound against). So a diagram's
stages follow the diagram its header names, and a block being written in gets the stage that puts holes where
something is still to be written. Prefer choosing a different set of stages over a stage that checks a condition
inside itself.

**Stage granularity is a judgement, not a rule.** Each stage walks the AST itself, so folding related interpretation
into one stage costs fewer walks, while a stage shared between languages or diagrams costs less to maintain and to
get right. Balance the two on the measured impact of the case in front of you. Neither "one stage per concern" nor
"as few stages as possible" is the rule.

**A slow stage is two halves** (`ISlowStage`) — a spelling checked, say. `Find` is made in the background from the
characters alone; `Apply` hangs what was found on the AST at once. Run by anything but the engine it has found
nothing, and hands the AST back as it was.

## 5. The builder

A builder is made by the engine and by nothing else, always the same way — what was read, what is being written, what
it is drawn in, whether it is read-only, and `Nesting` — and gives back a `Laid` from `Build`. It exposes nothing else.
`Nexaflow.Analyzers.Content` holds that as build errors (see [testing.md](testing.md)); `MermaidDiagramRulesTests`
holds that no builder names the table of languages.

**R12 — A builder never touches the source.** It works in AST parts and layout pieces. It never reads characters,
never prints the AST, and never works out which characters a part came from. Structure it needs belongs in a stage.

**R13 — A builder never decides anything from an offset.** Not where a part starts, not how long it is, not how two
parts compare. If a decision depends on where something sits in the source, the fact it actually needs is semantic and
belongs in a stage, which can say it in the language's own node.

**The room is the builder's to use.** `Within` says how much of the room it is handed it lays out within: a plot keeps
to a panel a reader can take in, a symbol is drawn at the size its modules say, and a score takes four fifths of a page
and sits in the middle of it.

**Where a box badly overstates the drawing, the builder says the shape** (`LayoutBuilder.Occupies`) — a pie's wedges
share a square, so a press must mean the wedge it landed in.

## 6. Content in another language

The parser reading the outer content says only that a piece is written in another language and which: a derived
`Kinds.Language` part holding the word — a fence by the word after it, a formula by being one (nobody writes a
language after `$$`), a diagram label by the word after its own fence. The piece's `Roles.Body` holds its characters.

The **engine** then parses those characters with that language and puts the AST in the piece's body
(`ContentNested.Reading`), and does the same inside that tree, until nothing is left unread. The body becomes a
`Kinds.Nested` node holding that language's AST and, where the characters had one, the line ending that closed them —
which belongs to the line the closing delimiter stands on and is not the other language's (`ContentNested.Own`).
`ContentNested` answers the questions about all of this: which language a node holds, the holders from a part upwards,
what was read in one, and the parts of a tree that are its own language's (`OwnParts` — what a builder reports trouble
from, since a nested language answers for its own).

**R14 — A builder knows nothing about what it nests.** Meeting a piece in another language it calls
`Nested(part, room, style)` — the one thing a builder may ask the engine for. The engine works that piece's AST over
with its own language's stages, lays it out with its own language's builder, at the room and in the style the asking
builder chose, and hands back a `ContentInset`: a layout tree and its size. The asking builder places it wherever its
own layout makes sense, based on where the part sits in the AST it is walking. It never learns what the language was.

So anything can be nested inside anything the engine knows about — **but only where the builder looks for nested
content.** A builder is not obliged to support nesting everywhere, and should not pretend to. For a pie chart, nested
content in the title is worth supporting, and in the legend text; in a value it is not. Supporting a site is a
deliberate decision: the builder asks `Nested` there, and places what comes back.

The room a fence gets in a narrow table cell, the size a formula is set at in a sentence, and where either goes all
stay with the builder drawing the thing that holds them.

## 7. Nothing ever fails to draw

**R15 — The pipeline always produces a layout.** A builder is handed half-typed content all day, and "the content
vanished" is never the way to say so. The seam asks `Laid.Draws` rather than whether a tree exists, because a tree can
hold nothing visible. `ContentLanguageDrawingTests` sweeps every language against content written right and written
wrong.

**The good answer is a diagnosis.** The parser, a stage or the builder notices what is wrong and says something the
reader can act on. The most a builder can say about something it cannot draw is which part it was given and why —
`AsSource(parts, reason)`, or `Diagnostic.Of(part, reason)` for something wrong in what it did draw. Showing content
as written is one helper's (`SourceShown`): it prints the AST back, puts a piece standing for each blamed part over
its characters, and writes each reason beneath.

**The stopgap is the engine's.** The engine wraps parsing, the stages and building in a catch. If anything throws, it
builds an error AST holding the source and the error, and lays that out with an error builder. This exists so that no
throw anywhere in the pipeline can reach the element, and it is a backstop, never a design: a language that relies on
it to report ordinary bad input is wrong, because the reader gets an exception's words instead of an explanation.

**A failure costs the part that failed.** Nested content is caught where it is nested, not where the throw is noticed.
Reading content in another language happens while the builder that asked for it is part-way through laying its own, so a
throw let out would be blamed on the only tree that builder has — all of it — and one bad fence would show a whole
document as written. The catch belongs at the block, which then shows as written with why inside a document that still
draws as a document.

So there are three answers, all a `Laid`:

1. **The reading makes no sense** — it is not the language: shown as written, the error marked.
2. **Something is missing** — not yet written: a hole where it goes, while the content is being written only.
3. **A rule of the language is broken** — it read, but means something that cannot be: drawn with a wave under the
   part where the content is being written and that part is drawn as words the reader types into; otherwise shown as
   written, the part marked.

**Which answer a language gives turns on one question: is this content typed into where it is drawn?** A diagram that
draws nothing asks to be shown as written, blaming what it could make nothing of. A formula keeps its typesetting
while it is written, and something it read but cannot draw is a warning in place. A tune is typed into where it is drawn — its title and
the words under its staff as text, its notes as notes — so something it read but cannot draw is marked in place. A
structure, a 2D code, a plot and a word cloud are never typed into where they are drawn, so anything wrong shows them
as written. A barcode's
value is typed into where it is printed, so a value that will not encode keeps a faint symbol with a wave under it
while it is written.

Where a language lays nothing out, what goes there is the characters typed, in a box ruled in the colour of trouble,
with the reason under them — still where it was written and still somewhere the caret can go.

## 8. The engine

`ContentEngine` is the orchestrator. It runs the pipeline, owns the lifetime of a render, holds every cache, and
manages editing — the caret, the selection, what can be taken back, the context menu, the buttons in a block's corner.
Nothing else holds state, and nothing else caches.

It lays content out in four steps, and nothing else does:

1. **Parse** the content with its language — markdown where nothing names one.
2. **Nest.** Wherever the parser named another language (`Kinds.Language`), parse the piece's own characters with that
   language and put the AST in the piece's body, then the same inside that tree, until nothing is left unread.
3. **Stages.** Run the language's stages.
4. **Build.** Make the builder the language names — from the worked-over reading, what is being written, what it is
   drawn in, whether it is only looked at, and `Nesting` — and lay it out at the room it was given.

`ContentEngine.Read` does the first three steps and lays nothing out, for whatever wants to know what content says
rather than to draw it.

**One engine per showing of some content**, because what it keeps is that content's: the parse of a document read again
as it is written, what every nested piece read to (a keystroke in a paragraph does not read the diagram under it
again), the blocks that read as they did last time, and what the reader has opened in each diagram. A surface keeps its
engine for as long as it shows content, so none of that is lost when a change of colours or of who may write makes its
element again.

**What the host says that the source does not** — where pictures are found, how links look, what diagrams are bound
against — is the engine's `Inputs` (`ContentInputs`), and what a language's stages read. Setting them, or `Forget`,
says that nothing laid before is to be set down again as it was.

**What is slow is never waited on** (`ContentEngine.Slow`). A `SlowParser` and an `ISlowStage` are the engine's to run,
never a language's. The content is laid without them first; the engine starts the work, keeps what it comes to by the
language, the stage and the characters — a few hundred deep, for every engine, so content shown again is shown as it
was left — and says so when it lands (`Reread`). Whatever shows the content moves to its own thread and lays it again.

## 9. Editing

Everything the reader does reaches the engine as one `ContentInput` (`ContentEngine.Input`): a key, text typed, a
press, a drag, a release. A window's keys and a test's are the same input by then, so what one does the other does. The
element only turns the pointer's pixels into the content's units and says when a move has gone far enough to be a drag.
No key is the surface's: the clipboard's and the page's go to the engine like every other, and where a page key means
nothing to the content the engine asks back for a page, which is as tall as whatever shows it.

**Who answers a key: two walks, and nothing is tracked.** From the piece the caret stands against, up the layout tree
to the first piece drawn from a part of an AST (through anything standing for one — `IStandsFor`); from that part, up
its own AST to the root, whose `BlockNode.Language` names the language (`ContentLanguages.WrittenIn`). Every AST is its
own and only the layout is one tree, so nothing has to be remembered to know which language a key is in.

The engine hands that language's `Editing.OnEdit` a `ContentEdit` — what was done (`EditKind`: typing, pasting,
settling, breaking on Shift+Enter, erasing, deleting, tabbing either way, inserting, choosing on a ribbon, dropping
what is picked out), the text, the piece, the part and the root — and the handler answers with a `ContentChange`: the
stretches to write over, where the caret goes, and what is shown as written. The engine makes it and lays the content
out again (`ContentEngine.Edited`). A null answer leaves the key to do what it does anywhere; a key taking back
characters still stops at the edges of content in another language, and takes the whole construct once nothing is left
inside.

**A handler names what the reader meant; the parser spells it.** Where a change is about meaning rather than
characters — a paste into a label — the handler names the part and the words (`ContentWrite.Words`) and the parser
makes them safe for that part (§3). A handler that concatenates syntax is doing the parser's job in the wrong place.

**Whatever the edit came to, the whole content is read again from its source.** An edit to the AST is provisional — the
stages do not re-derive themselves underneath it — so it prints, and what it prints is parsed and laid again. An edit
against a part knows what it touched, so trouble afterwards is blamed on the keystroke that caused it.

Offsets are the document's, because a language is laid at the offset its source starts at. Space and Enter both arrive
as settling, so a formula settles what is half-written where a document starts its next paragraph. Markdown writes
typed markup behind a backslash, continues a list on Enter and joins two paragraphs on backspace (`MarkdownEdits`);
LaTeX spells a command as itself and settles it on Space or Enter (`LatexEdits`); every diagram has a handler of its
own (`DiagramEdits`), which escapes what a place cannot hold as it is typed and leaves every other key to do what it
does anywhere.

**A right-click asks the same language.** The engine walks from the piece under the pointer as it does for a key and
asks that language what it offers there (`ContentEngine.Asked`, answered by `Editing.Offers` with the piece and the
root); the options of one choice come back under one `LayoutIntent.Group`, the one in force marked `Current`, and
`DiagramRibbon` draws them side by side, each as what it would make (`LayoutIntent.Shape`) and named in its tooltip. A
press on one comes back through the engine as a choosing edit on the same piece and part (`ContentEngine.Choose`), so
what a ribbon does is written in the handler that answers the keys.

Markdown's own offer is Insert: every language that names a block to start from (`ContentLanguage.DisplayName`, `Icon`
— a Fluent UI System Icons name — and `DefaultBlock`, gathered by `ContentLanguages.Insertable`) sits behind the
ribbon's one Insert button, which opens a sub-ribbon of their icons; choosing one writes its block after the block the
ribbon was opened over, the caret on its last line (`MarkdownEdits`). The surface adds nothing to the ribbon of its
own. Paste is the engine's: it asks whatever shows the content for what is on the clipboard, since a clipboard is the
application's, and hands the words to the language the caret is in as a pasting edit. What a copy holds is the engine's
too — only it knows what is picked out and what language that was written in, so a whole block copied carries the picture
it draws where its language says one is worth keeping, whether it was Ctrl+C or the block's own corner that asked.
While whole pieces are chosen — a slice, a node — there is no caret (`ContentEngine.ChoseWhole`).

**A move is its own seam.** Carrying what is picked out and letting it go somewhere else is the engine's gesture — the
press, the drag, the content laid out as it would read after the drop, and letting go settling exactly what was on
screen (`ContentEngine.Moving`, `BuildPreview`, `Release`). What it comes to by default is the characters carried,
emptied from where they were and written in at the drop, which is right for everything whose source is what a reader
sees. A language says otherwise through **`IOnMove`** (`IContentLanguage.OnMove`), told what is carried and where it is
being let go (`ContentMove`) rather than left to work either out from the selection — because the case that needs it is
the one where the stretches carried are not next to one another and what they stand for is a place in a structure: a
slice of a pie, a column of a matrix, a note of a tune. It answers with a `ContentChange` like every other edit, so
there is still one path from a gesture to the source. Text arriving from outside is an edit rather than a move, and
stays one (`EditKind.Dropping`, `ContentEngine.Brought`).

**The rest is shared and comes with the element.** Shift chooses from where the choosing started and Ctrl adds or takes
back what is pressed (`ContentElement.BeginPointerSelect`); up and down go to the nearest line with somewhere to stand,
at the place nearest the column (`LayoutQuery.StepVertical`); undo takes back a stretch of writing, each step the whole
document as it stood (`EditHistory`); the pointer is a bar only over what can be written in
(`LayoutQuery.Writable`). A block's corner offers what its language says (`Editing.Corner`, `Editing.Offers`): code no
picture of itself, prose no corner at all. A language that implements any of these itself is off-spec.

## 10. The element and the surface

`MarkdownSurface` is the one control a page hosts, as many times as it shows content: it owns the scroller, the focus,
and what a search turned up, and answers what the engine asks of a control — a copy to be put on the clipboard, what is
on one, a picture of a block, a page. Inside it one element (`MarkdownElement`, a `ContentElement`) shows what its
engine holds.

The engine keeps the content as it is being written — the source, the caret, the selection, what was written so it can
be taken back, and the laid tree — and makes every edit to it. It also lays out the buttons in the corner of the block
the pointer is over, since only it knows both where the block came out and what its language offers
(`ContentEngine.Corner`), and a press on one is a press like any other. The element paints all that and says where the
reader is.

What happened is the engine's to say, and the surface says it again to the page as routed events: `SourceChanged` (what
changed, and whether it was written, taken back or put there), `Selected` (what is picked out — words, a note, a box in
a flowchart, each with the language it is written in and the id the drawing gives it), `PreRender` (laid out, not yet
shown) and `LinkNavigate` (a link out of the content, handled where the page took it).

**A whole document is one element** — the prose, the diagrams and the tunes are pieces of one layout tree, so a drag
runs from a word into a chart with nothing forwarding gestures between controls.

**A run of text is one piece with a place between any two of its letters** (`LayoutWords`): it answers which letter a
press landed on, where the caret stands and what a selection covers, from the text it was set with. A run says whether
what is drawn is what was written, which is what makes a caret possible in it, and whether pressing it shows what was
written — a number set to two places, a title without its quotes. A run saying something *about* the source without
showing it — the share a slice takes — is neither, and stepping never stops in it.

**A block keeps the picture it was painted as** (`LayoutKept`), so a keystroke paints the block typed in and a caret
blinking paints nothing. Only the blocks near the screen are painted (`ContentElement.OnScreen`), and the picture of a
block scrolled far away is let go.

## 11. Speed

**Nothing is incremental.** An edit can reshape everything after it — a closing brace, a bar line — so the whole
content is parsed and worked over again: one path, always taken, therefore always right.

What is saved is reuse, and it is keyed on identity, never on a guess about what an edit touched:

- **A block written as it was is read as it was.** The engine keeps a parse per document (`MarkdownParser.Parsing`)
  that hands back every block written exactly as last time beside the same definitions as the very block it handed out
  then, finished, so a keystroke reads and walks only the block it was typed in. `RereadingTests` holds such a reading
  to the same document read from nothing.
- **A block that reads as it did is laid as it was.** `WithUnchanged` says which of this reading's blocks are one the
  last reading had — the same characters *and* everything worked out about them, so a paragraph whose link was defined
  again elsewhere is not the same. What the builder laid for such a block (`LaidBlocks`, by where the block comes among
  the document's parts) is set down again, only what its pieces stand for moved along by the amount the edit moved
  everything after it. A block shown as written is never kept. What a block means on the day it is read — a Gantt
  chart's today — is part of its reading, so it is laid again when it is read again. `LaidBlocksTests` holds every
  sample, typed into and taken back, to the same source laid from nothing.
- **Slow work is never waited on** — see [§8](#8-the-engine).

## 12. Adding a language

1. A parser producing `ContentNode`s under R5 and R6, with kinds of its own, naming the language of anything written
   in another one — and spelling, for the write half, whatever an editing handler will ask it to write.
2. The stages it needs, each keeping the characters it was handed (R10), each saying what it worked out in the
   language's own nodes where that is what it is for (R7).
3. A builder that walks a `ContentReading` and lays out a layout tree of its own shape (R1), every piece carrying the
   part it was drawn from or nothing (R2), using no offsets (R13), asking `Nested` at the sites where it chooses to
   support nested content (R14).
4. A `ContentLanguage` describing the three, in the `ContentLanguages` table — and, where a key means something other
   than its characters, an `IOnEdit` offered through its `Editing` (`EditedBy`).

The caret, selection, choosing, undo, the clipboard, search and painting come with the element.

**A language is the unit, aggressively so.** Languages that share a parser, stages or a kit of drawing are still a
language each. Every Mermaid diagram is one, answering to the words its header is written with, and told apart by its
builder. So "where does the special case for diagram X go?" has one answer: X is a language.

`mermaid` is a language too, and the smallest: its parser reads no further than the header and holds the whole block as
written in the language the header names (`MermaidFenceParser`), and its builder hands back what that lays out as its
own, so nothing of it is drawn, found or picked out. A header naming no diagram is its parse error, marked on the
keyword.

The ones that ship are `Languages/Shipped`; `ContentLanguages` is the one table of them, and markdown is what content
is written in where nothing names a language.

## The languages

### Markdown

**A document is a list of blocks, and each block is its own content.** `MarkdownParser` reads in two passes: where each
block starts and which it is, and which language every fence and formula is written in (Markdig decides the boundaries
and nothing else); then each block in turn (`MarkdownBlocks`) — what it holds, by the reader for its kind
(`MarkdownInline`, `MarkdownList`, `MarkdownTable`, the block reader again for a quote's body), with every piece
finished as it is read: what pieces side by side make together (`MarkdownGroups` — a definition list's pairs, an
alert's marker, the display formula a paragraph of nothing but `$$ … $$` is) and the line ending closing a block's last
line (`MarkdownClosingLines`). A kind nothing reads is shown exactly as typed. A reader also records what the
characters do not say — a column's alignment, the squares a cell covers, which alphabet a list counts in — as derived
parts.

**A fence is a delimiter, another language, and a delimiter**, and so is a formula: `$$ … $$` is the same shape with the
language implied, and `$x$` is that shape small, framed by words instead of by lines.

| Stage | What it works out |
|---|---|
| `WithImages` | the picture an `![alt](where)` names, where this showing can find one (`MarkdownPictures`) |
| `WithLinks` | how this showing wants each link to look |
| `WithUnchanged` | which blocks read exactly as they did last time |
| `ShowBlocksAsWritten` | the block somebody is changing the markup of, as the characters it is written with |

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
starts at. What is drawn as other than what was typed — an entity, a renumbered marker — says so on its run and is
found the same way. **A saved reference says what a thing is, not where it sits**
(`heading:getting-started/list/item#2`), and lands as far as it still goes. **A link into the document is the
element's**: which heading `#notes-1` means is settled by the order the headings were written in, so following one is
never handed to the host.

### LaTeX

Its own document: [latex-parse-tree.md](latex-parse-tree.md). Its parser hangs what each shorthand name stands for
beneath it as it reads (`TexMacros`); its stages gather a sign written as several things (`GatherSigns`), read the
values written with a command (`SpanColumns`, `ReadValues`) and then say what every name means (`ResolveCommands`:
`TexCommandNode`, `TexGridNode`, `TexCharNode`), so `LatexBuilder` sets a formula with `TexTypesetter` from meanings
alone.

### Mermaid

One parser for every diagram, because they all open the same way: front matter, comments, directives, the header (the
first line that says anything, naming the diagram — `MermaidDiagrams`), accessibility lines; every other line a
statement its diagram's grammar reads (`IMermaidGrammar`). The front matter nests: each line holds, after its own
ending, the lines indented under it, so `config:` holds `pie:`, which holds `legendPosition:`, and a handler writing an
option walks down to its node rather than reading lines. The grammar names the diagram's stages, and every diagram's
grammar and builder are built from the kit. How a diagram is added and what the kit holds:
[mermaid-diagrams.md](mermaid-diagrams.md).

### ABC

The order of its stages is the argument:

| Stage | What it works out |
|---|---|
| `ResolveFields` | what the words of each field's value say for its letter: a key and clef, a meter and its sign, a unit length, a voice |
| `ResolveContext` | the key, meter, unit note length and voice in force on each line |
| `GroupTuplets` | a `(3` marker and the events it covers |
| `GroupBeams` | the events written together with no space between them |
| `GroupBars` | what is between two bar lines, and whether the line closing it is a repeat line |
| `ResolveNotes` | what each note sounds, how long each event lasts, and which rest a rest is |
| `AlignLyrics` | which syllable is sung on which note, and where it was written |
| `ResolveMarks` | the mark each decoration names, however it is spelled, and whether a quoted run is a chord or placed text |
| `CheckDrawable` | what is written correctly and still cannot be drawn: a decoration naming no mark |
| `ShowAsWritten` | the stretch under the caret, shown as typed |

**What is written is split as far as it goes**, so no stage takes characters apart:

- a `K:`, `M:`, `L:` or `V:` value into its words — a key (its tonic, the sharps or flats on it, the mode written
  straight after), figures (numbers and the marks between them), `key=value` settings (a quoted one whole, spaces and
  all) and words standing for themselves; every other field is held whole as the prose it is;
- a length suffix into its numbers and its slashes;
- a tuplet marker into its numbers, each in the role its place gives it, so `(3::2` has no `q` rather than an empty one;
- a `!…!` decoration into its bangs and its name — a one-character shorthand has nothing inside it and stays one leaf;
- a quoted run into its quotes, the character that places it, and its words;
- a syllable holding a `~` or a `\-` into its words, the join and the escape.

A key is a capital `A`–`G`, so `K:bass` names a clef and leaves the key alone, and a mode is its own word, so what
follows it — `K:Dm clef=bass` — is not part of it. What is left for a stage to read off characters is a mark written
once or repeated — `^^`, `,,`, `>>`, a rest's letter — where what it means is which mark and how many.

A length needs the unit note length; a tuplet's default needs the meter; a tuplet beams as one group; a beam never
crosses a bar line; an accidental lasts a bar. A measure never crosses a source line, because a line ending is a
suggested system break. **A note is four parts** — accidental, letter, octave marks, length — so each gesture rewrites
one: `A`–`G` writes a note in the octave of the one before, Page Up and Down move the octave, `+` and `-` double and
halve the length, and `#` and `_` move a semitone **from what the note sounds**, so flattening a bare `F` in G major
writes `=F`. Each stage says its answer in the tune's own nodes (`AbcFieldNode`, `AbcLineNode`, `AbcTupletNode`,
`AbcBarlineNode`, `AbcEventNode`), and a mark or words written against a note in the ones every notation shares
(`MusicMarkNode`, `MusicAnnotationNode`), so `AbcBuilder` only walks the tune.

### LilyPond

The same engraving, walked by a builder of its own: the two notations think about music differently, and share
`MusicBuilder` for what a score is drawn with. What is true of a note or a command wherever it is played is its stages'
— how long it is written and lasts (`ResolveDurations`), what it sounds (`ResolvePitches`), what a command's arguments
set (`ResolveCommands`), what a mark or a script's words are (`ResolveMarks`), how a chord's name is spelled
(`SpellChords`) — said in `LilyPondEventNode`, `LilyPondCommandNode` and the shared mark and word nodes. Where the bars
fall, how notes beam and which accidentals print depend on where a note is played, since a definition is played
wherever it is used; so `LilyPondBuilder` walks the music through to find them.

### 2D codes

`MatrixParser` reads a `qr`, `aztec`, `pdf417` or `datamatrix` body into lines of fields, one grammar for all four.
Each code's own stage (`EncodeQr`, `EncodeAztec`, `EncodeDataMatrix`, `EncodePdf417`) reads its fields, encodes them
and names the parts the symbol is made of (finders, timing lines, a bullseye, row indicators), leaving on the block
only what a picture needs (`MatrixSymbolNode`) — or the part at fault saying why. So one builder, `MatrixBuilder`, lays
out all four. No piece carries a part, because nothing drawn was typed.

### Barcodes

The same grammar, the value spelled out a piece per character by the parser, because each character printed stands for
one written; while the block is written in, `HoldValue` gives a value not yet written a hole. `EncodeBarcode` reads the
block, encodes the value and hangs what is drawn under it (`BarcodeBlockNode`): the bars (`BarcodeBarsNode`) and each
run printed with them (`BarcodeRunNode`), where every printed character that is a character of the value stands for
that character as written (`BarcodePrintedNode`) and what the format worked out stands for nothing. Only those
characters give the builder a part to put on what it lays, so the caret stops only where what is printed was typed.

### SMILES

`ConnectAtoms`, `CountHydrogens`, `Kekulize`, `DepictStructure`, each needing the one before, and each saying what it
works out in the molecule's own nodes (`MoleculeNode`, `AtomNode`). A bond between two atoms written side by side has no
characters, so a molecule's bonds name atoms by the order written. Where the atoms go is a stage's answer too
(`DepictStructure`, through `StructureLayout` and `CageLayout`): it is a fact about the molecule, not the room, so the
builder only scales it to its room and draws it.

### Word clouds

The parser decides whether a line is a setting or a word, since the settings are the lines above the words;
`ResolveCloud` says what the settings and colours come to (`WordCloudBlockNode`), marking a value a setting cannot
take, and `ResolveWords` says each word's weight, the size that sets it at and when it is packed
(`WordCloudWordNode`), on the word as written, marking a weight that will not read. A `mask:` picture is found by the
host (`WithPictures`). The layout tree is flat on purpose — every word placed absolutely, one run each — and the
packing (`WordMask`, `WordCloudBoard`) is the builder's to ask for, because it needs what only a type engine knows: the
outlines of the letters.

### Correlation plots

`scatter`, `bubble`, `heatmap` and `density2d` differ in what is drawn, not in what is written. The parser decides only
the shape of a line; the settings (`ResolveSettings`), which row names the columns (`ResolveShape`), each column
(`ResolveColumns`), what a cell reads as (`ResolveValues`), which channel a column feeds (`ResolveAesthetics`), for
`geom: corr`, the coefficients (`ResolveCorrelations`) and what a `stats:` line reports (`ResolveStatistics`) are
stages, because every one of them changes as the next line is typed; each is said in the block's own nodes
(`PlotBlockNode`, `PlotRowNode`, `PlotCellNode`). Binning, densities and fits are the builder's to ask for, because
they are worked out on the panel it lays out, and are tested against R's published numbers.

**The line between a stage and the builder is this**: a stage works out facts about the content, true wherever it is
drawn; the builder works out what depends on the room it was given, or on something only a type engine knows. So a
molecule's atom positions are a stage's answer and a plot's bins are the builder's.

## The oracles

A construct list says what is supported (`AbcConstructs` and its peers in `Nexaflow.Tests.Fixtures`), and a corpus says
whether it holds up on input nobody here wrote: ten thousand tunes (`NEXAFLOW_ABC_CORPUS`), SmilesDB against RDKit
(`NEXAFLOW_SMILES_CORPUS`), LaTeX against its own renderings. A corpus is read as bytes and never re-encoded — mojibake
included, because R6 has to survive it.

`AbcPictureSweepTests` holds our page against the engraving each tune ships with, and the number means something only
because of how it is measured: every page is also scored against a *different* tune's picture, since two pages of music
share most of their ink by being music; ours is given the reference's width and drawn at the staff size measured off
the reference's own empty stave; both are cropped to their ink; detail is compared at the scale where two engravers
still agree; and results are bucketed by how much music there is, because short pages all look alike.

Refactoring guards are opt-in and run beside any change to how content is reached: `LayoutSnapshotTests`
(`NEXAFLOW_LAYOUT_SNAPSHOTS`) holds every piece of every sample to a recorded layout, and `DiagramSnapshotTests`
(`NEXAFLOW_DIAGRAM_SNAPSHOTS`) every diagram to a recorded picture.

## Where the code is not yet the spec

Where the code stands against each rule, what aligning it buys, and the order the work is taken in:
[markdown-ast-alignment.md](markdown-ast-alignment.md). Nothing there licenses a divergence this document does not
already name.
