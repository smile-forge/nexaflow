# latex-corpus

Runs a large corpus of real LaTeX through the engine Nexaflow renders maths with, and reports
where it disagrees with the renderings the corpus ships.

The [amsmath survey](../amsmath-coverage) asks whether we support the constructs a *specification*
documents. This asks the other question: what do people actually write, and what happens when we
try to draw it. Roughly 230,000 formulas lifted from published papers turn out to answer that
rather differently.

```powershell
dotnet build tools/latex-corpus/LatexCorpus.csproj
$corpus = "tools/latex-corpus/bin/x64/Debug/net10.0-windows/latex-corpus.exe"

& $corpus parse   --dataset D:\Datasets\im2latex_230k\PRINTED_TEX_230k --out parse.txt
& $corpus compare --dataset D:\Datasets\im2latex_230k\PRINTED_TEX_230k --out report.html --limit 5000
& $corpus render  --dataset D:\Datasets\im2latex_230k\PRINTED_TEX_230k --out ours\ --limit 200
```

## The dataset

Any folder in the im2latex shape works:

| | |
|---|---|
| `final_png_formulas.txt` | one formula per line |
| `corresponding_png_images.txt` | the reference image for each, same line order |
| `generated_png_images/` | the images themselves |

The formulas are tokenised — `R _ { 1 2 }` rather than `R_{12}` — which parses the same, since
spaces between tokens mean nothing in maths mode.

## `parse` — what the engine cannot take

Runs every formula through the parser **and the box builder**, and groups the rejections by
message, most common first, with sample formulas for each. This is the cheap half of the exercise
and where most of the bugs are: no images are involved, nothing is subjective, and a single
missing command shows up with a count beside it.

## `compare` — where the drawing disagrees

Renders each formula, then scores it against the corpus image by **ink overlap**: crop both to
their ink, scale both to a common height, give both the same total amount of ink, and measure how
much of it lands in the same place. 1 is identical, 0 is nothing in common.

Equalising the total is what makes it a question about placement rather than weight - one
rasteriser at 20 pixels tall lays pale grey strokes where another at 50 lays black ones, and none
of that difference is a rendering bug.

What the numbers mean in practice, measured against this corpus:

| | |
|---|---|
| **0.8 and up** | the same rendering, down to the pixel |
| **around 0.5** | correct, with different spacing - where most of the corpus sits |
| **0.35 and below** | worth looking at; this is what `--flag` defaults to |
| **below 0.25** | usually a real defect |

**It is a ranking signal, not a verdict.** Two rasterisers never agree on a pixel - anti-aliasing,
hinting and sub-pixel placement see to that - so the scale-and-average is there to blur those away
and leave the shape. What survives is real: a missing glyph, a delimiter that did not grow, a
script on the wrong side, a fraction set as a row. What also survives, unfortunately, is honest
disagreement: a small spacing difference early in a long formula shifts everything after it, and
the score falls for a rendering that is different rather than wrong. So read the report from the
worst end and stop when the rows stop being interesting; never put a threshold in a build.

The report is an HTML page — reference above ours, worst first, each row with a tick box and a
**Copy picked** button, so a pass through it comes back as a list of formulas worth fixing. By
default the images sit in a folder beside the page and load as they are scrolled to, which is what
makes a few thousand rows bearable; `--embed` puts them in the file instead, for sending on.

## `render` — our rendering, as PNGs

Writes what we draw for each formula, named after the corpus image it corresponds to. Useful for
looking at a set outside the report, or for keeping a before-and-after around while fixing
something.

## Options

| | |
|---|---|
| `--dataset <folder>` | the corpus |
| `--out <path>` | report file, or output folder for `render` |
| `--limit <n>` | stop after n formulas (0 = all; `compare` defaults to 2000) |
| `--skip <n>` | start n formulas in |
| `--scale <n>` | formula scale to render at, default 20 |
| `--flag <0..1>` | ink overlap at or below which a row is flagged, default 0.35 |
| `--top <n>` | how many rows the report shows, default 300 |
| `--all-rows` | report every compared formula, not only the flagged ones |
| `--embed` | put the images in the HTML rather than in a folder beside it |

## Why it is not a test

It measures agreement with someone else's renderer over material we do not control, so it is
expected to disagree and must not fail a build. What we have decided to support is pinned by the
tests in `external/xaml-math/src/WpfMath.Tests`; this is how we find out what to decide next.
