using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// The initiatives CLI's argument parser. It exists because the previous "filter out anything starting with
/// '-', then look each flag up by name" idiom <b>silently ignored</b> an option the verb didn't know about —
/// so <c>set-concern &lt;id&gt; tests done --note "why"</c> discarded the note and still reported success.
/// Since the same parse guards a transactional <c>batch</c> script, a dropped option means a silent data loss.
/// </summary>
[TestClass]
[CoversNode("nfi-verbs")]
public class VerbArgsTests
{
    // Stands in for the real specs: two positionals, one value flag, one switch.
    private static readonly VerbSpec Spec =
        new("demo", 2, ["--desc"], ["--force"], "demo <a> <b> [<root>] [--desc <d>] [--force]");

    private static VerbArgs Parse(params string[] args)
    {
        Assert.IsTrue(VerbArgs.TryParse(Spec, args, out var parsed, out var error), error);
        return parsed;
    }

    private static string Error(params string[] args)
    {
        Assert.IsFalse(VerbArgs.TryParse(Spec, args, out _, out var error), "expected a parse failure");
        return error;
    }

    // ── The bug this parser exists to prevent ─────────────────────────────────

    [TestMethod]
    public void AnUnknownOption_IsAnError_NotSilentlyIgnored()
    {
        var error = Error("a", "b", "--note", "why");
        StringAssert.Contains(error, "unknown option '--note'");
        StringAssert.Contains(error, "usage:", "the failure shows the verb's usage");
    }

    [TestMethod]
    public void AnOptionMissingItsValue_IsAnError()
        => StringAssert.Contains(Error("a", "b", "--desc"), "'--desc' needs a value");

    [TestMethod]
    public void AValueGivenToASwitch_IsAnError()
        => StringAssert.Contains(Error("a", "b", "--force=yes"), "'--force' is a switch and takes no value");

    // ── Positionals and <root> ────────────────────────────────────────────────

    [TestMethod]
    public void PositionalsAreTakenInOrder_AndTheTrailingOneIsTheRoot()
    {
        var a = Parse("first", "second");
        CollectionAssert.AreEqual(new[] { "first", "second" }, a.Positionals.ToArray());
        Assert.IsNull(a.Root, "root is optional");

        var withRoot = Parse("first", "second", "C:/repo");
        CollectionAssert.AreEqual(new[] { "first", "second" }, withRoot.Positionals.ToArray());
        Assert.AreEqual("C:/repo", withRoot.Root);
    }

    [TestMethod]
    public void OptionsDoNotDisturbPositionalOrder()
    {
        var a = Parse("--desc", "d", "first", "--force", "second", "C:/repo");
        CollectionAssert.AreEqual(new[] { "first", "second" }, a.Positionals.ToArray());
        Assert.AreEqual("C:/repo", a.Root);
        Assert.AreEqual("d", a.Value("--desc"));
        Assert.IsTrue(a.Has("--force"));
    }

    [TestMethod]
    public void TooFewPositionals_IsAnError()
        => StringAssert.Contains(Error("only-one"), "not enough arguments");

    [TestMethod]
    public void ASurplusPositional_IsAnError_RatherThanBeingSwallowed()
        => StringAssert.Contains(Error("a", "b", "root", "stray"), "unexpected argument(s) 'stray'");

    [TestMethod]
    public void InsideABatch_ThereIsNoRoot_SoAThirdPositionalIsRejected()
    {
        Assert.IsFalse(VerbArgs.TryParse(Spec.InBatch, ["a", "b", "root"], out _, out var error));
        StringAssert.Contains(error, "a batch instruction takes no <root>");

        Assert.IsTrue(VerbArgs.TryParse(Spec.InBatch, ["a", "b"], out var ok, out _));
        Assert.IsNull(ok.Root);
    }

    // ── Value handling ────────────────────────────────────────────────────────

    [TestMethod]
    public void AValueIsTakenPositionally_SoItMayItselfLookLikeAnOption()
    {
        // --desc "--verbatim" must not be read as the unknown option '--verbatim'.
        Assert.AreEqual("--verbatim", Parse("a", "b", "--desc", "--verbatim").Value("--desc"));
    }

    [TestMethod]
    public void EqualsFormIsAccepted_AndSplitsOnTheFirstEqualsOnly()
    {
        Assert.AreEqual("d", Parse("a", "b", "--desc=d").Value("--desc"));
        Assert.AreEqual("x=y", Parse("a", "b", "--desc=x=y").Value("--desc"));
    }

    [TestMethod]
    public void AValueEqualToAnEarlierToken_IsNotMistakenForAPositional()
    {
        // The old FollowsFlag helper located a token with Array.IndexOf — the FIRST occurrence — so a flag
        // value that repeated an earlier positional was misread. Left-to-right parsing can't do that.
        var a = Parse("main", "b", "--desc", "main");
        CollectionAssert.AreEqual(new[] { "main", "b" }, a.Positionals.ToArray());
        Assert.AreEqual("main", a.Value("--desc"));
        Assert.IsNull(a.Root, "the repeated value is the option's, not a root");
    }

    [TestMethod]
    public void ARepeatedFlag_KeepsEveryValue_AndValueReturnsTheLast()
    {
        var spec = new VerbSpec("scan", 0, ["--test-dll"], [], "scan [--test-dll <p>]...");
        Assert.IsTrue(VerbArgs.TryParse(spec, ["--test-dll", "one.dll", "--test-dll", "two.dll"], out var a, out _));

        CollectionAssert.AreEqual(new[] { "one.dll", "two.dll" }, a.All("--test-dll").ToArray());
        Assert.AreEqual("two.dll", a.Value("--test-dll"));
    }

    [TestMethod]
    public void AnAbsentOption_ReadsAsNullOrFalse_AndAnEmptyListWhenRepeatable()
    {
        var a = Parse("a", "b");
        Assert.IsNull(a.Value("--desc"));
        Assert.IsFalse(a.Has("--force"));
        Assert.AreEqual(0, a.All("--desc").Count);
    }

    [TestMethod]
    public void ABareDashIsAPositional_NotAnOption()
    {
        // A single "-" is a conventional stand-in for stdin/"here", never an option name.
        var a = Parse("-", "b");
        CollectionAssert.AreEqual(new[] { "-", "b" }, a.Positionals.ToArray());
    }

    // ── The batch tokenizer that feeds this parser ────────────────────────────

    [TestMethod]
    public void Tokenize_GroupsAQuotedValue_AndSplitsOnWhitespace()
        => CollectionAssert.AreEqual(
            new[] { "set-node", "my-id", "--desc", "two words here" },
            Program.Tokenize("""set-node my-id --desc "two words here" """).ToArray());

    [TestMethod]
    public void Tokenize_KeepsAnExplicitlyEmptyQuotedValue()
    {
        // set-node documents an empty --note as "clear the field". The quotes used to collapse to nothing,
        // so the option looked like it was missing its value and the whole batch aborted.
        CollectionAssert.AreEqual(
            new[] { "set-node", "my-id", "--note", "" },
            Program.Tokenize("""set-node my-id --note "" """).ToArray());
    }

    [TestMethod]
    public void Tokenize_CollapsesRunsOfWhitespace_WithoutEmittingEmptyTokens()
        => CollectionAssert.AreEqual(
            new[] { "move", "a", "b" },
            Program.Tokenize("  move   a \t b  ").ToArray());
}
