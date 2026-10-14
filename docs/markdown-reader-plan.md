# Reading markdown ourselves

Markdown's own syntax is read through Markdig, and Markdig answers a different question from ours. This is the design
of the reader that replaces it in the app, and the order the work is taken in. Each phase is finished — its gate met —
before the next starts. When the last one lands, the design half of this file moves into
[markdown-ast.md](markdown-ast.md#markdown) and the rest goes.

## Why

Markdig turns markdown into HTML. Its tree is built for that: entities decoded, escapes taken off, indentation dropped,
link definitions gathered out of where they were written, every name resolved against the whole document. The parser
here has to copy every character and say where it came from (R5, R6), and reads each block as a function of its own
characters so a keystroke re-reads one block (§11). So of what Markdig hands back, the reading keeps **spans and type
tags** and a handful of facts — a heading's rank, a fence's character, an emphasis delimiter and count, a list's
alphabet and start, a box ticked, a column's alignment, a cell's spans, a hard break, a formula's content span.

Everything else is discarded or worked against:

- **Its text.** None is used. `Cut` exists to turn Markdig's spans back into source, including through the prefixes a
  container puts on every line (`Cut.Within`, `Original`, and the `lead` every container reader is handed).
- **Its resolution.** Reading blocks alone defeats Markdig's reference links and abbreviations, so `WithReferences`
  rebuilds `[text][name]` from what Markdig left of brackets it could not close, and resolves abbreviations;
  `WithTargets` re-reads definitions out of the trivia with Markdig's `LinkReferenceDefinition.TryParse`, and names
  headings itself.
- **Its spans, in places.** A heading's stops short of its newline and an unclosed fence's is four characters
  (`Cut.Closes`); a paragraph's reaches over a pipe table written under it (`One`'s ceiling); an alert's `[!NOTE]` is
  inside its paragraph's (`Called`); a cell can have none (`MarkdownTable.Where`); a definition item and a footer read as
  themselves when read alone (`Circles`); a table in a quote is read through a regex that blanks the quote's marks
  (`Unquoted`); definitions are moved into a group written nowhere (`LinkReferenceDefinitionGroup` filtering). Trivia
  tracking is off because it turns a pipe table back into a paragraph.
- **Its failures.** Every `Parse` is caught, and every reading is put to `Checked`, which shows the source as written
  when the tree does not print back.

And it is paid for. Each `Parse` runs the whole pipeline, sixteen extensions and the full inline pass, even where only
block spans are wanted — so a paragraph in an item in a quote is parsed four times (the document, the quote, the list,
the paragraph), each building and dropping a Markdig tree. `Windowed` doubles a stretch and parses it again until it
holds two blocks.

What Markdig genuinely gives is CommonMark's corner cases — when a container continues, laziness, tabs, the seven HTML
block kinds, setext headings, the emphasis rules, nested brackets, backtick runs — and the grammars of the extensions
enabled. That is a real body of knowledge with a written specification and a few thousand worked examples. Recognising
it is all that is needed here: no HTML, no normalisation, no document-wide resolution. So it is worth writing once more,
provided it is held to those examples from its first commit. That is phase 1.

## What does not change

The reader replaces how blocks and words are recognised. The tree it hands back is the tree handed back now, which is
what lets each phase be proved by comparing trees rather than by reviewing behaviour.

- **The same tree.** The same `MarkdownKinds`, roles and derived parts. Builders, stages and the editing handlers are
  untouched. Where a piece starts and ends follows the characters rather than Markdig's spans, which differ only where
  those spans were arbitrary.
- **The same two passes.** The split names each block and leaves its body verbatim; `MarkdownBlocks` reads each body
  with the reader for its kind and keeps it by `(lead, written)`. A block's reading is a function of its characters and
  what stands in front of its first line, and nothing else (`RereadingTests`). Nothing document-wide is resolved by the
  parser: what a name means is the stages' (`WithTargets`, `WithReferences`).
- **The write half.** `MarkdownParser.Rewrite` is already ours.
- **`Checked` stays as the last guard**, and the harness counts every time it fires. A reader that relies on it is wrong.

## The design

