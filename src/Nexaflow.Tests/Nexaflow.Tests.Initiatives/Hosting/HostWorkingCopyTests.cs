using System.IO;
using Nexaflow.Services.Initiatives.Hosting;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// The transaction boundary a resident process needs: a command edits the state it is handed and only its
/// write decides whether to keep the result, so the tree the host serves must never be the object a command
/// is editing.
/// <para>
/// Handing out the live instance is what let a refused <c>set-snaplink</c> print "nothing was written" while
/// leaving a half-applied link in memory, for the next unrelated command to persist — and an aborted batch
/// leave every earlier line of itself behind after saying nothing was written. Neither is reachable from a
/// one-shot process, which throws its host away, so both were invisible until the daemon made the process
/// outlive the command.
/// </para>
/// </summary>
[TestClass]
[CoversNode("initiatives-daemon")]
public class HostWorkingCopyTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nfi-copy-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        new ProductStore(_root).SaveTree(new Dictionary<string, ProductNode>
        {
            ["leaf"] = new()
            {
                Title = "Leaf",
                Concerns = [new ConcernLink { Tag = "tests", Snaplinks = [new Snaplink { Type = "code", Doc = "A.cs", Class = "A" }] }],
                Snaplinks = [new Snaplink { Type = "code", Doc = "B.cs", Class = "B" }],
            }
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [TestMethod]
    public void AWorkingCopyIsPrivate_SoAnAbandonedEditIsAbandoned()
    {
        using var host = new InitiativesHost(_root);

        var copy = host.WorkingCopy();
        copy.Nodes["leaf"].Title = "edited";
        copy.Nodes["leaf"].Snaplinks![0].Class = "Nope";
        copy.Nodes["leaf"].Concerns![0].Snaplinks![0].Doc = "gone.cs";
        copy.Nodes["leaf"].Children.Add("invented");

        Assert.AreEqual("Leaf", host.Tree.Nodes["leaf"].Title, "the tree the host serves is not the one being edited");
        Assert.AreEqual("B", host.Tree.Nodes["leaf"].Snaplinks![0].Class, "…down to the node's own snaplinks");
        Assert.AreEqual("A.cs", host.Tree.Nodes["leaf"].Concerns![0].Snaplinks![0].Doc, "…and a concern's");
        Assert.AreEqual(0, host.Tree.Nodes["leaf"].Children.Count);
    }

    /// <summary>Two commands in one process must not see each other's abandoned work either.</summary>
    [TestMethod]
    public void TwoWorkingCopies_DoNotSeeEachOther()
    {
        using var host = new InitiativesHost(_root);

        var first = host.WorkingCopy();
        first.Nodes["leaf"].Title = "edited";

        Assert.AreEqual("Leaf", host.WorkingCopy().Nodes["leaf"].Title);
    }

    /// <summary>
    /// The other half: what a command saved is adopted as a copy, because the command goes on editing the
    /// state it saved — a branch's pending links are put back on it for reporting — and none of that is
    /// what the file now says.
    /// </summary>
    [TestMethod]
    public void WhatWasSavedIsAdoptedAsACopy_NotByReference()
    {
        using var host = new InitiativesHost(_root);

        var copy = host.WorkingCopy();
        copy.Nodes["leaf"].Title = "saved";
        host.TreeSaved(copy);

        copy.Nodes["leaf"].Title = "after the write";

        Assert.AreEqual("saved", host.Tree.Nodes["leaf"].Title);
    }
}
