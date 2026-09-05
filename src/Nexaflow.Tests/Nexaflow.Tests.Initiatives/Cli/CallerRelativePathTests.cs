using System.Collections.Generic;
using System.IO;
using Nexaflow.Services.Initiatives.Cli;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Cli;

/// <summary>
/// Which directory a path typed on the command line is measured from.
///
/// <para>
/// Every invocation is served by the resident process for the tree — started once, from somewhere else,
/// answering callers who stand in several different directories at the same time. So the process's own
/// current directory is not the caller's, and every <c>Path.GetFullPath(p)</c> that silently used it was
/// resolving the caller's argument against a stranger's directory. <c>nfi batch tree.batch</c> reported
/// "no such script file" for a file the caller was looking straight at.
/// </para>
/// <para>
/// The other half is the same defect wearing the other face: <c>&lt;root&gt;</c> was inferred from the
/// first positional whatever it named, and <c>batch</c>'s first positional is a <b>file</b>. The daemon was
/// then asked to start with a file for a working directory, could not, and the command meant to rewrite the
/// tree answered with a Win32Exception instead. Both are pinned here because <c>batch</c> is the documented
/// replacement for hand-editing tree.json and is what an agent is told to reach for.
/// </para>
/// <para>
/// Assertions are on the tree and the exit code rather than on printed text: the console is process-global,
/// this suite runs its methods in parallel, and the question these tests ask — was the right file found and
/// the tree actually written — is answered better by the tree than by what was said about it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("nfi-self-location")]
public class CallerRelativePathTests
{
    private const int Clean = 0, Error = 2;

    private string _tmp = string.Empty;

    /// <summary>Where the caller stands — deliberately NOT the product root, which is the whole point.</summary>
    private string Caller => Path.Combine(_tmp, "caller");

    /// <summary>A repo with a <c>.product/</c> holding one node, the way a real root has one.</summary>
    private string Product => Path.Combine(_tmp, "product");

    [TestInitialize]
    public void Setup()
    {
        _tmp = Directory.CreateTempSubdirectory("nexa-callerpath-").FullName;
        Directory.CreateDirectory(Caller);
        Directory.CreateDirectory(Product);

        var store = new ProductStore(Product);
        store.Initialize("P");
        store.SaveTree(new Dictionary<string, ProductNode> { ["n"] = new() { Title = "n", Status = Status.Should } });
    }

    [TestCleanup]
    public void Teardown() { try { Directory.Delete(_tmp, recursive: true); } catch { } }

    /// <summary>Runs a verb exactly as the daemon does: the caller's directory carried on the request rather
    /// than taken from the process. <see cref="Program.Execute"/> is the entry point <c>DaemonServer</c> calls.</summary>
    private static int Serve(string callerDirectory, params string[] args)
    {
        using (RequestScope.Begin(new StringWriter(), new StringWriter(), callerDirectory))
            return Program.Execute(args);
    }

    /// <summary>The one instruction every script here applies, read back off disk.</summary>
    private Status StatusOnDisk => new ProductStore(Product).Load().Nodes["n"].Status;

    private string Script(string directory)
    {
        var path = Path.Combine(directory, "tree.batch");
        File.WriteAllText(path, "set-status n done\n");
        return path;
    }

    // ── the script path ──────────────────────────────────────────────────────────────────────────

    /// <summary>The reported bug, in the form every documented example is written in.</summary>
    [TestMethod]
    public void ARelativeScriptPath_IsFoundWhereTheCallerStands()
    {
        Script(Caller);

        Assert.AreEqual(Clean, Serve(Caller, "batch", "tree.batch", Product));
        Assert.AreEqual(Status.Done, StatusOnDisk, "the script was found, so the tree was actually rewritten");
    }

    /// <summary>The same name, but the file sitting under the product root instead — which is where the
    /// resident process happens to stand, and therefore the one place it must NOT be looked for.</summary>
    [TestMethod]
    public void ARelativeScriptPath_IsNotLookedForUnderTheProductRoot()
    {
        Script(Product);

        Assert.AreEqual(Error, Serve(Caller, "batch", "tree.batch", Product),
                        "the caller has no tree.batch, so this must fail rather than quietly run the root's");
        Assert.AreEqual(Status.Should, StatusOnDisk, "and nothing may be written on the way to failing");
    }