Three pieces, all in `Nexaflow.Markdown/Prose`, WPF-free, allocation-aware, and each emitting `ContentNode`s directly
— there is no intermediate tree.

### `MarkdownLines` — a block's lines inside its containers

The one primitive the others share. A block's content is a list of line ranges in the document, each with what its
containers put in front of it — a quote's `>`, an item's indentation — **recorded, not stripped**. What a reader reads is
the ranges; what falls between them is the trivia it already is, at the offsets it already has. A tab only partly taken
by a container stays the block's, as CommonMark's virtual columns say, so its character is never split.

This replaces `Cut.Within`, `Original`, `Contained` and the string rewriting behind them: nothing is copied into a
second string, so nothing has to be mapped back.

### `MarkdownScanner` — blocks

CommonMark's block algorithm: one pass over lines with a stack of open blocks. Each line first continues the open
containers it can, then tries every block start, then goes to the innermost open leaf or lazily continues a paragraph.
A block, once closed, is never opened again.

**Each block kind is a rule in a table** — how it starts, how it continues, what closing it does — so an extension is a
rule added to the table, and which rules are in it is the reader's options. Closing is where a paragraph can turn out
to be something else: a setext heading, link definitions, a pipe table's head, a definition list's term. The extensions
enabled today, each with the specification it answers to:

| Rule | Kind | Specification |
|---|---|---|
| quote, list, item, heading (ATX, setext), fence, indented code, HTML (7 kinds), rule, paragraph, link definition | block | CommonMark |
| front matter (first line only) | block | Markdig's YAML specs |
| `$$` block | block (a fence whose language is implied) | Markdig's maths specs |
| alert (`> [!NOTE]`) | quote variant | Markdig's alert specs |
| pipe table, grid table | block | Markdig's table specs |
| definition list, figure, footer | block | Markdig's definition-list and figure/footer specs |
| task box, lettered and roman lists | item facts | Markdig's task-list and list-extra specs |

`UseDiagrams` and `UseAutoIdentifiers` affect only HTML output and are not rules. Footnotes are not enabled today and
are not added by this work.

**Reading inside a container is the same scan, started inside it.** `MarkdownParser.Inside`, `MarkdownList.Read` and
`MarkdownTable.Read` are handed the block and its lead, as now; the scanner opens the containers the lead names and
reads on from there. The scan is linear and does no inline work, so reading a container's body again costs little —
and it keeps the contract above exactly, which is what makes phase 2 provable by tree equality. Handing the nested
structure from the first scan straight to the readers is the obvious next saving, and it is taken only if the timing
in phase 1 says the rescan matters.

**A stretch stops exactly.** Because a closed block is final, `Windowed` stops at the first line past its soft length
where a top-level block starts with nothing else open — once, with no doubling and no reading again. Front matter is a
rule, so `FrontMatterEnd` goes.

### The inline reader — words

