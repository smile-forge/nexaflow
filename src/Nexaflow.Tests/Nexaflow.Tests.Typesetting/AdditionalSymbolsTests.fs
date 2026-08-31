namespace WpfMath.Tests

open Xunit

open WpfMath.Parsers
open WpfMath.Tests.Utils
open XamlMath.Atoms

// Tests for the LaTeX symbols/commands added on top of the JMathTeX symbol set:
//   accents   : \mathring
//   over-arrows: \overrightarrow, \overleftarrow
//   arrows     : \mapsto, \longrightarrow, \longleftarrow, \hookrightarrow, \Longrightarrow, \Longleftrightarrow
//   dots       : \vdots, \ddots, \dots
//   symbols    : \S, \P, \notin, \varnothing, \nexists, \implies, \iff, \lhook, \rhook
type AdditionalSymbolsTests() =
    static do initializeFontResourceLoading()


    [<Theory>]
    [<InlineData(@"\mathring{a}")>]
    [<InlineData(@"\overrightarrow{AB}")>]
    [<InlineData(@"\overleftarrow{AB}")>]
    [<InlineData(@"\mapsto")>]
    [<InlineData(@"a \mapsto b")>]
    [<InlineData(@"\longrightarrow")>]
    [<InlineData(@"\longleftarrow")>]
    [<InlineData(@"\hookrightarrow")>]
    [<InlineData(@"\Longrightarrow")>]
    [<InlineData(@"\Longleftrightarrow")>]
    [<InlineData(@"\vdots")>]
    [<InlineData(@"\ddots")>]
    [<InlineData(@"\dots")>]
    [<InlineData(@"\S")>]
    [<InlineData(@"\P")>]
    [<InlineData(@"\notin")>]
    [<InlineData(@"\varnothing")>]
    [<InlineData(@"\nexists")>]
    [<InlineData(@"\implies")>]
    [<InlineData(@"\iff")>]
    [<InlineData(@"\lhook")>]
    [<InlineData(@"\rhook")>]
    [<InlineData(@"\begin{pmatrix}a & \cdots & b \\ \vdots & \ddots & \vdots \\ c & \cdots & d\end{pmatrix}")>]
    [<InlineData(@"a\quad b")>]
    [<InlineData(@"a\qquad b")>]
    [<InlineData(@"a\ b")>]
    [<InlineData(@"a~b")>]
    [<InlineData(@"a\hspace{2em}b")>]
    [<InlineData(@"a\hspace{-3pt}b")>]
    member _.``Command parses to a non-null root atom``(markup: string) =
        Assert.NotNull(parseRoot markup)

    [<Theory>]
    [<InlineData(@"\quad")>]
    [<InlineData(@"\qquad")>]
    [<InlineData(@"\ ")>]
    [<InlineData(@"~")>]
    [<InlineData(@"\hspace{2em}")>]
    [<InlineData(@"\hspace{20pt}")>]
    [<InlineData(@"\hspace{1cm}")>]
    [<InlineData(@"\hspace{-3pt}")>]
    [<InlineData(@"\hspace*{2em}")>]
    member _.``spacing commands are parsed as a SpaceAtom``(markup: string) =
        Assert.IsType<SpaceAtom>(parseRoot markup) |> ignore

    [<Theory>]
    [<InlineData(@"\hspace{2xyz}")>]
    [<InlineData(@"\hspace{abc}")>]
    member _.``hspace with an invalid length is set as its own characters``(markup: string) =
        Assert.NotEmpty(undrawn markup)

    [<Fact>]
    member _.``mathring is parsed as an accent``() =
        Assert.IsType<AccentedAtom>(parseRoot @"\mathring{a}") |> ignore

    [<Theory>]
    [<InlineData(@"\overrightarrow{AB}")>]
    [<InlineData(@"\overleftarrow{AB}")>]
    member _.``over-arrows are parsed as an OverArrowAtom``(markup: string) =
        Assert.IsType<OverArrowAtom>(parseRoot markup) |> ignore

    [<Theory>]
    [<InlineData(@"\vdots")>]
    [<InlineData(@"\ddots")>]
    member _.``vertical and diagonal dots are parsed as a DotsAtom``(markup: string) =
        Assert.IsType<DotsAtom>(parseRoot markup) |> ignore

    [<Theory>]
    [<InlineData(@"\S")>]
    [<InlineData(@"\P")>]
    [<InlineData(@"\varnothing")>]
    [<InlineData(@"\lhook")>]
    [<InlineData(@"\rhook")>]
    member _.``glyph-backed symbols are parsed as a SymbolAtom``(markup: string) =
        Assert.IsType<SymbolAtom>(parseRoot markup) |> ignore

    // amssymb negated relations (composed with \not) and synonyms.
    [<Theory>]
    [<InlineData(@"\nless")>]
    [<InlineData(@"\ngtr")>]
    [<InlineData(@"\nleq")>]
    [<InlineData(@"\ngeq")>]
    [<InlineData(@"\nleqslant")>]
    [<InlineData(@"\ngeqslant")>]
    [<InlineData(@"\nleqq")>]
    [<InlineData(@"\ngeqq")>]
    [<InlineData(@"\nprec")>]
    [<InlineData(@"\nsucc")>]
    [<InlineData(@"\npreceq")>]
    [<InlineData(@"\nsucceq")>]
    [<InlineData(@"\nsim")>]
    [<InlineData(@"\ncong")>]
    [<InlineData(@"\nvdash")>]
    [<InlineData(@"\nvDash")>]
    [<InlineData(@"\nVdash")>]
    [<InlineData(@"\nmid")>]
    [<InlineData(@"\nparallel")>]
    [<InlineData(@"\nsubseteq")>]
    [<InlineData(@"\nsupseteq")>]
    [<InlineData(@"\nsubseteqq")>]
    [<InlineData(@"\nsupseteqq")>]
    [<InlineData(@"\ntriangleleft")>]
    [<InlineData(@"\ntriangleright")>]
    [<InlineData(@"\ntrianglelefteq")>]
    [<InlineData(@"\ntrianglerighteq")>]
    [<InlineData(@"\nleftarrow")>]
    [<InlineData(@"\nrightarrow")>]
    [<InlineData(@"\nLeftarrow")>]
    [<InlineData(@"\nRightarrow")>]
    [<InlineData(@"\nleftrightarrow")>]
    [<InlineData(@"\nLeftrightarrow")>]
    [<InlineData(@"\doublecup")>]
    [<InlineData(@"\doublecap")>]
    [<InlineData(@"\restriction")>]
    [<InlineData(@"\Doteq")>]
    [<InlineData(@"\llless")>]
    [<InlineData(@"\gggtr")>]
    member _.``amssymb symbols parse to a non-null root atom``(markup: string) =
        Assert.NotNull(parseRoot markup)

    // Fraction commands.
    [<Theory>]
    [<InlineData(@"\dfrac{a}{b}")>]
    [<InlineData(@"\tfrac{a}{b}")>]
    [<InlineData(@"\cfrac{a}{b}")>]
    [<InlineData(@"\cfrac[l]{a}{b}")>]
    [<InlineData(@"\cfrac[r]{a}{b}")>]
    [<InlineData(@"\cfrac{1}{2+\cfrac{1}{3}}")>]
    member _.``dfrac tfrac cfrac are parsed as a FractionAtom``(markup: string) =
        Assert.IsType<FractionAtom>(parseRoot markup) |> ignore

    [<Theory>]
    [<InlineData(@"\nicefrac{a}{b}")>]
    [<InlineData(@"\sfrac{a}{b}")>]
    [<InlineData(@"\nicefrac{1}{2}")>]
    member _.``nicefrac and sfrac are parsed as a SlashFractionAtom``(markup: string) =
        Assert.IsType<SlashFractionAtom>(parseRoot markup) |> ignore

    // Multiple integrals and modulo operators.
    [<Theory>]
    [<InlineData(@"\iint")>]
    [<InlineData(@"\iiint")>]
    [<InlineData(@"\iiiint")>]
    [<InlineData(@"\idotsint")>]
    [<InlineData(@"\oiint")>]
    [<InlineData(@"\oiiint")>]
    [<InlineData(@"\iint_D f")>]
    [<InlineData(@"a \bmod b")>]
    [<InlineData(@"a \equiv b \pmod{n}")>]
    [<InlineData(@"x \pod{p}")>]
    member _.``integrals and modulo parse to a non-null root atom``(markup: string) =
        Assert.NotNull(parseRoot markup)
