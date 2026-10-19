# Jbig2

A JBIG2 decoder in memory-safe C#: generic regions arithmetically coded with any template and its adaptive pixels, or
MMR coded; generic refinement regions; text regions and symbol dictionaries, Huffman or arithmetically coded, with
refinement and aggregation, custom Huffman tables and coding contexts carried from one dictionary to the next; halftone
regions and pattern dictionaries; striped pages, of known height or not; files with a header, sequential or random
access, and the headerless streams a PDF embeds, with the segments of their `JBIG2Globals` stream. It decodes the first
page to one bit a pixel. It decodes only; Nexaflow writes no JBIG2 of its own.

## Origin

A port of [Apache PDFBox](https://pdfbox.apache.org/)'s JBIG2 ImageIO plugin (pdfbox-jbig2), by way of BobLd's C#
port, [PdfPig.Filters.Jbig2.PdfboxJbig2](https://github.com/BobLd/UglyToad.PdfPig.Filters.Jbig2.PdfboxJbig2) 0.1.16.2.
Each file names the Java files it ports, keeps the standard's clause numbers in its comments, and says how it was
changed. It is held to two references:

- **SerenityOS's JBIG2 test files** (`Media/Jbig2/Fixtures/` in `Tests.Components`): files made by SerenityOS's own
  encoder to exercise every coding path one at a time, each decoding to the same picture. Every one decodes to it,
  bit for bit (`Jbig2DecoderTests`).
- **jbig2dec**, the decoder Ghostscript and MuPDF are built on: every JBIG2 image in the PDF corpus decodes to the
  bits jbig2dec decodes it to (`Jbig2ComparisonSweep`, `tools/bench/Run-Jbig2Bench.ps1`). jbig2dec is AGPL: it is
  built outside the repository and only measured against, never carried or ported.

## Where it departs from PDFBox

- **Generic refinement is rewritten from 6.3.5.** Each pixel's context is formed as Figures 12 and 13 number its
  pixels, and the second adaptive pixel is read from the reference at the place the region puts it. PDFBox's
  byte-packed refinement paths numbered the contexts otherwise and placed that pixel on the wrong side of the
  reference's offset, and decoded refinement — of a page, a symbol or a symbol instance — wrongly. With TPGRON, the
  context of SLTP is the template with only the reference pixel under the decoded one set, for template 1 as for
  template 0; jbig2dec sets another pixel for template 1, and the test files agree with the standard.
- **A Huffman-coded refinement decodes its own bytes.** In a Huffman-coded text region or symbol dictionary, a
  refinement bitmap is coded in the SYMINREFSIZE bytes that follow its size, and is decoded by an arithmetic decoder of
  its own that starts at them and reads no further, as 6.4.11 and 6.5.8.2.2 have it.
- **Damaged data decodes as far as it goes.** Past the end of its data the arithmetic decoder reads 0xFF — the marker
  that ends arithmetically coded data — and the MMR decoder zeros, so a segment cut short decodes to the end of its
  region, as jbig2dec does. A segment whose data the stream does not hold in full is read as far as it goes; a text
  region whose data runs out keeps the symbols it placed; a region that cannot be decoded at all is left off the page;
  each is reported in `Jbig2Image.Warnings`. Every value the data gives is checked before it sizes or indexes
  anything — a symbol identifier past the symbols, a gray-scale value past the patterns, which draws the last pattern
  as jbig2dec draws it, a symbol no pixels wide — so a damaged stream yields a page or a `Jbig2Exception` and nothing
  else.
- **Memory and work are limited.** Every bitmap, dictionary and table is charged to `Jbig2DecodeOptions.MemoryLimit`;
  a header that claims a page or a grid of any size is refused when its memory would pass the limit, before it is
  allocated. Every pixel decoded, combined or copied — and every row and every bitmap, so none is free — counts against
  `Jbig2DecodeOptions.WorkLimit`, a multiple of the page's pixels: a few corrupted bytes can claim millions of symbol
  instances or a grid of millions of cells and little memory, and are refused in a fraction of a second rather than
  decoded for minutes, as jbig2dec decodes them. A loop the data drives must make progress: two empty export runs or
  two empty height classes in a row are damage, not a reason to go on forever.
- **Globals are decoded once.** `Jbig2Globals` reads a `JBIG2Globals` stream once, and decodes each of its dictionaries
  the first time a page needs it; the pages of a document decode its shared symbol dictionary once between them, on
  any number of threads.
- **One reader and one blit.** A segment's bytes are read through one reader over a stretch of an array, in place of
  the stream and sub-stream classes; one blit and one extract, clipped to both bitmaps, stand in for the shifted,
  unshifted and pixel-by-pixel variants. An MMR-coded height class takes exactly its BMSIZE bytes. A page is the one
  region on it only when that region covers it exactly and its operator leaves it as it is.
- **The MQ decoder keeps a context in a byte.** The state's index in Table E.1 sits above the more probable symbol,
  and what follows each symbol is looked up; the decisions are the same.
- **Spans and vectors only:** no `unsafe`, no `Unsafe`, no reinterpreting casts of memory.

## Licence

The Apache License 2.0: [LICENSE](LICENSE) and [NOTICE](NOTICE) beside this file, the header of each source file, and
the entry in `src/Nexaflow.Core/Assets/ThirdPartyNotices.md`.
