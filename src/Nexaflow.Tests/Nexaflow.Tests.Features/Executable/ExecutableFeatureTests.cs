using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable;
using Nexaflow.Features.Executable.FileActions;
using Nexaflow.Features.Executable.Services;
using Nexaflow.IO.Pe;
using Nexaflow.Tests.Fixtures;
using NSubstitute;

namespace Nexaflow.Tests.Features.Executable;

/// <summary>The feature's own surface: the file actions, the dependency walk and its mermaid output.</summary>
[TestClass]
[CoversNode("executable-inspector")]
public sealed class ExecutableFeatureTests
{
    private static IShellServices Shell() => Substitute.For<IShellServices>();

    // ── File actions ──────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void Inspect_opens_the_executable_page_for_the_file()
    {
        var shell  = Shell();
        var action = new InspectPeAction(shell);

        Assert.IsTrue(action.PerformAction(@"C:\Windows\System32\kernel32.dll"));

        shell.Received(1).OpenTab(
            ExecutableTabRegistration.StaticPageKind,
            Arg.Is<Dictionary<string, string>>(p => p["path"] == @"C:\Windows\System32\kernel32.dll"),
            Arg.Any<IPageView>(), Arg.Any<bool>());
    }

    [TestMethod, TestCategory("Unit")]
    public void Inspect_declares_the_experience_the_filemap_maps()
    {
        // OptionalExtension criteria are what keep this off the double-click path, so the id has to
        // be its own rather than shared with "Run" at /binary/executable.
        Assert.AreEqual("/binary/pe", InspectPeAction.StaticExperienceId);
        Assert.AreEqual("/binary/pe", new InspectPeAction(Shell()).ExperienceId);
        Assert.IsTrue(new InspectPeAction(Shell()).OpensViewer);
    }

    [TestMethod, TestCategory("Unit")]
    public void Av_scan_is_universal_and_opens_no_viewer()
    {
        var action = new AvScanAction(Shell());

        // AMSI is content-agnostic, so the action is offered on any file; and because it shows a
        // result rather than a tab it must not claim to be a viewer.
        Assert.AreEqual("/", AvScanAction.StaticExperienceId);
        Assert.AreEqual("/", action.ExperienceId);
        Assert.IsFalse(action.OpensViewer);
        Assert.IsTrue(action.SupportsMultipleFiles);
        Assert.IsFalse(action.IsDestructive);
    }

