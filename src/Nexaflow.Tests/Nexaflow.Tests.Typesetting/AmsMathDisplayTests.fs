namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.Utils
open XamlMath.Atoms
open XamlMath.Exceptions
open XamlMath.Rendering

// The rest of what the amsmath survey (tools/amsmath-coverage) turned up: the document-level
// constructs, which are read and dropped rather than rejected; the limit controls; \hdotsfor, the
// one cell in a matrix that is drawn to a width instead of setting one; the \big family of set-size
// delimiters; and \genfrac, the general fraction the others are spelled with.
type AmsMathDisplayTests() =
    static do initializeFontResourceLoading()

    static let environment = WpfTeXEnvironment.Create()

    static let boxOf (markup: string) = (parse markup).RootAtom.CreateBox(environment)
    static let widthOf (markup: string) = (boxOf markup).Width
    static let heightOf (markup: string) = (boxOf markup).TotalHeight

    /// What the formula actually puts on the page, as opposed to the space it reserves.
    static let inkBounds (markup: string) =
        let geometry = System.Windows.Media.GeometryGroup()
        let renderer = GeometryElementRenderer(geometry, 1.0) :> IElementRenderer
        renderer.RenderElement(boxOf markup, 0.0, 0.0)
        geometry.Bounds

    /// How many rules a formula draws: they are the only rectangles in one.
    static let ruleCount (markup: string) =
        let geometry = System.Windows.Media.GeometryGroup()
        let renderer = GeometryElementRenderer(geometry, 1.0) :> IElementRenderer
        renderer.RenderElement(boxOf markup, 0.0, 0.0)
        geometry.Children
        |> Seq.filter (fun g -> g :? System.Windows.Media.RectangleGeometry)
        |> Seq.length

    static let renders (markup: string) =
        Assert.NotNull((parse markup).RootAtom)
        Assert.NotNull(boxOf markup)

    // ── document-level commands ──────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"x = y \tag{1}")>]
    [<InlineData(@"x = y \tag*{\dagger}")>]
    [<InlineData(@"x = y \notag")>]
    [<InlineData(@"x = y \nonumber")>]
    [<InlineData(@"x = y \label{eq:one}")>]
    [<InlineData(@"x = y \eqref{eq:one}")>]
    [<InlineData(@"x = y \raisetag{-6pt}")>]
    [<InlineData(@"\numberwithin{equation}{section} x = y")>]
    [<InlineData(@"\allowdisplaybreaks x = y")>]
    [<InlineData(@"x = y \displaybreak[0]")>]
    [<InlineData(@"\DeclareMathOperator{\argmax}{arg\,max} x = y")>]
    [<InlineData(@"\DeclarePairedDelimiter{\abs}{\lvert}{\rvert} x = y")>]
    [<InlineData(@"\accentedsymbol{\dotB}{\dot{B}} x = y")>]
    member _.``a document-level command leaves nothing behind``(markup: string) =
        // Numbering, cross references, page breaks: there is no page here and nothing to number, so
        // the command has no work left to do. Rejecting it would cost the formula around it, which
        // is the part that can still be drawn.
        Assert.Equal(widthOf @"x = y", widthOf markup, 6)

    [<Fact>]
    member _.``a discarded command keeps the argument out of the formula``() =
        // The point of reading the argument rather than skipping the command name: nothing of
        // "arg\,max" or "eq:one" may leak into the output as ordinary letters.
        Assert.Equal(widthOf @"x", widthOf @"\DeclareMathOperator{\argmax}{arg\,max} x", 6)
        Assert.Equal(widthOf @"x", widthOf @"x \label{eq:one}", 6)

    [<Fact>]
    member _.``nobreakdash leaves the dash it was protecting``() =
        Assert.Equal(widthOf @"a-b", widthOf @"a\nobreakdash-b", 6)

    [<Fact>]
    member _.``shoveleft and shoveright keep their contents``() =
        // Unlike the rest, the argument here is maths: only the flush-to-margin goes.
        Assert.Equal(widthOf @"a + b", widthOf @"\shoveleft{a + b}", 6)
        Assert.Equal(widthOf @"a + b", widthOf @"\shoveright{a + b}", 6)

    [<Fact>]
    member _.``intertext does not typeset the text it interrupts with``() =
        let plain = @"\begin{aligned} a &= b \\ c &= d \end{aligned}"
        let interrupted = @"\begin{aligned} a &= b \\ \intertext{and hence} c &= d \end{aligned}"
        Assert.Equal(widthOf plain, widthOf interrupted, 6)
        Assert.Equal(heightOf plain, heightOf interrupted, 6)

    // ── display environments ─────────────────────────────────────────────────────

    [<Theory>]
    [<InlineData(@"\begin{equation} x = y \end{equation}")>]
    [<InlineData(@"\begin{equation*} x = y \end{equation*}")>]
    [<InlineData(@"\begin{subequations} \begin{equation} x = y \end{equation} \end{subequations}")>]
    member _.``a display environment is nothing more than its contents``(markup: string) =
        Assert.Equal(widthOf @"x = y", widthOf markup, 6)
        Assert.Equal((inkBounds @"x = y").Top, (inkBounds markup).Top, 6)

    [<Theory>]
    [<InlineData(@"\begin{alignat}{2} a &= b & c &= d \end{alignat}")>]
    [<InlineData(@"\begin{alignat*}{2} a &= b & c &= d \end{alignat*}")>]
    [<InlineData(@"\begin{xalignat}{2} a &= b & c &= d \end{xalignat}")>]
    [<InlineData(@"\begin{xxalignat}{2} a &= b & c &= d \end{xxalignat}")>]
    [<InlineData(@"\begin{alignedat}{2} a &= b & c &= d \end{alignedat}")>]
    member _.``the alignat column count is read, not typeset``(markup: string) =
        // The count exists to set inter-column spacing across a page of text. What it must not do is
        // come out as a digit at the front of the formula.
        Assert.Equal(widthOf @"\begin{aligned} a &= b & c &= d \end{aligned}", widthOf markup, 6)

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

    [<Theory>]
    [<InlineData(@"\textstyle\sum")>]
    [<InlineData(@"\displaystyle\sum")>]
    member _.``displaylimits asks for whatever the style would have chosen``(operator: string) =
        Assert.Equal(widthOf (operator + @"_{i} x"), widthOf (operator + @"\displaylimits_{i} x"), 6)
        Assert.Equal(heightOf (operator + @"_{i} x"), heightOf (operator + @"\displaylimits_{i} x"), 6)

    [<Fact>]
    member _.``the last limit control wins``() =
        Assert.Equal(
            widthOf @"\textstyle\sum\limits_{i} x",
            widthOf @"\textstyle\sum\nolimits\limits_{i} x",
            6)

    [<Fact>]
    member _.``a limit control is only a command after an operator``() =
        // Anywhere it could not mean anything it is left undrawn and set as its own characters,
        // rather than being quietly eaten and leaving a formula that looks like it was understood.
        Assert.Equal<string list>([ @"\limits" ], undrawn @"x\limits_{i}")

    // ── \hdotsfor ────────────────────────────────────────────────────────────────

    [<Fact>]
    member _.``hdotsfor parses to a cell that spans the columns asked for``() =
        Assert.Equal(2, Assert.IsType<HDotsForAtom>(parse(@"\hdotsfor{2}").RootAtom).ColumnSpan)
        Assert.Equal(3, Assert.IsType<HDotsForAtom>(parse(@"\hdotsfor[1.5]{3}").RootAtom).ColumnSpan)

    [<Fact>]
    member _.``hdotsfor is drawn across the columns it spans``() =
        // Cells with a width but no ink, so the only thing left to measure is the dots themselves.
        let across = @"\begin{matrix} \hspace{2em} & \hspace{2em} \\ \hdotsfor{2} \end{matrix}"
        let oneColumn = @"\begin{matrix} \hspace{2em} & \hspace{2em} \\ \hdotsfor{1} & \hspace{2em} \end{matrix}"
        let two = (inkBounds across).Width
        let one = (inkBounds oneColumn).Width

        // Same matrix either way, so the only thing that can move is how far the dots reach.
        Assert.Equal(widthOf across, widthOf oneColumn, 6)
        Assert.True(two > 1.6 * one, $"dots of {two} against {one}: the span is not being followed")
        Assert.True(two > 0.7 * widthOf across, $"dots of {two} do not reach across {widthOf across}")

    [<Fact>]
    member _.``a spanning cell does not decide how wide a column is``() =
        // \hdotsfor takes the width the other rows settled on; it never argues for one of its own.
        let sized = @"\begin{matrix} a & b \end{matrix}"
        Assert.Equal(widthOf sized, widthOf @"\begin{matrix} a & b \\ \hdotsfor{2} \end{matrix}", 6)

    [<Fact>]
    member _.``the hdotsfor spacing option spreads the dots out``() =
        // A wider gap fits fewer dots into the same span, so there is less ink across it.
        Assert.Equal(widthOf @"\hdotsfor{3}", widthOf @"\hdotsfor[4]{3}", 6)
        Assert.True((inkBounds @"\hdotsfor[2]{3}").Width < (inkBounds @"\hdotsfor{3}").Width)
        Assert.True((inkBounds @"\hdotsfor[4]{3}").Width < (inkBounds @"\hdotsfor[2]{3}").Width)

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

    [<Fact>]
    member _.``genfrac in parentheses with no rule is binom``() =
        // Which is exactly how amsmath spells \binom, \dbinom and \tbinom, so they have to agree.
        Assert.Equal(widthOf @"\binom{n}{k}", widthOf @"\genfrac{(}{)}{0pt}{}{n}{k}", 6)
        Assert.Equal(heightOf @"\binom{n}{k}", heightOf @"\genfrac{(}{)}{0pt}{}{n}{k}", 6)
        Assert.Equal(widthOf @"\dbinom{n}{k}", widthOf @"\genfrac{(}{)}{0pt}{0}{n}{k}", 6)
        Assert.Equal(widthOf @"\tbinom{n}{k}", widthOf @"\genfrac{(}{)}{0pt}{1}{n}{k}", 6)

    [<Fact>]
    member _.``the genfrac thickness is the rule that gets drawn``() =
        // 0pt is how \binom asks for no rule at all; a thicker one pushes the two halves apart.
        Assert.Equal(1, ruleCount @"\genfrac{}{}{}{}{n}{k}")
        Assert.Equal(0, ruleCount @"\genfrac{}{}{0pt}{}{n}{k}")
        Assert.True(heightOf @"\genfrac{}{}{}{}{n}{k}" < heightOf @"\genfrac{}{}{3pt}{}{n}{k}")

    [<Fact>]
    member _.``the genfrac style number picks the size``() =
        let widths = [ "0"; "1"; "2"; "3" ] |> List.map (fun n ->
            widthOf (@"\genfrac{(}{)}{0pt}{" + n + @"}{n}{k}"))
        match widths with
        | [ display; text; script; scriptScript ] ->
            Assert.True(display > text, "display should be larger than text")
            Assert.True(text > script, "text should be larger than script")
            // Script and scriptscript come out the same: a fraction sets both halves in
            // scriptscript either way, so with no rule to move there is nothing left to shrink.
            Assert.True(scriptScript <= script)
        | _ -> failwith "expected four widths"

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