CommonMark's inline algorithm, one pass left to right over a leaf's `MarkdownLines`: code spans by backtick run,
autolinks, bare addresses (Markdig's autolink extension), raw HTML, entities, escapes, breaks; delimiter runs onto a
delimiter stack (`*` `_`, and the extras `~` `^` `=` `+` and the citation's `""`); brackets onto a bracket stack, closed
by an inline destination or a reference label;
`$…$` by Markdig's maths rules. Escapes come out as their own pieces as they are read, so `Escaped`'s second pass goes.
`MarkdownInline` keeps its name and its contract; the scan behind it is ours.

**Links are made in two places, from one list.** Some brackets are settled by what follows them: `[words](where)` is a
link whatever the document defines, and so is an address in angle brackets or written bare — the reader makes those as
it reads. Others wait on a definition that can be written anywhere in the document — `[words][name]`, `[name][]`,
`[name]` — and a block is read from its own characters alone, so the reader cannot settle them. It neither guesses nor
drops them. A run holding such a bracket keeps the list the reader resolved it from — its text, its delimiter runs with
what they can open and close, its brackets — as a derived part, and is read as CommonMark reads it where nothing is
defined. `WithReferences` takes that same list and runs the resolving half of the algorithm over it again with the
document's labels known: a bracket whose label is defined makes its link, the brackets before it can no longer open one,
and the delimiters are matched again round the links made. One implementation of the list and its resolution
(`MarkdownDelimiters`) is what the reader and the stage both run, so a link by name is read exactly as CommonMark reads
it — `*[a*][ref]` included — and never by a second bracket matcher written beside the first.

A run with no list, or whose labels nothing defines, is left exactly as the reader handed it — most runs, at no cost. The
list names pieces by their place in the run, never by offset, so a block kept from the last reading and moved along
(`FurtherOn`) holds a list that is still true. `Atoms`, `Bracketed`, `Leftover`, `Joined` and the cutting of words at
brackets go: nothing has to be recovered from a tree that is told it outright.

### Export — one reading, written out in any format

Copying as HTML, the published site, and later Word and ODF are one job: the reading written out in another format. So
there is one walk and one writer per format, and a new format is a writer and nothing else.

- **The walk** (`MarkdownExport`) turns the reading, worked over by its stages, into a format-neutral document
  (`ExportDocument`): headings with their rank and anchor, paragraphs, lists with their numbering, items with their box,
  quotes and alerts, tables with their alignment and spans, definition lists, figures, rules, code with its language;
  and inside them runs of words — emphasised every way markdown has, code, links with where they go, pictures, breaks,
  abbreviations with what they stand for. Every one of those facts is already on the reading, so no writer reads source
  or works anything out again — the same discipline as a builder's (R12, R13).
- **Content in another language** is exported as what its language offers, a language's own `ContentLanguage` saying
  so: a form of its own a writer understands (MathML from a formula for HTML, OMML for Word, once a language offers it),
  else a picture, else its code. Which blocks are pictures is already answered: the language's corner says a block
  saves as one (`IContentLanguage.Corner`, `BlockCorner.Saves`), which is what offers *Save as a picture* on a diagram
  or a formula and never on code. The picture is the one thing the walk cannot make — drawing is WPF — so it asks
  through `IExportPictures`, which `Visuals.Text` answers with the painting that Save and a block's copy already use
  (`MarkdownSurface.CapturePicture`, `ContentEngine.CopyOf`). Which of these a writer takes is the writer's and the
  occasion's: copying to the clipboard as HTML takes no pictures, so a diagram is its code block, as any fence is; the
  site, a Word document or a PDF put the diagram in as its picture.
- **The writers** (`IDocumentWriter`) only spell: text first (the clipboard's plain text and the help search's index,
  which then match what the page shows by construction), HTML next (the clipboard and the site), Word and ODF when they
  are wanted.
- **Where it lives.** The document and the walk sit in `Nexaflow.Markdown/Export`, WPF-free, beside the reading they
  walk. The writers go in a new `Nexaflow.IO.Documents`, WPF-free, which is where a format's own package — OpenXML for
  Word — is taken when that format arrives, so `Nexaflow.Markdown` keeps depending on nothing.

The HTML writer is held to CommonMark's own expected HTML for every core example, which makes it the one check here
that does not go through Markdig at all.

### What else reads markdown

| Today | After |
|---|---|
| `HelpPlainText` — a Markdig walk | the text writer |
| `HelpContents` — Markdig's ids | the anchors `WithTargets` hangs, which are the ids the page follows |
| `MarkdownClipboard` — `Markdig.ToPlainText`, `Markdig.ToHtml` | the text and HTML writers, without pictures |
| `tools/site` — Markdig's HTML renderer | its page shell, its `help:` and `locate:` links and its figures stay; the body is the HTML writer's, so the site renders what the app renders |
| `Features.Markdown` — a reference nothing uses | removed |

## Phase 0 — what has no effect

Independent, and small enough to ride with phase 1: drop `Features.Markdown`'s package reference; drop the
`FootnoteGroup` mapping, which no enabled extension produces.

## Phase 1 — the differential harness

**Goal.** Before a line of the new reader exists, every document in the corpus is read two ways and every place they
disagree is listed. It finds what the glue gets wrong today, and it judges the new reader from its first commit by the
same measure.

### The specifications

Markdig stays the package it is. What the harness needs from Markdig's repository is test data rather than the
component: the specification files Markdig is tested against, taken at the `1.3.2` tag the package was built from and
copied into `Nexaflow.Tests.Markdown/Prose/Differential/Specs/`, with a `LICENSE.md` beside them naming the source, the
tag and the licences — CommonMark's specification is CC BY-SA 4.0, Markdig's own are BSD-2-Clause. They are test data:
nothing ships them, so they are not in the product's third-party notices. Only those for what is enabled: `CommonMark`, `AbbreviationSpecs`, `AlertBlockSpecs`, `AutoLinks`, `DefinitionListSpecs`,
`EmphasisExtraSpecs`, `FigureFooterAndCiteSpecs`, `GridTableSpecs`, `ListExtraSpecs`, `MathSpecs`, `PipeTableSpecs`,
`PipeTableGfmSpecs`, `TaskListSpecs` and `YamlSpecs`. A change that moves the package moves the specifications with it.### The corpus

| Set | Where | What it speaks for |
|---|---|---|
| **Samples** | `MarkdownSamples` (`TestSampleData.Files("markdown")`) | everything the app knows how to render — every diagram, LaTeX, music, codes, plots, the extensions, and `mixed-content.md`, which is long on purpose |
| **CommonMark** | the specification's examples | the core syntax, every corner the spec thought to write down |
| **Extensions** | the extension specifications' examples | each enabled extension, as Markdig defines it |
| **Pages** | the help pages (`src/**/Localization/*/help/*.md`) and `docs/**/*.md` | real documents nobody wrote as tests |
| **Half-written** | each block of Samples and Pages that spans lines, cut at its line ends (six places at most) | what an editor holds all day: fences not closed, tables half drawn |

Each input has an id that survives the corpus growing — `CommonMark#312`, `samples/mermaid-pie.md`,
`pages/docs/testing.md`. A block is cut rather than a whole document because every block before the last is finished:
what a cut adds is the last block's, and a cut is held only to what it parts on that the whole block does not. Every
document is read with `\n` line endings, so an id and what it names are the same on every machine.

### The subjects

- **Markdig** — the whole document parsed once with `MarkdownParser.Pipeline`. The reference.
- **Today** — `MarkdownParser.Parsing()` then `WithTargets` and `WithReferences`: the reading as the app has it once
  what the document defines is known.
- **Scanner** — from phase 2, the new reader through the same stages.

### The projection — `MarkdownShape`

Two trees of different shapes are compared by what both say: the set of **constructs**, each a kind, where its first
and last characters stand, and its facts, ordered by start, then by end descending. Nesting is in the ranges, so no tree
shape has to agree.

- **Kinds** are the shared vocabulary — `MarkdownKinds` for blocks and inlines, `Kinds.Link`, the language of a fence.
  The harness keeps its own table from Markdig's types to these. It is not the reader's mapping, because the mapping is
  part of what is being tested; it becomes the specification the scanner is written to.
- **Ranges** are line endings and trailing space excluded; a container's includes its own marks. Each further
  allowance for a Markdig quirk is a named normalisation with a test of its own and a reason, and nothing else is
  forgiven.
- **Facts** are a heading's rank; a list's ordering, alphabet and start; a box's state; a column's alignment; a cell's
  spans; an emphasis's kind; a link's or picture's destination, resolved; a hard break; a fence's language; a formula
  displayed or inline.
- **Not projected**: what this pipeline works out that Markdig has no notion of — an alert's marker gathered, a
  definition's pairs, a `$$` paragraph as a display formula, an escape as its own piece. Where a construct has a
  counterpart (a definition item, a maths block) its range is compared all the same.
- **Ours alone.** The reading goes past what Markdig reads — a picture's asked size (`|300x200`), every fenced language
  drawn rather than shown as code, an abbreviation used in a block other than the one defining it. Markdig is no oracle
  for those: Samples carries them, they are held to losslessness and the fallback count like everything else, and what
  they mean is held by their own tests.

### What it checks

1. **Agreement.** Today's constructs against Markdig's, for every input. A divergence is the input's id, the side that
   said it, and the construct by its kind, facts and the characters it stands for — never by where it stands, so an
   edit elsewhere in a page leaves it alone. Everything found is written beside the test binary, once as the ledger
   would hold it and once with lines and columns.
2. **The ledger.** `markdown-reading-differences.txt`, beside the tests: one line per divergence that is known, each
   under a line saying why. A divergence not in it fails; an entry that no longer diverges fails until it is taken out —
   a ratchet that only shrinks, like `automation-ids-without-a-journey.txt`. Each group is a glue defect, or Markdig
   departing from the specification's own expected HTML, which is then the oracle for that example.
3. **Losslessness.** Every tree prints as its input and passes `AstOracle.Faults`, on every input and every cut.
4. **The fallback count.** What `Checked` showed as written is projected as a construct of its own
   (`shown-as-written`), and a block of a kind nothing reads as another (`unread`). Markdig has neither, so each one is a
   divergence, and the ledger holds every one there is.
5. **Stretches.** `Windowed` at two soft lengths over Samples and Pages says what `Parsing` says.
6. **Timing**, opt-in by `NEXAFLOW_MARKDOWN_TIMING`, reported and not asserted, in `markdown-reading-costs.tsv` beside
   the test binary: per document, Markdig's reading and the app's, their time and what they allocate, and a keystroke's
   re-read in the middle of the longest document.

It lives in `Nexaflow.Tests.Markdown/Prose/Differential/`, covers `markdown-text` and `reference-links`, and runs with
the suite: the corpus is in the tree, and reading it involves no fonts and no desktop.

**Gate.** The harness is green with the ledger filled and triaged, and the timing baseline is recorded.

The defects the ledger names are not fixed in the glue. Most come from reading a block alone, a line at a time, without
what holds it — which is what the new reader does not do — so they are left to go with it, and whatever is still listed
once phase 3 lands is fixed then.

## Phase 2 — blocks

`MarkdownLines` and `MarkdownScanner` behind `MarkdownParser.Read`, `Inside`, `MarkdownList`, `MarkdownTable` and
`Windowed`, in this order: CommonMark's core rules, then tables, then the rest of the table above. Words are still read by
Markdig through `MarkdownInline` — each reader stands alone, which is what lets the two halves move separately. A stretch
stops at the first block written past its length with nothing else open — never at a definition, nor at a paragraph
opening with a bracket, which is not known to be a block rather than a definition until it closes. `Cut` maps nothing,
and `Closes`, `Unquoted` and `FrontMatterEnd` go; `One`'s ceiling and `Circles` stay, as guards.

**Gate.** Scanner's constructs agree wherever Today's did, and the ledger only shrinks; the fallback count is nought;
every suite that reads markdown passes; a whole reading, and a keystroke's, is no slower and allocates no more than
Today's, best of many runs. The tree is compared with Today's only to account for every way it differs: where Markdig's
spans were arbitrary the scan's are the characters' own — a cell is its words without the space round them, a row is its
whole line, a blank line after a list stands between the list and what follows — and those differences are the point
rather than a regression.## Phase 3 — words

The inline reader and `MarkdownDelimiters` behind `MarkdownInline`, and `WithReferences` resolving from the list the
reader keeps. Markdig leaves `Nexaflow.Markdown`.

**Gate.** As phase 2, over inline constructs too, including the keystroke timing.

## Phase 4 — export

The export document, the walk and `IExportPictures`; the text and HTML writers in `Nexaflow.IO.Documents`, the HTML
one held to CommonMark's expected HTML. `HelpPlainText`, `HelpContents`, the clipboard and `tools/site` on them.
`MarkdownParser.Rewrite`'s list of the characters that need a backslash (`Marked`) is taken from the scanner's own
character classes, so what is escaped and what is read as markup are one table. Markdig then stays only where it is the
oracle, `Tests.Markdown`.

## Phase 5 — the documents

The design half of this file into [markdown-ast.md](markdown-ast.md#markdown); [MarkdownSupport.md](MarkdownSupport.md)
names the reader's rules where it names Markdig's extensions; the product tree's concerns and snaplinks follow; this
file goes.