    /// <summary>An absolute path is untouched by any of this and still simply works.</summary>
    [TestMethod]
    public void AnAbsoluteScriptPath_StillWorks()
    {
        var script = Script(Caller);

        Assert.AreEqual(Clean, Serve(Path.GetTempPath(), "batch", script, Product));
        Assert.AreEqual(Status.Done, StatusOnDisk);
    }

    /// <summary>--dry-run still writes nothing, which is what makes it the way a script is checked.</summary>
    [TestMethod]
    public void ADryRun_ParsesTheCallersScript_AndWritesNothing()
    {
        Script(Caller);

        Assert.AreEqual(Clean, Serve(Caller, "batch", "tree.batch", Product, "--dry-run"));
        Assert.AreEqual(Status.Should, StatusOnDisk);
    }

    // ── root inference ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The other bug. <c>&lt;root&gt;</c> is a directory by definition, and taking <c>batch</c>'s first
    /// positional for one named the script file as the root — which the client then handed the daemon as its
    /// working directory, and Windows refused the spawn.
    /// </summary>
    [TestMethod]
    public void AScriptFile_IsNeverMistakenForTheRoot()
    {
        var script = Script(Caller);

        using var scope = RequestScope.Begin(new StringWriter(), new StringWriter(), Product);

        Assert.AreEqual(Product, Program.ResolveRoot([script, "--dry-run"]),
                        "a file cannot be a root, so the caller's own directory is the answer");
    }

    /// <summary>…while a <c>&lt;root&gt;</c> that really is one is still read as one.</summary>
    [TestMethod]
    public void ADirectoryArgument_IsStillTheRoot()
    {
        using var scope = RequestScope.Begin(new StringWriter(), new StringWriter(), Caller);

        Assert.AreEqual(Product, Program.ResolveRoot([Product, "--json"]));
    }

    /// <summary>A relative root is measured from the caller too, not from the process.</summary>
    [TestMethod]
    public void ARelativeRoot_IsResolvedFromTheCaller()
    {
        using var scope = RequestScope.Begin(new StringWriter(), new StringWriter(), _tmp);

        Assert.AreEqual(Product, Program.ResolveRoot(["product"]));
    }

    // ── the directory test the parser leans on ───────────────────────────────────────────────────

    /// <summary>
    /// <c>VerbArgs</c> moves a trailing directory into the <c>&lt;root&gt;</c> slot, so where that directory
    /// is looked for decides what <c>nfi tree src</c> means. Measured in the daemon's directory it meant
    /// "the whole tree of whichever repo the daemon lives in", from anywhere on the machine.
    /// </summary>
    [TestMethod]
    public void ATrailingDirectory_IsOnlyARoot_WhenTheCallerCanSeeIt()
    {
        var spec = new VerbSpec("tree", 1, [], [], "tree [<node-id>] [<root>]", MinPositionals: 0);
        Directory.CreateDirectory(Path.Combine(Product, "src"));

        using (RequestScope.Begin(new StringWriter(), new StringWriter(), Caller))
        {
            Assert.IsTrue(VerbArgs.TryParse(spec, ["src"], out var away, out var error), error);
            Assert.AreEqual("src", away[0], "the caller has no src/, so it is a node id and nothing moves");
            Assert.IsNull(away.Root);
        }

        using (RequestScope.Begin(new StringWriter(), new StringWriter(), Product))
        {
            Assert.IsTrue(VerbArgs.TryParse(spec, ["src"], out var here, out var error), error);
            Assert.AreEqual("src", here.Root, "standing next to it, it is the root the rule promises");
        }
    }

    // ── the guarantee a failed spawn was said to have broken ─────────────────────────────────────

    /// <summary>
    /// A daemon must not fail to start because of an argument. The root is carried as an argument and every
    /// path is caller-relative, so its working directory is orientation only — and a root that names a file
    /// has to degrade to a real directory rather than have Windows refuse the spawn outright.
    /// </summary>
    [TestMethod]
    public void ADaemonIsNeverStartedInADirectoryThatIsNotOne()
    {
        Assert.IsTrue(Directory.Exists(DaemonServer.SpawnDirectory(Script(Caller))));
        Assert.AreEqual(Product, DaemonServer.SpawnDirectory(Product), "a real root is still used as given");
    }
}