    [TestMethod, TestCategory("Unit")]
    public void Av_scan_queues_background_work_rather_than_blocking()
    {
        var shell  = Shell();
        var action = new AvScanAction(shell);

        Assert.IsTrue(action.PerformAction(PeFixtures.Notepad));

        shell.Received(1).QueueBackgroundTask(
            Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod, TestCategory("Unit")]
    public void Av_scan_with_no_files_does_nothing()
    {
        var shell = Shell();
        Assert.IsFalse(new AvScanAction(shell).PerformAction([]));
        shell.DidNotReceive().QueueBackgroundTask(
            Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>>(), Arg.Any<CancellationToken>());
    }

    [TestMethod, TestCategory("Unit")]
    public void Antivirus_product_state_decodes_the_protection_byte()
    {
        // productState packs three bytes: provider, real-time protection, definitions. Reading the
        // provider byte instead of the protection byte reports an enabled Defender as disabled.
        Assert.AreEqual((true,  true),  AntivirusProducts.DecodeState(0x06_11_00), "enabled, current");
        Assert.AreEqual((true,  false), AntivirusProducts.DecodeState(0x06_11_10), "enabled, out of date");
        Assert.AreEqual((false, true),  AntivirusProducts.DecodeState(0x06_09_00), "disabled");
        Assert.AreEqual((true,  true),  AntivirusProducts.DecodeState(0x06_10_00), "enabled (0x10 form)");
    }

    [TestMethod, TestCategory("Unit")]
    public void An_unscannable_file_is_never_reported_as_clean()
    {
        // Saying "clean" because nothing looked at it would be worse than saying nothing.
        var result = AmsiScanner.ScanFile(@"C:\definitely\not\here.bin");

        Assert.AreEqual(AmsiVerdict.Unavailable, result.Verdict);
        Assert.IsFalse(result.IsThreat);
    }

    // ── Dependency walk ───────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void The_dependency_walk_resolves_modules_and_marks_api_sets()
    {
        var graph = new DependencyWalker(maxDepth: 2).Walk(PeFixtures.Notepad);

        Assert.AreEqual("notepad.exe", graph.Root.Name);
        Assert.IsTrue(graph.Root.Children.Count > 0);
        Assert.IsTrue(graph.NodeCount > 1);

        var apiSets = graph.Root.Children.Where(c => c.Kind == DependencyKind.ApiSet).ToList();
        Assert.IsTrue(apiSets.Count > 0, "API sets should be marked, not chased.");
        Assert.IsTrue(apiSets.All(a => a.Children.Count == 0), "An API set is a leaf.");

        var resolved = graph.Root.Children.Where(c => c.Kind == DependencyKind.Resolved).ToList();
        Assert.IsTrue(resolved.Count > 0);
        Assert.IsTrue(resolved.All(r => r.Path is { Length: > 0 }));
    }

    [TestMethod, TestCategory("Unit")]
    public void Nothing_below_the_root_expands_unless_it_is_asked_for()
    {
        // Expansion is explicit, not depth-driven: a native binary's second level is already
        // unreadable, so the graph only grows where the user pointed.
        var graph = new DependencyWalker().Walk(PeFixtures.Notepad);

        Assert.IsTrue(graph.Root.Children.Count > 0, "The root's own imports are always shown.");
        Assert.IsTrue(graph.Root.Children.All(c => c.Children.Count == 0),
            "No second level should appear without an explicit expansion.");
        Assert.IsTrue(graph.Root.Children.Any(c => c.CanExpand),
            "Resolvable modules should offer themselves for expansion.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Expanding_one_module_opens_only_that_subtree()
    {
        var baseline = new DependencyWalker().Walk(PeFixtures.Notepad);
        var target   = baseline.Root.Children.First(c => c.CanExpand);

        var expanded = new DependencyWalker().Walk(
            PeFixtures.Notepad, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { target.Name });

        var opened = expanded.Root.Children.Where(c => c.Children.Count > 0).ToList();
        Assert.AreEqual(1, opened.Count, "Exactly one module should have opened up.");
        Assert.AreEqual(target.Name, opened[0].Name, "The wrong module expanded.");
        Assert.IsTrue(expanded.NodeCount > baseline.NodeCount, "The graph should have grown.");
        Assert.IsFalse(opened[0].CanExpand, "An expanded module no longer offers expansion.");
    }

    [TestMethod, TestCategory("Unit")]
    public void The_walk_honours_the_node_cap()
    {
        // Expand broadly so the cap is the thing that stops it, not the expansion set.
        var names = new DependencyWalker().Walk(PeFixtures.Notepad).Root.Children
                        .Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var graph = new DependencyWalker(maxDepth: 8, maxNodes: 15).Walk(PeFixtures.Notepad, names);

        Assert.IsTrue(graph.Truncated);
        Assert.IsTrue(graph.NodeCount <= 16, $"Got {graph.NodeCount} nodes against a cap of 15.");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_module_reachable_twice_is_linked_not_re_expanded()
    {
        // kernel32 and ntdll are reachable from practically every node; without this the graph of
        // any real binary explodes.
        var names = new DependencyWalker().Walk(PeFixtures.Notepad).Root.Children
                        .Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = new DependencyWalker().Walk(PeFixtures.Notepad, names);

        var all = Flatten(graph.Root).ToList();
        foreach (var group in all.Where(n => n.Kind is DependencyKind.Resolved)
                                 .GroupBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            Assert.IsTrue(group.Count(n => n.Children.Count > 0) <= 1,
                $"'{group.Key}' was expanded more than once.");

        // A repeat sighting is recorded as an edge, not a second subtree.
        Assert.IsTrue(all.Any(n => n.Kind == DependencyKind.Cycle),
            "A module reached twice should appear as a back-reference.");
    }

    private static IEnumerable<DependencyNode> Flatten(DependencyNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }

    // ── Mermaid output ────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void The_diagram_emits_real_mermaid_click_directives()
    {
        var graph  = new DependencyWalker().Walk(PeFixtures.Notepad);
        var markdown = DependencyMermaid.Build(graph);

        Assert.IsTrue(markdown.StartsWith("```mermaid"), "The block must be fenced as mermaid.");
        Assert.IsTrue(markdown.Contains("graph LR"));
        Assert.IsTrue(markdown.TrimEnd().EndsWith("```"));

        // Standard `click id href "…"` rather than a private convention, so the diagram stays
        // portable if it is pasted anywhere else.
        Assert.IsTrue(markdown.Contains("click n0 href \""), "The root node should carry a link.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Expandable_and_openable_nodes_are_told_apart_by_their_href()
    {
        var root = new DependencyNode("app.exe", DependencyKind.Resolved, @"C:\app\app.exe");
        var child = new DependencyNode("lib.dll", DependencyKind.Resolved, @"C:\app\lib.dll");
        root.Children.Add(child);

        var markdown = DependencyMermaid.Build(new DependencyGraph(root, 2, false, 1));

        // The root is already open, so clicking it inspects; the child still has a subtree behind
        // it, so clicking it expands. The + in the label says which before it is clicked.
        Assert.IsTrue(markdown.Contains($"{DependencyMermaid.OpenScheme}"),
            "An expanded node links to its file.");
        Assert.IsTrue(markdown.Contains($"{DependencyMermaid.ExpandScheme}lib.dll"),
            "An unexpanded node links to an expand action.");
        Assert.IsTrue(markdown.Contains("+ lib.dll"), "An expandable node is marked with a +.");
        Assert.IsFalse(markdown.Contains("+ app.exe"), "An already-open node carries no + marker.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Unresolvable_modules_get_no_link()
    {
        var root = new DependencyNode("app.exe", DependencyKind.Resolved, @"C:\app\app.exe");
        root.Children.Add(new DependencyNode("missing.dll", DependencyKind.Missing));
        root.Children.Add(new DependencyNode("api-ms-win-core-x-l1-1-0.dll", DependencyKind.ApiSet));

        var markdown = DependencyMermaid.Build(new DependencyGraph(root, 3, false, 1));

        Assert.IsTrue(markdown.Contains("click n0 href"), "The root resolves, so it links.");
        Assert.IsFalse(markdown.Contains("click n1"), "A missing module has nothing to open.");
        Assert.IsFalse(markdown.Contains("click n2"), "An API set has no file on disk.");
        Assert.IsTrue(markdown.Contains("not found"));
        Assert.IsTrue(markdown.Contains("API set"));
    }

    [TestMethod, TestCategory("Unit")]
    public void Delay_loaded_edges_are_drawn_dashed()
    {
        var root = new DependencyNode("app.exe", DependencyKind.Resolved, @"C:\app\app.exe");
        root.Children.Add(new DependencyNode("late.dll", DependencyKind.Resolved, @"C:\app\late.dll")
        { IsDelayLoad = true });

        var markdown = DependencyMermaid.Build(new DependencyGraph(root, 2, false, 1));

        Assert.IsTrue(markdown.Contains("n0 -.-> n1"), "A delay-load edge is dashed.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Quotes_in_a_path_cannot_break_out_of_a_label()
    {
        var root = new DependencyNode("we\"ird.dll", DependencyKind.Resolved, "C:\\a\"b\\we\"ird.dll");
        var markdown = DependencyMermaid.Build(new DependencyGraph(root, 1, false, 0));

        Assert.IsFalse(markdown.Contains("we\"ird"), "A raw quote would terminate the label early.");
        Assert.IsTrue(markdown.Contains("&quot;"));
    }
}
