namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils
open XamlMath
open XamlMath.Atoms
open XamlMath.Rendering

// Tests for the LaTeX commands added on top of the JMathTeX command set:
//   style switches : \displaystyle, \textstyle, \scriptstyle, \scriptscriptstyle
//   annotations    : \overset, \underset, \stackrel
//   extent         : \phantom, \hphantom, \vphantom, \smash, \mathllap, \mathrlap, \mathclap
//   frames         : \boxed, \fbox
//   font switches  : \mathbb, \mathbf, \mathsf, \mathtt, \mathfrak, \mathscr and the \text* family
//   arrows         : \xrightarrow, \xleftarrow, \xleftrightarrow, \xRightarrow, \xLeftarrow,
//                    \xLeftrightarrow, \xmapsto, \impliedby
//   environments   : matrix, cases, aligned, split, gather, gathered, smallmatrix and the starred forms
type AdditionalCommandsTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    static let renderBounds (markup: string) =
        let box = (parseRoot markup).CreateBox(environment)
        let geometry = System.Windows.Media.GeometryGroup()
        let renderer = GeometryElementRenderer(geometry, 1.0) :> IElementRenderer
        renderer.RenderElement(box, 0.0, 0.0)
        box, geometry

    [<Theory>]
    [<InlineData(@"\displaystyle x")>]
    [<InlineData(@"\textstyle x")>]
    [<InlineData(@"\scriptstyle x")>]
    [<InlineData(@"\scriptscriptstyle x")>]
    [<InlineData(@"\displaystyle\sum_{i=1}^{n} i")>]
    [<InlineData(@"\displaystyle\frac{a}{b}")>]
    member _.``style switches are parsed as a StyleAtom``(markup: string) =
        Assert.IsType<StyleAtom>(parseRoot markup) |> ignore

    [<Fact>]
    member _.``a style switch takes the rest of its group, not just the next element``() =
        // The limits have to end up inside the switch: display style is what puts them above and below the
        // operator, and only a big operator that carries its own limits can be drawn that way.
        let atom = Assert.IsType<StyleAtom>(parseRoot @"\displaystyle\sum_{i=1}^{n}")
        let operatorAtom = Assert.IsType<BigOperatorAtom>(atom.BaseAtom)
        Assert.NotNull(operatorAtom.LowerLimitAtom)
        Assert.NotNull(operatorAtom.UpperLimitAtom)

    [<Fact>]
    member _.``a style switch inside a group ends with the group``() =
        // The "b" is outside the braces, so it keeps the outer style and the root is a row, not the switch.
        Assert.IsType<RowAtom>(parseRoot @"{\scriptstyle a} b") |> ignore

    [<Theory>]
    [<InlineData(@"\overset{a}{b}")>]
    [<InlineData(@"\underset{a}{b}")>]
    member _.``overset and underset are parsed as an UnderOverAtom``(markup: string) =
        Assert.IsType<UnderOverAtom>(parseRoot markup) |> ignore

    [<Fact>]
    member _.``underset sets its annotation as close as overset does``() =
        // The gap the annotation is held at is the only thing that differs between the two, and it is
        // asked for in the same unit, so an annotation set below must sit exactly as far from the base
        // as the same annotation set above. (It did not: the under gap was built with the *over* unit,
        // which an under-only atom leaves at the default em — an 18x gap for a value meant as mu.)
        let overBox  = (parseRoot @"\overset{a}{X}").CreateBox(environment)
        let underBox = (parseRoot @"\underset{a}{X}").CreateBox(environment)
        let bare     = (parseRoot @"X").CreateBox(environment)

        let above = overBox.Height - bare.Height
        let below = underBox.Depth - bare.Depth
        Assert.Equal(above, below, 6)

    [<Fact>]
    member _.``stackrel is typed as a relation``() =
        let atom = Assert.IsType<TypedAtom>(parseRoot @"\stackrel{f}{\rightarrow}")
        Assert.Equal(TexAtomType.Relation, atom.GetLeftType())
        Assert.Equal(TexAtomType.Relation, atom.GetRightType())

    [<Theory>]
    [<InlineData(@"\phantom{x}")>]
    [<InlineData(@"\hphantom{x}")>]
    [<InlineData(@"\vphantom{x}")>]
    member _.``the phantom family is parsed as a PhantomAtom``(markup: string) =
        Assert.IsType<PhantomAtom>(parseRoot markup) |> ignore

    [<Fact>]
    member _.``smash keeps the width and drops the height``() =
        let box = (parseRoot @"\smash{\frac{a}{b}}").CreateBox(environment)
        let reference = (parseRoot @"\frac{a}{b}").CreateBox(environment)
        Assert.Equal(reference.Width, box.Width)
        Assert.Equal(0.0, box.Height)
        Assert.Equal(0.0, box.Depth)

    [<Theory>]
    [<InlineData(@"\mathllap{x}")>]
    [<InlineData(@"\mathrlap{x}")>]
    [<InlineData(@"\mathclap{x}")>]
    [<InlineData(@"\llap{x}")>]
    [<InlineData(@"\rlap{x}")>]
    member _.``the lap family drops the width and keeps the height``(markup: string) =
        Assert.IsType<LapAtom>(parseRoot markup) |> ignore
        let box = (parseRoot markup).CreateBox(environment)
        Assert.Equal(0.0, box.Width)
        Assert.True(box.Height > 0.0)

    [<Theory>]
    [<InlineData(@"\overbrace{a+b}")>]
    [<InlineData(@"\overbrace{a+b}^{n}")>]
    [<InlineData(@"\overbrace{a+b}^n")>]
    [<InlineData(@"\underbrace{a+b}")>]
    [<InlineData(@"\underbrace{a+b}_{n}")>]
    [<InlineData(@"\underbrace{\overbrace{a+b}^{n}+c}_{m}")>]
    member _.``braces are parsed as an OverUnderDelimiter``(markup: string) =
        Assert.IsType<OverUnderDelimiter>(parseRoot markup) |> ignore

    [<Fact>]
    member _.``a brace takes the script that follows it as its label``() =
        // \overbrace is an operator: the "n" belongs above the brace, so the command has to read it
        // before the parser attaches it as an ordinary superscript to the right.
        Assert.IsType<OverUnderDelimiter>(parseRoot @"\overbrace{a+b}^{n}") |> ignore
        Assert.IsType<OverUnderDelimiter>(parseRoot @"\underbrace{a+b}_{n}") |> ignore

        // A script on the other side is not the brace's label, and stays an ordinary script.
        Assert.IsType<ScriptsAtom>(parseRoot @"\overbrace{a+b}_{n}") |> ignore

    [<Fact>]
    member _.``a brace with no label renders``() =
        // The delimiter can stand alone, and the script box it does not have must not be reached for.
        let _, geometry = renderBounds @"\overbrace{a+b}"
        Assert.NotEmpty(geometry.Children)

    [<Theory>]
    [<InlineData(@"\overbrace{a+b+c}^{\text{three terms}}")>]
    [<InlineData(@"\underbrace{d+e}_{\text{two more}}")>]
    [<InlineData(@"\overbrace{a+b}")>]
    member _.``a brace is drawn across its base, not beside it``(markup: string) =
        // A label wider than the base widens the whole atom, and the brace is centred in it. The
        // delimiter is padded to reach that width — with the leftover, not with the width itself,
        // which would push the brace half a width to the right and out of its own box.
        let box, geometry = renderBounds markup
        // A brace overhangs its span a little by design, so the bound is loose; the bug it guards
        // against slid the brace by half a width, which is nowhere near this.
        let tolerance = box.Width * 0.25
        Assert.True(geometry.Bounds.Left > -tolerance,
                    $"ink starts at {geometry.Bounds.Left}, left of the box (width {box.Width})")
        Assert.True(geometry.Bounds.Right < box.Width + tolerance,
                    $"ink reaches {geometry.Bounds.Right}, past the box width {box.Width}")

    [<Theory>]
    [<InlineData(@"\substack{a \\ b}")>]
    [<InlineData(@"\substack{i < j \\ j < k \\ k < l}")>]
    [<InlineData(@"\sum_{\substack{i < j \\ j < k}} a_{ij}")>]
    member _.``substack parses to a non-null root atom``(markup: string) =
        Assert.NotNull(parseRoot markup)

    [<Fact>]
    member _.``substack stacks its lines in script size, set solid``() =
        let atom   = Assert.IsType<StyleAtom>(parseRoot @"\substack{a \\ b}")
        let matrix = Assert.IsType<MatrixAtom>(atom.BaseAtom)
        Assert.Equal(TexStyle.Script, atom.TargetStyle)
        Assert.Equal(2, matrix.MatrixCells.Count)
        Assert.True(matrix.VerticalPadding < MatrixAtom.DefaultPadding,
                    "substack lines should sit closer together than table rows")

    [<Theory>]
    [<InlineData(@"\boxed{x}")>]
    [<InlineData(@"\fbox{x}")>]
    [<InlineData(@"\boxed{\frac{a}{b}}")>]
    member _.``boxed is parsed as a BoxedAtom``(markup: string) =
        Assert.IsType<BoxedAtom>(parseRoot markup) |> ignore

    [<Fact>]
    member _.``a frame is wider and taller than what it frames``() =
        let framed = (parseRoot @"\boxed{x}").CreateBox(environment)
        let bare = (parseRoot @"x").CreateBox(environment)
        Assert.True(framed.Width > bare.Width)
        Assert.True(framed.Height > bare.Height)

    [<Theory>]
    [<InlineData(@"\xrightarrow{f}")>]
    [<InlineData(@"\xleftarrow{f}")>]
    [<InlineData(@"\xleftrightarrow{f}")>]
    [<InlineData(@"\xRightarrow{f}")>]
    [<InlineData(@"\xLeftarrow{f}")>]
    [<InlineData(@"\xLeftrightarrow{f}")>]
    [<InlineData(@"\xmapsto{f}")>]
    [<InlineData(@"\xrightarrow[g]{f}")>]
    member _.``extensible arrows are parsed as an ExtensibleArrowAtom``(markup: string) =
        Assert.IsType<ExtensibleArrowAtom>(parseRoot markup) |> ignore

    [<Fact>]
    member _.``an extensible arrow grows to fit its label``() =
        let short = (parseRoot @"\xrightarrow{f}").CreateBox(environment)
        let long = (parseRoot @"\xrightarrow{f \circ g \circ h}").CreateBox(environment)
        Assert.True(long.Width > short.Width)

    [<Theory>]
    [<InlineData(@"\mathbb{R}")>]
    [<InlineData(@"\mathbf{x}")>]
    [<InlineData(@"\mathsf{x}")>]
    [<InlineData(@"\mathtt{x}")>]
    [<InlineData(@"\mathfrak{g}")>]
    [<InlineData(@"\mathscr{L}")>]
    [<InlineData(@"\textrm{x}")>]
    [<InlineData(@"\textbf{x}")>]
    [<InlineData(@"\textit{x}")>]
    [<InlineData(@"\textsf{x}")>]
    [<InlineData(@"\texttt{x}")>]
    [<InlineData(@"\textsc{x}")>]
    member _.``font switches parse to a non-null root atom``(markup: string) =
        Assert.NotNull(parseRoot markup)

    [<Fact>]
    member _.``a text font switch keeps its spaces``() =
        // The argument of a \text* command is text, not maths: the space between the words survives.
        let atom = Assert.IsType<RowAtom>(parseRoot @"\textbf{a b}")
        Assert.Contains(atom.Elements, fun e -> e :? SpaceAtom)

    [<Theory>]
    [<InlineData(@"\begin{matrix}a & b \\ c & d\end{matrix}")>]
    [<InlineData(@"\begin{smallmatrix}a & b \\ c & d\end{smallmatrix}")>]
    [<InlineData(@"\begin{cases}a & x > 0 \\ b & x \leq 0\end{cases}")>]
    [<InlineData(@"\begin{aligned}a &= b \\ c &= d\end{aligned}")>]
    [<InlineData(@"\begin{split}a &= b \\ c &= d\end{split}")>]
    [<InlineData(@"\begin{align*}a &= b \\ c &= d\end{align*}")>]
    [<InlineData(@"\begin{gather}a \\ b\end{gather}")>]
    [<InlineData(@"\begin{gather*}a \\ b\end{gather*}")>]
    [<InlineData(@"\begin{gathered}a \\ b\end{gathered}")>]
    member _.``environments parse to a non-null root atom``(markup: string) =
        Assert.NotNull(parseRoot markup)

    [<Fact>]
    member _.``smallmatrix is set in script style``() =
        let atom = Assert.IsType<StyleAtom>(parseRoot @"\begin{smallmatrix}a & b\end{smallmatrix}")
        Assert.Equal(TexStyle.Script, atom.TargetStyle)

    [<Theory>]
    [<InlineData(@"\implies")>]
    [<InlineData(@"\impliedby")>]
    [<InlineData(@"\iff")>]
    [<InlineData(@"\Longleftarrow")>]
    [<InlineData(@"\textcolor{red}{x}")>]
    [<InlineData(@"\color{red}{x}")>]
    member _.``implication arrows and textcolor parse to a non-null root atom``(markup: string) =
        Assert.NotNull(parseRoot markup)

    [<Theory>]
    [<InlineData(@"\displaystyle\sum_{i=1}^{n} i")>]
    [<InlineData(@"\overset{a}{b}")>]
    [<InlineData(@"\underset{a}{b}")>]
    [<InlineData(@"\stackrel{f}{\rightarrow}")>]
    [<InlineData(@"\phantom{xyz}")>]
    [<InlineData(@"\hphantom{xyz}")>]
    [<InlineData(@"\vphantom{xyz}")>]
    [<InlineData(@"\smash{x}")>]
    [<InlineData(@"\mathclap{xyz}")>]
    [<InlineData(@"\boxed{\frac{a}{b}}")>]
    [<InlineData(@"\mathbb{R}")>]
    [<InlineData(@"\textbf{a b}")>]
    [<InlineData(@"\textsc{abc}")>]
    [<InlineData(@"\xrightarrow[g]{f}")>]
    [<InlineData(@"\xmapsto{f}")>]
    [<InlineData(@"\xLeftrightarrow{f}")>]
    [<InlineData(@"\begin{smallmatrix}a & b \\ c & d\end{smallmatrix}")>]
    [<InlineData(@"\begin{gathered}a \\ b\end{gathered}")>]
    [<InlineData(@"\begin{cases}a & x > 0 \\ b & x \leq 0\end{cases}")>]
    [<InlineData(@"\impliedby")>]
    member _.``a box is created for the command``(markup: string) =
        Assert.NotNull((parseRoot markup).CreateBox(environment))
