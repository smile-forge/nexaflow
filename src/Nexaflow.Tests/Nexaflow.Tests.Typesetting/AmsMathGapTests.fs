namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils
open XamlMath.Atoms
open XamlMath.Rendering

// The amsmath constructs closed after surveying the package against the engine
// (tools/amsmath-coverage). Each is cheap on its own; together they were the bulk of what
// was missing that a formula in a markdown document would actually reach for.
type AmsMathGapTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    static let renders (markup: string) =
        let root = (parse markup).RootAtom
        Assert.NotNull(root)
        Assert.NotNull(root.CreateBox(environment))

    static let widthOf (markup: string) = (parse markup).RootAtom.CreateBox(environment).Width

    /// Where the topmost ink of a formula lands - the only thing separating two shapes
    /// that share a width.
    static let inkTop (markup: string) =
        let geometry = System.Windows.Media.GeometryGroup()
        let renderer = GeometryElementRenderer(geometry, 1.0) :> IElementRenderer
        renderer.RenderElement((parse markup).RootAtom.CreateBox(environment), 0.0, 0.0)
        geometry.Bounds.Top

    // ── italic capital Greek ─────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData("Gamma")>]
    [<InlineData("Delta")>]
    [<InlineData("Theta")>]
    [<InlineData("Lambda")>]
    [<InlineData("Xi")>]
    [<InlineData("Pi")>]
    [<InlineData("Sigma")>]
    [<InlineData("Upsilon")>]
    [<InlineData("Phi")>]
    [<InlineData("Psi")>]
    [<InlineData("Omega")>]
    member _.``italic Greek capitals are the maths italic, not the roman``(name: string) =
        // \varGamma and \Gamma are the same letter from two faces: the upright roman and the maths
        // italic. Which font each resolves to is the whole difference — \Delta and \Lambda happen
        // to have the same advance width in both, so measuring them would prove nothing.
        let fontOf (markup: string) =
            let atom = Assert.IsType<SymbolAtom>((parse markup).RootAtom)
            atom.GetCharFont(environment.MathFont).Value.FontId

        Assert.Equal(1, fontOf ("\\" + name))          // cmr10, upright
        Assert.Equal(0, fontOf ("\\var" + name))       // cmmi10, italic

    // ── the semantic dots ────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\dotsb", @"\cdots")>]     // between binary operators and relations
    [<InlineData(@"\dotsi", @"\cdots")>]     // with integrals
    [<InlineData(@"\dotsm", @"\cdots")>]     // between factors
    [<InlineData(@"\dotsc", @"\ldots")>]     // with commas
    [<InlineData(@"\dotso", @"\ldots")>]     // anything else
    member _.``each dots command resolves to the shape amsmath gives it``(dots: string, shape: string) =
        // The two shapes are the same width, so only where the ink sits tells them apart: \cdots
        // rides the axis, \ldots sits on the baseline.
        renders dots
        Assert.Equal(inkTop shape, inkTop dots, 6)
        Assert.NotEqual(inkTop @"\ldots", inkTop @"\cdots")   // the comparison above has teeth

    // ── the limit-like operators ─────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\injlim")>]
    [<InlineData(@"\projlim")>]
    [<InlineData(@"\varinjlim")>]
    [<InlineData(@"\varprojlim")>]
    [<InlineData(@"\varliminf")>]
    [<InlineData(@"\varlimsup")>]
    [<InlineData(@"\injlim_{n} A_n")>]
    [<InlineData(@"\varprojlim_{n} A_n")>]
    member _.``the limit operators render``(markup: string) = renders markup

    [<Fact>]
    member _.``the decorated limits are taller than a bare lim``() =
        // \varliminf is lim underlined, \varlimsup lim overlined, \varinjlim lim over an arrow.
        let bare = (parse @"\lim").RootAtom.CreateBox(environment).TotalHeight
        for markup in [ @"\varliminf"; @"\varlimsup"; @"\varinjlim"; @"\varprojlim" ] do
            let decorated = (parse markup).RootAtom.CreateBox(environment).TotalHeight
            Assert.True(decorated > bare, $"{markup} is no taller than a plain lim")

    // ── stretchy arrow accents ───────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\overrightarrow{AB}")>]
    [<InlineData(@"\overleftarrow{AB}")>]
    [<InlineData(@"\overleftrightarrow{AB}")>]
    [<InlineData(@"\underrightarrow{AB}")>]
    [<InlineData(@"\underleftarrow{AB}")>]
    [<InlineData(@"\underleftrightarrow{AB}")>]
    member _.``arrow accents are parsed as an OverArrowAtom``(markup: string) =
        Assert.IsType<OverArrowAtom>(parse(markup).RootAtom) |> ignore
        renders markup

    [<Fact>]
    member _.``an under-arrow grows downwards and an over-arrow upwards``() =
        let bare = (parse @"AB").RootAtom.CreateBox(environment)
        let over = (parse @"\overrightarrow{AB}").RootAtom.CreateBox(environment)
        let under = (parse @"\underrightarrow{AB}").RootAtom.CreateBox(environment)

        Assert.True(over.Height > bare.Height, "an over-arrow should add height")
        Assert.Equal(bare.Depth, over.Depth, 6)
        Assert.True(under.Depth > bare.Depth, "an under-arrow should add depth")
        Assert.Equal(bare.Height, under.Height, 6)

    // ── one-sided vertical bars ──────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\lvert x \rvert")>]
    [<InlineData(@"\lVert x \rVert")>]
    [<InlineData(@"\left\lvert \frac{a}{b} \right\rvert")>]
    member _.``the one-sided bars render``(markup: string) = renders markup

    [<Fact>]
    member _.``a one-sided bar is the same glyph as the plain one``() =
        Assert.Equal(widthOf @"\lvert", widthOf @"\vert", 6)
        Assert.Equal(widthOf @"\lVert", widthOf @"\Vert", 6)

    // ── binomials in a forced style ──────────────────────────────────────────────

    [<Fact>]
    member _.``dbinom and tbinom keep their size inside a script``() =
        // The whole point of them: a plain \binom shrinks with the surrounding style, these do not.
        let plain = widthOf @"x_{\binom{n}{k}}"
        Assert.True(widthOf @"x_{\dbinom{n}{k}}" > plain)
        Assert.True(widthOf @"x_{\tbinom{n}{k}}" > plain)

    // ── \pmb ─────────────────────────────────────────────────────────────────────

    [<Fact>]
    member _.``pmb is boldsymbol``() =
        // amsmath's \pmb fakes bold by overprinting, because it predates having a bold face.
        // There is a real one here, so \pmb takes it.
        Assert.Equal(widthOf @"\boldsymbol{\alpha}", widthOf @"\pmb{\alpha}", 6)
