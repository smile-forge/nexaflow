using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Architecture;

/// <summary>
/// The <c>nfi</c> CLI must stay a shell over <c>GraphQuery</c>/<c>GraphReport</c>, never a second
/// implementation of them.
/// <para>
/// This is not a style preference. <c>GraphQuery</c>'s own header states the requirement — the CLI and the
/// in-app assistant ask the same questions and must get the same answer — and it had already been broken:
/// <c>Program.cs</c> carried private copies of <c>TypeRank</c>, <c>NodeLine</c>, <c>BlockEnd</c>,
/// <c>BuildAdjacency</c> and <c>Bfs</c>, and the <c>BlockEnd</c> copy had silently diverged in three ways.
/// One of them clamped an unclosed block to 40 lines however many were asked for, so raising the shared
/// scan budget fixed the assistant and left the terminal answering differently. Nothing caught it, because
/// two copies of a pure function compile perfectly and only disagree at runtime.
/// </para>
/// <para>
/// A textual check is the right instrument here: the CLI is a single-file <c>Program.cs</c> whose members are
/// private, so reflection cannot see them, and the failure being guarded against is precisely a *declaration*
/// reappearing. Renaming a copy to dodge this test is possible; that is a deliberate act, not the accident
/// this catches.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("architecture guard")]
public class CliHasNoPrivateGraphTwinsTests
{
    /// <summary>Members of the shared graph library that the CLI must call rather than reimplement. Each is a
    /// pure function whose duplicate would be invisible until it gave a different answer.
    /// <para>
    /// <c>BlockEnd</c> stays on the list although the library no longer has one: where a declaration stops is
    /// <c>SourceSpans</c>' question now, answered by the tree-sitter parse. A hand-rolled brace scanner
    /// reappearing in the CLI under that name is precisely the regression this list exists to catch.
    /// </para></summary>
    private static readonly string[] SharedGraphMembers =
    [
        "TypeRank", "NodeLine", "BlockEnd", "Adjacency", "BuildAdjacency", "Bfs",
        "OwnedFiles", "Scope", "Identity", "AppendRelations",
    ];

    private static string CliProgram()
        => Path.Combine(RepoRoot.Locate(), "src", "Nexaflow.Services.Initiatives.Cli", "Program.cs");

    [TestMethod]
    public void Cli_declares_no_private_twin_of_a_shared_graph_member()
    {
        Assert.IsTrue(File.Exists(CliProgram()), $"expected the CLI entry point at {CliProgram()}");
        var source = File.ReadAllText(CliProgram());

        var offenders = new List<string>();
        foreach (var member in SharedGraphMembers)
        {
            // A declaration, not a call: a modifier and a return type ahead of the name and a '(' after it.
            var declaration = new Regex(
                $@"^\s*(private|internal|public|protected)\s+(static\s+)?[\w<>,\[\]\?\s\.]+\s{Regex.Escape(member)}\s*\(",
                RegexOptions.Multiline);
            if (declaration.IsMatch(source)) offenders.Add(member);
        }

        Assert.AreEqual(0, offenders.Count,
            "Program.cs re-declares shared graph member(s) instead of calling the library: "
            + string.Join(", ", offenders)
            + ". Call GraphQuery/GraphReport directly — a second copy compiles fine and diverges silently.");
    }

    [TestMethod]
    public void Cli_actually_calls_the_shared_graph_library()
    {
        var source = File.ReadAllText(CliProgram());

        // The inverse assertion: deleting the twins would also pass the test above if the CLI simply stopped
        // doing the work. It has to be delegating.
        foreach (var qualified in new[] { "GraphQuery.", "GraphReport." })
            StringAssert.Contains(source, qualified,
                $"the CLI should be routing graph work through {qualified.TrimEnd('.')}");
    }

    [TestMethod]
    public void The_block_scan_budget_has_exactly_one_definition()
    {
        var source = File.ReadAllText(CliProgram());

        // The magic 400 this replaced was written out at six call sites across two files; that is how the two
        // BlockEnd copies came to disagree about it without anyone noticing. The budget moved to SourceSpans
        // with the block resolution; the way to get it wrong did not.
        var bareLiterals = Regex.Matches(source, @"Block(Of)?\([^)]*,\s*\d+\s*\)");
        Assert.AreEqual(0, bareLiterals.Count,
            "pass GraphQuery.BlockScanLines, not a literal: " + string.Join(", ", bareLiterals.Select(m => m.Value)));
    }
}
