using System.IO;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// What happens when a directory turns up where an id belongs — the mistake `nfi tree D:\SomeRepo` makes.
///
/// <para>
/// Left alone it was the worst kind of wrong: the path was read as a node id, the root fell back to the
/// current directory, a <b>different repository's</b> tree was searched, and the answer — "no node
/// 'D:\SomeRepo'" — was perfectly true and about the wrong repo. The fix has to keep the strictness the
/// parser is built on, so the discriminator is <c>Directory.Exists</c>: a real id (<c>code:src/Foo.cs#T:Bar</c>)
/// is full of slashes and never names a directory.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Argument parsing for the headless CLI — infrastructure, not a product-tree node.")]
public class VerbPathArgumentTests
{
    private static readonly VerbSpec Optional =
        new("tree", 1, [], ["--full"], "tree [<node-id>] [<root>] [--full]", MinPositionals: 0);

    private static readonly VerbSpec Required =
        new("describe", 1, [], [], "describe <node-id> [<root>]");

    private static string ADirectory => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

    [TestMethod]
    public void ADirectoryBecomesTheRoot_WhenTheIdIsOptional()
    {
        // "Show me that repo's tree" is a complete request, so there is exactly one sensible reading.
        Assert.IsTrue(VerbArgs.TryParse(Optional, [ADirectory], out var parsed, out var error), error);
        Assert.AreEqual(ADirectory, parsed.Root);
        Assert.AreEqual(string.Empty, parsed.Positionals.Count > 0 ? parsed[0] : string.Empty,
                        "the optional slot is left unsupplied rather than filled with a path");
    }

    [TestMethod]
    public void ADirectoryDoesNotDisplaceARequiredId()
    {
        // `describe` cannot run without a node id, so moving the path to <root> would silently produce a
        // different, broken command. Parsing succeeds here; TryRead is what rejects it, with the fix printed.
        Assert.IsTrue(VerbArgs.TryParse(Required, [ADirectory], out var parsed, out var error), error);
        Assert.AreEqual(ADirectory, parsed[0], "it stays in the slot it was typed into");
        Assert.IsNull(parsed.Root);
    }

    [TestMethod]
    public void ARealIdIsNeverMistakenForARoot()
    {
        // The whole rule rests on this: ids contain slashes constantly and directories they are not.
        const string id = "code:src/Nexaflow.Syntax/CodeStructureExtractor.cs#T:CodeStructureExtractor";
        Assert.IsTrue(VerbArgs.TryParse(Optional, [id], out var parsed, out var error), error);
        Assert.AreEqual(id, parsed[0]);
        Assert.IsNull(parsed.Root);
    }

    [TestMethod]
    public void AnOptionalIdMaySimplyBeOmitted()
    {
        Assert.IsTrue(VerbArgs.TryParse(Optional, [], out var parsed, out var error), error);
        Assert.AreEqual(0, parsed.Positionals.Count);

        Assert.IsFalse(VerbArgs.TryParse(Required, [], out _, out var missing), "a required id is still required");
        StringAssert.Contains(missing, "missing arguments");
    }

    [TestMethod]
    public void BothArgumentsStillWorkTheOrdinaryWay()
    {
        Assert.IsTrue(VerbArgs.TryParse(Optional, ["some-node", ADirectory], out var parsed, out var error), error);
        Assert.AreEqual("some-node", parsed[0]);
        Assert.AreEqual(ADirectory, parsed.Root);
    }

    [TestMethod]
    public void SurplusArgumentsAreStillRejected()
    {
        Assert.IsFalse(VerbArgs.TryParse(Optional, ["some-node", ADirectory, "extra"], out _, out var error));
        StringAssert.Contains(error, "unexpected argument");
    }

    [TestMethod]
    public void ABatchInstructionTakesNoRoot_SoNothingMoves()
    {
        // Inside a batch script every instruction shares the run's single root, so a directory has no slot
        // to move into and must not silently vanish from the one it was typed in.
        Assert.IsTrue(VerbArgs.TryParse(Optional.InBatch, [ADirectory], out var parsed, out var error), error);
        Assert.AreEqual(ADirectory, parsed[0]);
        Assert.IsNull(parsed.Root);
    }
}
