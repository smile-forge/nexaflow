namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils
open XamlMath.Atoms

// Two places the engine had quietly departed from TeX's own layout rules. Neither shows up against a
// specification - both came from putting our rendering beside a published paper's, formula by
// formula (tools/latex-corpus).
type TeXLayoutRuleTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    static let boxOf (markup: string) = (parse markup).RootAtom.CreateBox(environment)
    static let widthOf (markup: string) = (boxOf markup).Width
    static let heightOf (markup: string) = (boxOf markup).Height
    static let totalHeightOf (markup: string) = (boxOf markup).TotalHeight

    // ── where an integral's limits go ────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\int")>]
    [<InlineData(@"\oint")>]
    member _.``an integral sets its limits beside it``(operator: string) =
        // TeX gives an integral \nolimits by default and \sum \limits, in every style - which is why
        // \int_0^\infty reads the way it does in every published paper. Setting them beside makes the
        // operator wider and shorter than stacking them would.
        let beside = operator + @"_{0}^{n}"
        let stacked = operator + @"\limits_{0}^{n}"
        Assert.True(widthOf beside > widthOf stacked, "side-set limits widen the operator")
        Assert.True(totalHeightOf beside < totalHeightOf stacked, "and stop it growing upwards")

    [<Fact>]
    member _.``a sum still stacks its limits``() =
        Assert.True(totalHeightOf @"\sum_{0}^{n}" > totalHeightOf @"\int_{0}^{n}")

    [<Fact>]
    member _.``an operator with no limits at all is still the display size``() =
        // Choosing the display form of the glyph happens where the scripts are set, so an operator
        // that has none must not take the short way out and come out at the size of the letters.
        Assert.True(totalHeightOf @"\int" > totalHeightOf @"\textstyle\int",
                    "a lone integral should take the display glyph")
        // The same glyph as one carrying limits, give or take how far the limits themselves hang.
        Assert.True(
            totalHeightOf @"\int" > 0.95 * totalHeightOf @"\int_{0}",
            "a lone integral should be the glyph an integral with limits uses")

    // ── a row separator at the end of a matrix ───────────────────────────────────

    [<Theory>]
    [<InlineData(@"\begin{matrix} a & b \\ c & d \end{matrix}")>]
    [<InlineData(@"\begin{pmatrix} a & b \\ c & d \end{pmatrix}")>]
    [<InlineData(@"\begin{cases} a & b \\ c & d \end{cases}")>]
    member _.``a trailing row separator closes the last row rather than opening another``
        (markup: string) =
        // "a & b \\ c & d \\" is a normal way to write a matrix out. The empty row it used to leave
        // behind was a blank line the grid grew to fit - and the delimiters grew again to cover that,
        // which is what left the last row sitting near the middle of its brackets.
        let trailing = markup.Replace(@" \end", @" \\ \end")
        Assert.Equal(heightOf markup, heightOf trailing, 6)
        Assert.Equal(totalHeightOf markup, totalHeightOf trailing, 6)

    // ── a script on an accented base ─────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\dot{C}^{\mu}")>]
    [<InlineData(@"\hat{a}_{b\sigma}")>]
    [<InlineData(@"\vec{v}^{2}")>]
    [<InlineData(@"\tilde{C}^{\mu}")>]
    member _.``a script after an accent attaches to the accented atom``(markup: string) =
        // It used to fall through to the parser's "there is no base to hand" path, which hangs the
        // script off an empty box standing next to the accented atom - so it was set at the height of
        // nothing at all, and came out level with the letter instead of with the accent.
        let scripts = Assert.IsType<ScriptsAtom>((parse markup).RootAtom)
        Assert.IsType<AccentedAtom>(scripts.BaseAtom) |> ignore

    [<Fact>]
    member _.``a superscript reaches above the accent below it``() =
        // The accent is part of the nucleus the script sits on, so the script clears it. Were it not,
        // the accent would still be the tallest thing in the box and adding the script would not
        // change the height at all.
        Assert.True(
            heightOf @"\dot{C}^{\mu}" > heightOf @"\dot{C}",
            "a script on an accented base should reach past the accent")
        Assert.True(
            heightOf @"\dot{C}^{\mu}" > heightOf @"C^{\mu}",
            "and sit higher than the same script on a bare letter")
