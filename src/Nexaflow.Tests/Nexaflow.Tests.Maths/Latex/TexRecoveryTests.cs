using Nexaflow.Maths.Latex;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Maths.Latex;

/// <summary>
/// What the parser does with input nobody would call valid — which is to say, with almost every state a
/// formula passes through while it is being written.
///
/// <para>
/// <c>\frac{a</c> is not a broken formula, it is a formula halfway through being typed, and an editor
/// holds one of those far more often than it holds a finished one. So none of this throws, none of it is
/// dropped, and none of it is quietly repaired: closing that brace would be the most natural thing in
/// the world and would mean the reader's caret was no longer where they left it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("maths-latex-holds")]
public class TexRecoveryTests
{
    private static readonly string[] Awkward =
    [
        "",
        " ",
        "}",
        "{",
        "{{{",
        "}}}",
        @"\",
        @"\\",
        @"\frac",
        @"\frac{a",
        @"\frac{a}",
        @"\frac{}{}",
        @"\sqrt[3",
        @"\sqrt[",
        "x^",
        "^",
        "_",
        "^2",
        "&",
        "a & b",
        "%",
        "% a comment and nothing else",
        "x % trailing comment",
        @"\end{matrix}",
        @"\begin{matrix}",
        @"\begin{matrix} a & b",
        @"\begin{matrix}\end{matrix}",
        @"\begin{matrix} a & b \\ \end{matrix}",
        @"\begin{matrix} a \\ b \\ \end{matrix}",
        @"\left( x",
        @"\right)",
        @"\left",
        @"\begin",
        @"\begin{",
        @"\notacommand{x}",
        @"\begin{notanenvironment} x \end{notanenvironment}",
        "$x$",
        "α + β",
        "x^^2",
        @"\frac{\frac{\frac{a}{b}}{c}}{d}",
        "{a & b}",
        @"\begin{array}{cc} a & b \end{array}",
        @"\begin{array} a & b \end{array}",
    ];

    [TestMethod]
    public void NoneOfItThrows()
    {
        foreach (var latex in Awkward)
        {
            try
            {
                TexParser.Parse(latex);
            }
            catch (Exception e)
            {
                Assert.Fail($"\"{latex}\" threw {e.GetType().Name}: {e.Message}");
            }
        }
    }

    [TestMethod]
    public void AllOfItReadsBackExactly()
    {
        foreach (var latex in Awkward)
            Assert.AreEqual(latex, TexParser.Parse(latex).Print(), $"\"{latex}\"");
    }

    [TestMethod]
    public void NothingIsRepaired()
    {
        // The temptation, and the trap. An unclosed group is left unclosed: a parser that supplied the
        // brace would round-trip everything except the formulas an editor actually holds, and the
        // reader would watch their caret move as they typed.
        var group = Only(TexParser.Parse(@"{a"));

        Assert.AreEqual(TexKind.Group, group.Kind);
        Assert.IsNotNull(group.Part(TexRole.Open), "the brace that was typed is there");
        Assert.IsNull(group.Part(TexRole.Close), "the one that was not typed is not");
    }

    [TestMethod]
    public void AnArgumentThatWasNeverTypedIsSimplyAbsent()
    {
        var command = Only(TexParser.Parse(@"\frac{a}"));

        Assert.AreEqual(TexKind.Command, command.Kind);
        Assert.IsNotNull(command.Part(TexRole.Numerator), "the numerator was typed");
        Assert.IsNull(command.Part(TexRole.Denominator), "the denominator was not");
    }

    [TestMethod]
    public void ACommandDoesNotKeepTheSpaceAfterAnArgumentItNeverGot()
    {
        // \frac at the end of a line has no numerator, so it has no business owning the space that
        // follows it either — that space is between two things, not inside one. It matters because the
        // moment the reader types the {, the space must not end up inside the fraction.
        var root = TexParser.Parse(@"\frac ");

        Assert.AreEqual(2, root.Children.Count, "the command, then the space");
        Assert.AreEqual(TexKind.Command, root.Children[0].Kind);
        Assert.AreEqual(TexKind.Space, root.Children[1].Kind);
    }

    [TestMethod]
    public void MachineryWhereContentGoesIsHeldAsWritten()
    {
        // A closing brace that closes nothing cannot be read as anything. It is kept, marked as held
        // rather than understood, and everything around it is read normally.
        var root = TexParser.Parse("a}b");

        Assert.AreEqual(3, root.Children.Count);
        Assert.AreEqual(TexKind.Verbatim, root.Children[1].Kind);
        Assert.AreEqual("}", root.Children[1].Text);
        Assert.AreEqual(TexKind.Char, root.Children[2].Kind, "and the reading carries on");
    }

    [TestMethod]
    public void AnUnterminatedEnvironmentStillHasItsRows()
    {
        var environment = Only(TexParser.Parse(@"\begin{matrix} a & b"));

        Assert.AreEqual(TexKind.Environment, environment.Kind);
        Assert.IsNotNull(environment.Part(TexRole.Begin));
        Assert.IsNull(environment.Part(TexRole.End), "it was never ended");
        Assert.AreEqual(1, environment.Parts(TexRole.Row).Count());
    }

    /// <summary>The one thing the formula is made of, ignoring the space around it.</summary>
    private static TexNode Only(TexNode root)
    {
        var content = root.Children
            .Where(child => child.Kind is not (TexKind.Space or TexKind.Comment))
            .ToList();

        Assert.AreEqual(1, content.Count, $"expected one thing, got: {string.Join(", ", content)}");
        return content[0];
    }
}
