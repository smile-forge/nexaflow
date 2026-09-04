namespace WpfMath.Tests

open Xunit

open WpfMath.Rendering
open WpfMath.Tests.Utils

// What the amsmath survey (tools/amsmath-coverage) turned up and is still pinned here: where an
// operator's limits go, the \big family of set-size delimiters, \genfrac — the general fraction the
// others are spelled with — and escaped braces inside a group.
type AmsMathDisplayTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    static let boxOf (markup: string) = (parse markup).RootAtom.CreateBox(environment)
    static let widthOf (markup: string) = (boxOf markup).Width
    static let heightOf (markup: string) = (boxOf markup).TotalHeight

    static let renders (markup: string) =
        Assert.NotNull((parse markup).RootAtom)
        Assert.NotNull(boxOf markup)

    // ── display environments ─────────────────────────────────────────────────────

    [<Fact>]
    member _.``multline sets its lines like gather``() =
        let markup = @"\begin{multline} a + b \\ c + d \end{multline}"
        Assert.Equal(widthOf @"\begin{gathered} a + b \\ c + d \end{gathered}", widthOf markup, 6)
        Assert.Equal(heightOf @"\begin{gathered} a + b \\ c + d \end{gathered}", heightOf markup, 6)

    // ── where an operator's limits go ────────────────────────────────────────────

    [<Fact>]
    member _.``limits stacks the scripts where the style would have set them beside``() =
        // Text style puts an operator's scripts beside it; \limits overrides that, and the operator
        // grows taller and stops being widened by them.
        let beside = @"\textstyle\sum_{i}^{n} x"
        let stacked = @"\textstyle\sum\limits_{i}^{n} x"
        Assert.True(heightOf stacked > heightOf beside, "\\limits should stack the scripts")
        Assert.True(widthOf stacked < widthOf beside, "stacked scripts should no longer widen the operator")

    [<Fact>]
    member _.``nolimits sets the scripts beside where the style would have stacked them``() =
        let stacked = @"\displaystyle\sum_{i}^{n} x"
        let beside = @"\displaystyle\sum\nolimits_{i}^{n} x"
        Assert.True(heightOf beside < heightOf stacked, "\\nolimits should unstack the scripts")
        Assert.True(widthOf beside > widthOf stacked, "scripts set beside the operator widen it")

    [<Fact>]
    member _.``a limit control is only a command after an operator``() =
        // Anywhere it could not mean anything it is left undrawn and set as its own characters,
        // rather than being quietly eaten and leaving a formula that looks like it was understood.
        Assert.Equal<string list>([ @"\limits" ], undrawn @"x\limits_{i}")

    // ── \big, \Big, \bigg, \Bigg ─────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\bigl( x \bigr)")>]
    [<InlineData(@"\Bigl[ x \Bigr]")>]
    [<InlineData(@"\biggl\{ x \biggr\}")>]
    [<InlineData(@"\Biggl\langle x \Biggr\rangle")>]
    [<InlineData(@"\big| x \big|")>]
    [<InlineData(@"x \bigm| y")>]
    member _.``the sized delimiters render``(markup: string) = renders markup

    [<Fact>]
    member _.``the four sizes step up, starting above the plain delimiter``() =
        let sizes = [ @"\big("; @"\Big("; @"\bigg("; @"\Bigg(" ] |> List.map heightOf
        Assert.True(List.head sizes > heightOf @"(", "\\big should be taller than a plain (")
        Assert.Equal<double list>(List.sort sizes, sizes)
        Assert.Equal(4, sizes |> List.distinct |> List.length)

    [<Fact>]
    member _.``a sized delimiter does not shrink with the style``() =
        // TeX gives \big and its friends absolute lengths rather than sizes relative to the style, so
        // a \Big( inside a script is the delimiter it was outside one.
        Assert.True(heightOf @"\scriptstyle(" < heightOf @"(", "a plain delimiter does shrink")
        Assert.True(heightOf @"\scriptstyle\Big(" >= heightOf @"\Big(", "a \\Big one should not")

    [<Fact>]
    member _.``the m spelling spaces as a relation``() =
        // Same delimiter at the same size either way; what the l/r/m spellings change is the atom
        // type, and so the space around it.
        Assert.Equal(heightOf @"\bigl(", heightOf @"\bigr(", 6)
        Assert.True(widthOf @"x \bigm| y" > widthOf @"x \big| y", "a relation takes more space around it")

    [<Fact>]
    member _.``a sized delimiter needs something that is a delimiter``() =
        Assert.NotEmpty(undrawn @"\big x")

    // ── \genfrac ─────────────────────────────────────────────────────────────────

    [<Fact>]
    member _.``genfrac with nothing asked for is frac``() =
        Assert.Equal(widthOf @"\frac{n}{k}", widthOf @"\genfrac{}{}{}{}{n}{k}", 6)
        Assert.Equal(heightOf @"\frac{n}{k}", heightOf @"\genfrac{}{}{}{}{n}{k}", 6)

    [<Theory>]
    [<InlineData(@"\genfrac{(}{)}{0pt}{4}{n}{k}", @"\genfrac{(}{)}{0pt}{}{n}{k}")>]   // there is no style 4
    [<InlineData(@"\genfrac{(}{)}{banana}{}{n}{k}", @"\genfrac{(}{)}{}{}{n}{k}")>]    // not a length
    [<InlineData(@"\genfrac{(}{)}{1}{}{n}{k}", @"\genfrac{(}{)}{}{}{n}{k}")>]         // a number with no unit
    member _.``genfrac falls back where it cannot read a thickness or a style``(markup: string, asIf: string) =
        // It used to refuse the formula outright. It now sets the fraction as though the argument had
        // been left empty — and does so silently, which is the one place in this suite where
        // something unreadable leaves no mark on the formula at all.
        Assert.Empty(undrawn markup)
        Assert.Equal(widthOf asIf, widthOf markup, 6)
        Assert.Equal(heightOf asIf, heightOf markup, 6)

    // ── escaped braces inside a group ────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"{\{}")>]
    [<InlineData(@"\frac{\{}{x}")>]
    [<InlineData(@"\text{\{}")>]
    [<InlineData(@"\genfrac{\{}{\}}{0pt}{}{n}{k}")>]
    member _.``an escaped brace inside a group is a character, not a nesting level``(markup: string) =
        // \genfrac{\{}{\}} is what turned this up: the } of \} was closing the group it sat in, so
        // every one of these was an "Illegal end, missing '}'".
        renders markup

    [<Fact>]
    member _.``an escaped brace does not close the group around it``() =
        Assert.Equal(widthOf @"\{", widthOf @"{\{}", 6)
