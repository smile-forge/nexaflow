namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils

// The plain-TeX font and size switches: {\cal N} rather than \mathcal{N}. LaTeX2e deprecated them
// and amsmath never documented them, so nothing here had them - and then a corpus of 238,000
// formulas lifted from published papers turned out to reject 35,392 of them, of which 33,463 were
// one of these (tools/latex-corpus). A formula copied out of a paper is written in them.
type PlainTexSwitchTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    static let boxOf (markup: string) = (parse markup).RootAtom.CreateBox(environment)
    static let widthOf (markup: string) = (boxOf markup).Width
    static let heightOf (markup: string) = (boxOf markup).TotalHeight

    /// Which face the character came out of - the whole point of a font switch, and the one thing a
    /// width cannot tell you, since two alphabets can set a letter to the same width.
    static let fontOf (markup: string) = (boxOf markup).GetLastFontId()

    [<Theory>]
    [<InlineData(@"\cal", @"\mathcal", "N")>]
    [<InlineData(@"\bf", @"\mathbf", "p")>]
    [<InlineData(@"\it", @"\mathit", "H")>]
    [<InlineData(@"\mit", @"\mathit", "H")>]
    [<InlineData(@"\rm", @"\mathrm", "d")>]
    [<InlineData(@"\sf", @"\mathsf", "x")>]
    [<InlineData(@"\tt", @"\mathtt", "x")>]
    [<InlineData(@"\frak", @"\mathfrak", "g")>]
    [<InlineData(@"\scr", @"\mathscr", "L")>]
    member _.``a font switch is its math command applied to the rest of the group``
        (switch: string, command: string, letter: string) =
        let switched = "{" + switch + " " + letter + "}"
        let applied = command + "{" + letter + "}"

        Assert.Equal(fontOf applied, fontOf switched)
        Assert.Equal(widthOf applied, widthOf switched, 6)

    [<Theory>]
    [<InlineData(@"\cal", "N")>]
    [<InlineData(@"\bf", "p")>]
    [<InlineData(@"\rm", "d")>]
    [<InlineData(@"\sf", "x")>]
    [<InlineData(@"\tt", "x")>]
    [<InlineData(@"\frak", "g")>]
    [<InlineData(@"\scr", "L")>]
    member _.``a font switch reaches a different face``(switch: string, letter: string) =
        // The switches that name an alphabet of their own, as opposed to \it and \mit, which ask for
        // the italic a letter in maths is set in anyway.
        Assert.NotEqual(fontOf letter, fontOf ("{" + switch + " " + letter + "}"))

    [<Fact>]
    member _.``a font switch stops at the end of its group``() =
        // The whole difference between a switch and a one-argument command: {\bf a}b bolds the a only.
        Assert.Equal(widthOf @"\mathbf{a}b", widthOf @"{\bf a}b", 6)
        Assert.NotEqual(widthOf @"\mathbf{ab}", widthOf @"{\bf a}b")

    [<Fact>]
    member _.``a font switch does not swallow a matrix's separators``() =
        // It consumes the rest of its group, and a cell is a group - so & and \\ have to survive it.
        let switched = @"\begin{matrix} {\bf a} & b \\ c & d \end{matrix}"
        Assert.Equal(widthOf @"\begin{matrix} \mathbf{a} & b \\ c & d \end{matrix}", widthOf switched, 6)
        Assert.Equal(heightOf @"\begin{matrix} \mathbf{a} & b \\ c & d \end{matrix}", heightOf switched, 6)

    [<Theory>]
    [<InlineData(@"\frac{\cal A}{\cal B}")>]
    [<InlineData(@"S_{\mathrm{\scriptsize gauged}}")>]
    [<InlineData(@"{\cal W}_{\mathrm{tree}} = y \Phi")>]
    [<InlineData(@"\left\{ \begin{array}{c} {\phi = {\bf p}^{2}} \end{array} \right.")>]
    member _.``a switch renders where a paper puts one``(markup: string) =
        Assert.NotNull(boxOf markup)

    [<Fact>]
    member _.``the size switches that have an equivalent here shrink``() =
        Assert.True(widthOf @"{\scriptsize x}" < widthOf @"x")
        Assert.True(widthOf @"{\tiny x}" < widthOf @"{\scriptsize x}")
