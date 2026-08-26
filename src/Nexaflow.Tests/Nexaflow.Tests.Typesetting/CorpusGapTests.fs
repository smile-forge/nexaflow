namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils
open XamlMath.Atoms

// Commands a corpus of published papers reached for that nothing here had: two arrows, two spaces,
// the retyping family, and the characters that have to be escaped to be written at all
// (tools/latex-corpus).
type CorpusGapTests() =
    static do initializeFontResourceLoading()

    static let parse (markup: string) = WpfTeXFormulaParser.Instance.Parse(markup)
    static let environment = WpfTeXEnvironment.Create()

    static let boxOf (markup: string) = (parse markup).RootAtom.CreateBox(environment)
    static let widthOf (markup: string) = (boxOf markup).Width

    // ── arrows ───────────────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\longleftrightarrow")>]
    [<InlineData(@"\longmapsto")>]
    [<InlineData(@"\hookleftarrow")>]
    member _.``the arrows that were missing render``(markup: string) =
        Assert.NotNull(boxOf markup)

    [<Fact>]
    member _.``a long arrow is longer than its short form``() =
        // Each is built by butting a shaft against an arrowhead, the way the ones already here are.
        Assert.True(widthOf @"\longleftrightarrow" > widthOf @"\leftrightarrow")
        Assert.True(widthOf @"\longmapsto" > widthOf @"\mapsto")

    [<Fact>]
    member _.``hookleftarrow is the mirror of hookrightarrow``() =
        Assert.Equal(widthOf @"\hookrightarrow", widthOf @"\hookleftarrow", 1)

    // ── spaces ───────────────────────────────────────────────────────────────────

    [<Fact>]
    member _.``the spaces are the widths they are named for``() =
        // \enspace is half a quad, and an interword space is narrower again.
        let gap (markup: string) = widthOf ("a " + markup + " b") - widthOf "a b"
        Assert.Equal(gap @"\qquad" / 4.0, gap @"\enspace", 6)
        Assert.True(gap @"\space" < gap @"\enspace")
        Assert.True(gap @"\space" > 0.0)

    [<Fact>]
    member _.``mspace is hspace in math units``() =
        Assert.Equal(widthOf @"a\hspace{18mu}b", widthOf @"a\mspace{18mu}b", 6)
        Assert.True(widthOf @"a\mspace{18mu}b" > widthOf @"a\mspace{9mu}b")

    // ── retyping ─────────────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\mathord", "x")>]
    [<InlineData(@"\mathop", "x")>]
    [<InlineData(@"\mathbin", "x")>]
    [<InlineData(@"\mathrel", "x")>]
    [<InlineData(@"\mathopen", "[")>]
    [<InlineData(@"\mathclose", "]")>]
    [<InlineData(@"\mathpunct", ",")>]
    [<InlineData(@"\mathinner", "x")>]
    member _.``a retyping command keeps its argument and changes its kind``
        (command: string, content: string) =
        Assert.IsType<TypedAtom>(parse(command + "{" + content + "}").RootAtom) |> ignore

    [<Fact>]
    member _.``retyping is what changes the space around a symbol``() =
        // The whole reason the family exists: same glyph, different spacing either side of it.
        Assert.True(widthOf @"a \mathrel{x} b" > widthOf @"a \mathord{x} b")
        Assert.True(widthOf @"a \mathbin{x} b" > widthOf @"a \mathord{x} b")

    [<Fact>]
    member _.``mathop takes a limit rather than a script``() =
        // \mathop{argmax}_x is the reason a paper writes it: the x becomes a limit under the name.
        Assert.NotEqual(widthOf @"\mathop{argmax}_{x}", widthOf @"\mathord{argmax}_{x}")

    // ── escaped literals ─────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\#")>]
    [<InlineData(@"\$")>]
    [<InlineData(@"\%")>]
    [<InlineData(@"\&")>]
    [<InlineData(@"\_")>]
    member _.``an escaped literal renders``(markup: string) =
        Assert.True(widthOf ("a" + markup + "b") > widthOf "ab")

    [<Fact>]
    member _.``the underscore is drawn, there being no glyph for it``() =
        // OT1 has no underscore, so LaTeX draws a short rule under the baseline; so does this.
        Assert.IsType<RuleAtom>(parse(@"\_").RootAtom) |> ignore
        Assert.True(widthOf @"\_" > 0.0)
