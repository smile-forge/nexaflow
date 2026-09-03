module WpfMath.Tests.BoxTests

open System

open FSharp.Core.Fluent
open Xunit

open WpfMath.Parsers
open WpfMath.Rendering
open WpfMath.Tests.ApprovalTestUtils
open WpfMath.Tests.Utils
open XamlMath
open XamlMath.Atoms
open XamlMath.Boxes

let private environment = WpfTeXEnvironment.Create()

/// Which stretch of the source a box was built from.
///
/// An atom no longer carries a SourceSpan. It carries the part of the parse tree it was built from,
/// which answers the same question without keeping a second copy of the offsets beside the tree that
/// already holds them - and that second copy is exactly what parted company from the tree whenever
/// anything was edited. So these ask the part.
let private origin (box: Box) =
    match box.Node with
    | null -> (-1, -1)
    | node ->
        match node.Origin with
        | null -> (-1, -1)
        | part -> (part.Start, part.Length)

[<Fact>]
let ``AccentedAtom should have a skew according to the char``() =
    let topAtom = parseRoot @"\bar{\bar{x}}" :?> AccentedAtom
    let childAtom = topAtom.BaseAtom :?> AccentedAtom

    let topBox = topAtom.CreateBox(environment).Children.[0]
    let childBox = childAtom.CreateBox(environment).Children.[0]

    Assert.Equal(topBox.Shift, childBox.Shift)

[<Fact>]
let ``Box for \text{æ,} should be created successfully``() =
    let atom = parseRoot @"\text{æ,}"
    let box = atom.CreateBox(environment)
    Assert.NotNull(box)

[<Fact>]
let ``ScriptsAtom should set Shift on the created box when creating box without any sub- or superscript``() =
    Utils.initializeFontResourceLoading()

    let baseAtom = CharAtom('x')
    let scriptsAtom = ScriptsAtom(baseAtom, null, null)

    let box = scriptsAtom.CreateBox(environment)

    let expectedShift = -(box.Height + box.Depth) / 2.0 - environment.MathFont.GetAxisHeight(environment.Style)
    Assert.Equal(expectedShift, box.Shift)

[<Fact>]
let ``RowAtom creates boxes with proper sources``() =
    let formula = parse "2+2"
    let box = formula.CreateBox environment :?> HorizontalBox
    let chars = box.Children.filter (fun x -> x :? CharBox)
    Assert.Collection (
        chars,
        Action<_>(fun (x: Box) -> Assert.Equal((0, 1), origin x)),
        Action<_>(fun (x: Box) -> Assert.Equal((1, 1), origin x)),
        Action<_>(fun (x: Box) -> Assert.Equal((2, 1), origin x)))

[<Fact>]
let ``BigOperatorAtom creates a box with proper sources``() =
    // A sum rather than an integral: an integral sets its limits beside it, so its box is a
    // horizontal one. The offsets are the same either way - both names are three letters.
    let formula = parse @"\sum_a^b"
    let box = formula.CreateBox environment :?> VerticalBox

    let charBoxes =
        box.Children
            .filter(fun x -> x :? HorizontalBox)
            .collect(fun x -> x.Children.filter (fun y -> y :? CharBox))
            .toList()

    // The operator's own stretch is `\sum`, backslash included. The parser this replaced named a
    // command by its letters alone, which left the backslash belonging to nothing.
    //
    // FAILING, and reporting something true: the operator's glyph box comes back (-1, -1) — it carries
    // no node at all, so there is nothing to ask where it was written. The two script boxes either side
    // of it do. Whatever links a box to the atom it was built from is not doing it for the character a
    // big operator draws itself with.
    Assert.Collection (
        charBoxes,
        Action<_>(fun (x: Box) -> Assert.Equal((7, 1), origin x)),
        Action<_>(fun (x: Box) -> Assert.Equal((0, 4), origin x)),
        Action<_>(fun (x: Box) -> Assert.Equal((5, 1), origin x)))

[<Fact>]
let ``Cyrillic followed by Latin should be rendered properly``() =
    Utils.initializeFontResourceLoading()
    let atom = parseRoot @"\text{Ц}V"
    let box = atom.CreateBox environment
    Assert.NotNull(box)

let private verifyBox source =
    let atom = parseRoot source
    let box = atom.CreateBox environment
    verifyObject box

[<Fact>]
let simpleMatrixBox() =
    verifyBox @"\pmatrix{2 & 2 \\ 2 & 2}"

[<Fact>]
let casesBox() =
    verifyBox @"\cases{a \\ b \\ c}"

[<Fact>]
let nestedMatrixBox() =
    verifyBox @"\matrix{ 1 & 2 & 3 \\ 4 & {\matrix{ 5 \\ 6 }} & 7 }"

[<Fact>]
let wideItemInMatrixBox() =
    verifyBox @"x = \pmatrix{0 & -r & 0 \\ 0 & 0 & -r sin^2(\theta)}"

// Cells of differing heights within a row must share a baseline: the short "a"/"c" and the
// taller "b"/"d" sit on the same line rather than the short glyphs floating up.
[<Fact>]
let mixedHeightCellsShareBaseline() =
    verifyBox @"\matrix{a & b \\ c & d}"

[<Fact>]
let bMatrixBox() =
    verifyBox @"\bmatrix{2 & 2 \\ 2 & 2}"

[<Fact>]
let bbMatrixBox() =
    verifyBox @"\Bmatrix{2 & 2 \\ 2 & 2}"

[<Fact>]
let vMatrixBox() =
    verifyBox @"\vmatrix{2 & 2 \\ 2 & 2}"

[<Fact>]
let vvMatrixBox() =
    verifyBox @"\Vmatrix{2 & 2 \\ 2 & 2}"

[<Fact>]
let emptyCellMatrix() =
    verifyBox @"\matrix{A & B \\ A & B \\ & B}"

[<Fact>]
let shortCommandForThinspace(): unit =
    verifyBox @"\,"

[<Fact>]
let shortCommandForNotEqual(): unit =
    verifyBox @"\neq"

[<Fact>]
let emptyColorbox(): unit =
    verifyBox @"\colorbox{red}{}"

[<Fact>]
let emptyMathrm(): unit =
    verifyBox @"\mathrm{}"

[<Fact>]
let emptyCommandText(): unit =
    verifyBox @"\text{}"

[<Fact>]
let emptyColorRed(): unit =
    verifyBox @"\color{red}{}"

[<Fact>]
let emptyMatrix(): unit =
    verifyBox @"\matrix{}"
