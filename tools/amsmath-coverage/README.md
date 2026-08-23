# amsmath coverage

How much of [amsmath](https://ctan.org/pkg/amsmath) the maths in Nexaflow's markdown can
actually render, measured rather than assumed.

```powershell
dotnet build external/xaml-math/src/WpfMath/WpfMath.csproj -f net8.0-windows   # once
pwsh tools/amsmath-coverage/Check-AmsMathCoverage.ps1
```

Every construct in `amsmath-checklist.txt` is parsed **and rendered** through the real
engine (`WpfMath`, from `external/xaml-math`). Rendering, not parsing, is the test: a
wrong glyph mapping parses perfectly well and only falls over when a box is built for
it, so a parse-only check would report success for something that cannot be drawn.

Each line comes out as one of:

| | |
|---|---|
| `OK` | renders |
| `~~` | renders, but the checklist records a caveat — see below |
| `NO` | the engine rejects it, with the message it gave |
| `--` | nothing a formula can invoke at all - a length register rather than a command - so not counted either way |

## Caveats

The survey can only see *whether* a formula came out, not whether it came out **right**.
Anything known to be approximate has its caveat written in the fourth column of the
checklist, and is then reported as `~~` rather than `OK`. `\smash[t]` is the worked
example: the optional argument is not read, so `[t]` is typeset as two characters and the
formula "renders" while being wrong.

If you find another case like that, add the caveat to the checklist rather than leaving a
misleading `OK`.

The document-level commands - `\tag`, `\label`, `\intertext`, `\DeclareMathOperator` and the rest -
are all caveated, because they are read and dropped: there is no page here to number or break. Their
samples have to sit beside real maths the way they would in a document, since a formula that renders
to nothing is reported as broken, which for these would be the wrong answer.

## Keeping the checklist honest

```powershell
pwsh tools/amsmath-coverage/Check-AmsMathCoverage.ps1 -Refresh
```

fetches the current amsmath from CTAN, prints the version it found, and lists any command
or environment the package's own user guide names that the checklist does not cover — so
something added upstream shows up as a gap rather than being quietly missed. Names
belonging to LaTeX itself, or to the guide's own examples, are filtered out; the rest are
worth a look, though judge each one, since the guide mentions constructs in prose as well
as documenting them.

The checklist is grouped by the section of the guide that introduces each construct, so a
gap in a whole area is visible at a glance.

## Other options

- `-Json` — the per-construct results as JSON, for feeding somewhere else.
- `-Configuration Release` — measure a Release build of the engine instead of Debug.

## Why a script and not a test

This is a survey of a third-party specification, not a statement about our own behaviour.
It is expected to report gaps, so it must not fail a build. The things we *have* decided
to support are pinned by real tests in `external/xaml-math/src/WpfMath.Tests`, and the
LaTeX reference pages under `test-samples/markdown/latex-math-*.md` are held to their own
claims by `MarkdownSampleRenderTests.LatexMathSamplesTypeset`.
