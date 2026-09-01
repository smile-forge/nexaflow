module WpfMath.Tests.EnvironmentTests

open Xunit

open WpfMath.Tests.ApprovalTestUtils
open WpfMath.Tests.Utils
open XamlMath.Exceptions

[<Fact>]
let alignEnvironment(): unit =
    verifyParseResult @"\begin{align} x+1 &= y \\ x &= y-1 \end{align}"

[<Fact>]
let alignEnvironmentLarge(): unit =
    verifyParseResult @"\begin{align} x+1 &= y & a*2 &= b \\ x &= y-1 & a &= \frac{b}{2} \end{align}"

[<Fact>]
let pMatrixEnvironment(): unit =
    verifyParseResult @"\begin{pmatrix}{line 1}\\line 2\end{pmatrix}"

[<Fact>]
let bMatrixEnvironment(): unit =
    verifyParseResult @"\begin{bmatrix}a & b \\ c & d\end{bmatrix}"

[<Fact>]
let bbMatrixEnvironment(): unit =
    verifyParseResult @"\begin{Bmatrix}a & b \\ c & d\end{Bmatrix}"

[<Fact>]
let vMatrixEnvironment(): unit =
    verifyParseResult @"\begin{vmatrix}a & b \\ c & d\end{vmatrix}"

[<Fact>]
let vvMatrixEnvironment(): unit =
    verifyParseResult @"\begin{Vmatrix}a & b \\ c & d\end{Vmatrix}"

[<Fact>]
let nestedEnvironment(): unit =
    verifyParseResult @"\begin{pmatrix}line 1\\\begin{pmatrix}line x\end{pmatrix}\end{pmatrix}"

[<Fact>]
let nestedMatrix(): unit =
    verifyParseResult @"\begin{pmatrix}line 1\\\pmatrix{line x & line y}\end{pmatrix}"

// An environment that cannot be made sense of is set as the characters that were written, the same
// as anything else that cannot be drawn — see UnreadableMarkupTests, where that answer is set out.
// Each of these was a TexParseException naming what was wrong with it; the reader is now shown the
// stretch itself, squiggled, which says the same thing in the place it happened.

[<Theory>]
[<InlineData(@"\begin{}")>]                                      // no name
[<InlineData(@"\begin{unknown}")>]                               // a name nobody has heard of
[<InlineData(@"\begin{pmatrix}\begin{pmatrix}\end{pmatrix}")>]   // no \end for the outer one
[<InlineData(@"\begin{pmatrix}\end{unknown}")>]                  // and this \end is not its
let ``an environment that cannot be read is set as its own characters``(markup: string): unit =
    Assert.NotEmpty(undrawn markup)
    Assert.Empty(unreadable markup)
