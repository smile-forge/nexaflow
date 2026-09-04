namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils
open XamlMath
open XamlMath.Atoms
open XamlMath.Rendering

// Tests for the last three constructs the reference used to list as unsupported:
//   \begin{array}{…} with per-column alignment, | rules and \hline
//   \operatorname / \operatorname*
//   \mbox
type ArrayAndOperatorTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    static let renders (markup: string) =
        let root = (parse markup).RootAtom
        Assert.NotNull(root)
        Assert.NotNull(root.CreateBox(environment))

    static let widthOf (markup: string) = (parse markup).RootAtom.CreateBox(environment).Width

    /// The x offset of every rule an array draws: the rules are the only rectangles in one.
    static let ruleOffsets (markup: string) =
        let geometry = System.Windows.Media.GeometryGroup()
        let renderer = GeometryElementRenderer(geometry, 1.0) :> IElementRenderer
        renderer.RenderElement((parse markup).RootAtom.CreateBox(environment), 0.0, 0.0)
        geometry.Children
        |> Seq.choose (fun g ->
            match g with
            | :? System.Windows.Media.RectangleGeometry as r -> Some r.Rect.Left
            | _ -> None)
        |> List.ofSeq

    // ── array ────────────────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\begin{array}{c} a \end{array}")>]
    [<InlineData(@"\begin{array}{cc} a & b \\ c & d \end{array}")>]
    [<InlineData(@"\begin{array}{lcr} a & b & c \\ dd & ee & ff \end{array}")>]
    [<InlineData(@"\begin{array}{cc|c} 1 & 0 & 3 \\ 0 & 1 & 4 \end{array}")>]
    [<InlineData(@"\begin{array}{|c|c|} a & b \\ c & d \end{array}")>]
    [<InlineData(@"\begin{array}{cc} \hline a & b \\ \hline c & d \\ \hline \end{array}")>]
    [<InlineData(@"\left[\begin{array}{cc|c} 1 & 0 & 3 \\ 0 & 1 & 4 \end{array}\right]")>]
    [<InlineData(@"\begin{array}{c} \begin{array}{c} a \end{array} \end{array}")>]
    member _.``arrays render``(markup: string) = renders markup

    [<Fact>]
    member _.``an array is a matrix carrying its column spec``() =
        let atom = Assert.IsType<MatrixAtom>((parse @"\begin{array}{lcr} a & b & c \end{array}").RootAtom)
        Assert.NotNull(atom.ColumnSpec)
        Assert.Equal(3, atom.ColumnSpec.Alignments.Count)
        Assert.Equal(TexAlignment.Left, atom.ColumnSpec.Alignments.[0])
        Assert.Equal(TexAlignment.Center, atom.ColumnSpec.Alignments.[1])
        Assert.Equal(TexAlignment.Right, atom.ColumnSpec.Alignments.[2])

    [<Fact>]
    member _.``the preamble decides where a short cell sits in its column``() =
        // A narrow cell above a wide one has slack in its column, and l, c and r spend that slack
        // differently: after the cell, either side of it, or before it. The gap laid in front of the
        // cell is exactly that decision, so measure it rather than the ink (the widest cell fills
        // the column whatever the alignment, and would hide the difference).
        let gapBeforeFirstCell (markup: string) =
            let box = (parse markup).RootAtom.CreateBox(environment)
            box.Children.[0].Children.[0].Width      // first row, the strut leading its first cell

        let body = @" i \\ mmm \end{array}"
        let l = gapBeforeFirstCell (@"\begin{array}{l}" + body)
        let c = gapBeforeFirstCell (@"\begin{array}{c}" + body)
        let r = gapBeforeFirstCell (@"\begin{array}{r}" + body)
        Assert.True(l < c, "a left-aligned cell should sit further left than a centred one")
        Assert.True(c < r, "a centred cell should sit further left than a right-aligned one")

    [<Fact>]
    member _.``a rule in the preamble is drawn``() =
        let inkCount (markup: string) =
            let geometry = System.Windows.Media.GeometryGroup()
            let renderer = GeometryElementRenderer(geometry, 1.0) :> IElementRenderer
            renderer.RenderElement((parse markup).RootAtom.CreateBox(environment), 0.0, 0.0)
            geometry.Children.Count

        let plain = inkCount @"\begin{array}{cc} a & b \end{array}"
        let ruled = inkCount @"\begin{array}{c|c} a & b \end{array}"
        Assert.True(ruled > plain, "the | should have added a rule to the drawing")

    [<Fact>]
    member _.``hline is recorded rather than laid out as a row``() =
        let ruled = Assert.IsType<MatrixAtom>(
                        (parse @"\begin{array}{c} \hline a \\ \hline b \\ \hline \end{array}").RootAtom)
        Assert.Equal(2, ruled.MatrixCells.Count)     // two rows, not five
        Assert.Equal(3, ruled.HorizontalRules.Count) // above, between, below

    [<Theory>]
    [<InlineData(@"\begin{array} a \end{array}")>]              // no preamble at all
    [<InlineData(@"\begin{array}{c@{x}c} a & b \end{array}")>]  // a preamble in a language we cannot read
    member _.``an array we cannot draw says so``(markup: string) =
        // Better a grid shown as the characters that asked for it than one quietly missing what was
        // asked for. It used to be an exception; it is a mark on the stretch now, and the reader sees
        // which stretch rather than a formula that has vanished.
        Assert.NotEmpty(undrawn markup)

    [<Theory>]
    [<InlineData(@"\begin{array}{} a \end{array}")>]      // not legal LaTeX, and the corpus has it anyway
    [<InlineData(@"\begin{array}{p} a \end{array}")>]     // a column type we cannot draw
    member _.``a preamble naming no column we can draw centres them instead``(markup: string) =
        // The one preamble fault that is recovered rather than marked. Refusing these bought nothing
        // and cost the whole formula: a reader handed `\begin{array}` in place of a matrix has been
        // given the worse of the two, and the cells already say how many columns there are.
        Assert.Empty(undrawn markup)
        renders markup

    // ── \operatorname ────────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\operatorname{argmax}")>]
    [<InlineData(@"\operatorname{argmin}_{x} f(x)")>]
    [<InlineData(@"\operatorname*{argmax}_{\theta} L(\theta)")>]
    [<InlineData(@"\operatorname{Tr}(A) = \sum_i a_{ii}")>]
    member _.``operatorname renders``(markup: string) = renders markup

    [<Fact>]
    member _.``operatorname is an operator, not just upright text``() =
        // The point of it: the name gets operator spacing and a following script becomes its limit.
        let atom = Assert.IsType<BigOperatorAtom>((parse @"\operatorname{argmax}").RootAtom)
        Assert.Equal(TexAtomType.BigOperator, atom.GetLeftType())

    [<Fact>]
    member _.``a script after operatorname becomes its limit``() =
        let atom = Assert.IsType<BigOperatorAtom>((parse @"\operatorname*{argmax}_{x}").RootAtom)
        Assert.NotNull(atom.LowerLimitAtom)

    // ── \mbox ────────────────────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\mbox{hello}")>]
    [<InlineData(@"x + \mbox{some text} = y")>]
    member _.``mbox renders``(markup: string) = renders markup

    [<Fact>]
    member _.``mbox is text under another name``() =
        Assert.Equal(widthOf @"\text{a few words}", widthOf @"\mbox{a few words}", 6)

    [<Fact>]
    member _.``mbox keeps its spaces``() =
        Assert.True(widthOf @"\mbox{a b}" > widthOf @"\mbox{ab}")

    // ── where a vertical rule lands ──────────────────────────────────────────────

    [<Fact>]
    member _.``a vertical rule sits on the column boundary, whichever row is widest``() =
        // The rule is placed from the first row, but a column is as wide as its widest cell in any
        // row. A first row that is not the widest must not drag the rule off the boundary with it.
        let narrowFirst = ruleOffsets @"\begin{array}{c|c} a & b \\ xxxx & d \end{array}"
        let widestFirst = ruleOffsets @"\begin{array}{c|c} xxxx & b \\ a & d \end{array}"

        Assert.Equal(1, narrowFirst.Length)
        Assert.Equal(widestFirst.Head, narrowFirst.Head, 6)
        Assert.InRange(
            narrowFirst.Head,
            widthOf @"xxxx",
            widthOf @"\begin{array}{c|c} a & b \\ xxxx & d \end{array}")
