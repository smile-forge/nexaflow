using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// The check that catches a shell having rewritten an escaped payload before the process saw it.
/// <para>
/// It exists because that failure is silent. Git Bash converts an argument it takes for a POSIX path, and
/// a doc comment written as <c>'/// &lt;x/&gt;\nfoo'</c> arrives with its backslash turned round. The text
/// still parses, the edit still applies, and the file ends up with a literal slash-n in it — found by eye,
/// later. The signature is narrow on purpose: an <em>escaped</em> payload is one whose whole point is its
/// backslashes, so one carrying none while carrying a slash where each escape belonged is not ambiguous.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Argument handling for the headless CLI — infrastructure, not a product-tree node.")]
public class MangledEscapeTests
{
    [DataTestMethod]
    [DataRow("// <inheritdoc/>/nprotected override Panel Layer => L;")]
    [DataRow("first/nsecond")]
    [DataRow("a/tb")]
    [DataRow("line/rnext")]
    public void EscapesTurnedRoundByTheShell_AreRefused(string value)
    {
        Assert.IsNotNull(Program.MangledEscapes("--text-escaped", value),
                         "an escaped payload with no backslash and a slash where one belongs is mangled");
    }

    /// <summary>The refusal has to say what to do, or it is just another failure to work around.</summary>
    [TestMethod]
    public void TheRefusal_NamesTheEnvironmentVariableAndTheOtherWaysIn()
    {
        var message = Program.MangledEscapes("--find-escaped", "a/nb");

        Assert.IsNotNull(message);
        StringAssert.Contains(message!, "MSYS2_ARG_CONV_EXCL");
        StringAssert.Contains(message!, "--find-file");
        StringAssert.Contains(message!, "--find-stdin");
    }

    [DataTestMethod]
    [DataRow("no slashes at all")]
    [DataRow("src/network/Foo.cs")]
    [DataRow("a/nb")]
    public void APayloadThatStillHasItsBackslashes_IsLeftAlone(string tail)
    {
        Assert.IsNull(Program.MangledEscapes("--text-escaped", "first" + Backslash + "n" + tail),
                      "the backslash proves the shell did not get to it");
    }

    /// <summary>A payload with nothing that looks like a turned-round escape is not this check's business,
    /// however path-like it is.</summary>
    [DataTestMethod]
    [DataRow("src/main/Foo.cs")]
    [DataRow("just some prose")]
    [DataRow("")]
    public void APayloadWithNoTurnedRoundEscape_IsLeftAlone(string value) =>
        Assert.IsNull(Program.MangledEscapes("--text-escaped", value));

    private const char Backslash = '\u005C';
}
