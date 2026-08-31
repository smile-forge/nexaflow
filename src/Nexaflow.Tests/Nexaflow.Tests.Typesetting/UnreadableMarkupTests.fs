module WpfMath.Tests.UnreadableMarkupTests

open Xunit

open WpfMath.Tests.Utils

// What becomes of markup that cannot be taken at face value.
//
// Every case here was a TexParseException with a message, raised by a parser that then abandoned the
// formula and left the reader looking at nothing. Nothing throws now: a stretch that cannot be read
// is carried through and marked, and what is shown is the characters that were typed with a squiggle
// under them - which is a great deal more use than a blank space. So the cases are still the cases
// and only the answer has changed, which is why they are kept rather than deleted.
//
// It comes back in one of two ways, and which is not a detail: a command nobody has heard of was
// never read at all, and one that was read but has no drawing is set as its own characters. The
// reader is told which by the colour of the squiggle, so these say which too.

// ── read, and nothing to draw ────────────────────────────────────────────────────

[<Theory>]
[<InlineData(@"\left x\right)")>]         // x is not a delimiter
[<InlineData(@"\left{")>]                 // nor is a bare brace; \{ is
[<InlineData(@"\left{2+2\right\}")>]      // and nothing closes the { that \left was handed
[<InlineData(@"\sqrt")>]                  // no radicand
[<InlineData(@"\sum_ ")>]                 // no subscript
[<InlineData(@"\frac{}")>]                // one argument, and it empty
[<InlineData(@"\binom{}")>]
[<InlineData(@"\color")>]
[<InlineData(@"\color{red}")>]            // a colour, and nothing to colour with it
let ``what cannot be drawn is set as the characters written``(markup: string): unit =
    Assert.NotEmpty(undrawn markup)
    Assert.Empty(unreadable markup)

// ── never read at all ────────────────────────────────────────────────────────────

[<Theory>]
[<InlineData(@"\left\x\right)", @"\x")>]
[<InlineData(@"\left\", @"\")>]
let ``a command nobody has heard of is marked as unread``(markup: string, unknown: string): unit =
    // The other colour. There is no drawing to decline because there is no command: the name itself
    // is what could not be made sense of, and the mark goes on exactly that.
    Assert.Equal<string list>([ unknown ], unreadable markup)

// ── colours ──────────────────────────────────────────────────────────────────────

[<Theory>]
[<InlineData(@"\color [nonexistent123] {red} x")>]
[<InlineData(@"\colorbox [nonexistent123] {red} x")>]
[<InlineData(@"\color {reddit} x")>]
[<InlineData(@"\colorbox {reddit} x")>]
[<InlineData(@"\color [gray] {x} x")>]
[<InlineData(@"\color [gray] {1.01} x")>]
[<InlineData(@"\color [argb] {2, 0.5, 0.5, 0.5} x")>]
[<InlineData(@"\color [argb] {x, 0.5, 0.5, 0.5} x")>]
[<InlineData(@"\color [ARGB] {256, 128, 128, 128} x")>]
[<InlineData(@"\color [ARGB] {x, 128, 128, 128} x")>]
[<InlineData(@"\color [cmyk] {2, 0.5, 0.5, 0.5, 0.1} x")>]
[<InlineData(@"\color [cmyk] {x, 0.5, 0.5, 0.5, 0.1} x")>]
[<InlineData(@"\color [HTML] {wwwwwwww} x")>]
let ``a colour that cannot be read costs the command and not the formula``(markup: string): unit =
    // A colour model nobody has heard of, a colour nobody has heard of, and numbers outside what the
    // model takes. All the same answer: the command is set as its own characters and what it was
    // going to colour is still set as maths.
    let marked = undrawn markup
    Assert.NotEmpty marked
    Assert.All(marked, fun stretch -> Assert.StartsWith(@"\color", stretch))
