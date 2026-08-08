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
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
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
    public void Expanding_two_different_modules_opens_two_different_subtrees()
    {
        // The counts and the contents must both follow the module that was opened. Same-looking
        // results would mean the expansion set was not reaching the walk.
        var baseline = new DependencyWalker().Walk(PeFixtures.Notepad);
        var pair     = baseline.Root.Children.Where(c => c.CanExpand).Take(2).ToList();
        if (pair.Count < 2) Assert.Inconclusive("Need two expandable modules to tell them apart.");

        List<string> ChildrenOf(DependencyNode module) =>
            new DependencyWalker()
                .Walk(PeFixtures.Notepad, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { module.Name })
                .Root.Children.First(c => c.Name == module.Name)
                .Children.Select(c => c.Name).ToList();

        var first  = ChildrenOf(pair[0]);
        var second = ChildrenOf(pair[1]);

        CollectionAssert.AreNotEqual(first, second,
            $"{pair[0].Name} and {pair[1].Name} came back with the same imports.");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_module_that_imports_nothing_stops_offering_to_expand()
    {
        // Keyed off whether the walk has been there, not off whether it found anything: otherwise a
        // module with no imports keeps a [+] chip that does nothing when clicked.
        var node = new DependencyNode("empty.dll", DependencyKind.Resolved, @"C:\empty.dll");
        Assert.IsTrue(node.CanExpand, "It has not been opened yet.");

        node.Walked = true;
        Assert.IsFalse(node.CanExpand, "It has now, and it turned out to import nothing.");
        Assert.IsTrue(node.IsExpanded);
    }

    [TestMethod, TestCategory("Unit")]
    public void A_nodes_number_says_what_it_counts()
    {
        // "3 imports" reads as "three modules behind this", which is wrong by an order of magnitude —
        // it is how many functions the parent uses out of it.
        var root  = new DependencyNode("app.exe", DependencyKind.Resolved, @"C:\app\app.exe") { Walked = true };
        var child = new DependencyNode("ole32.dll", DependencyKind.Resolved, @"C:\app\ole32.dll")
        { ImportedFunctions = ["CoInitializeEx", "CoCreateInstance", "CoUninitialize"] };
        root.Children.Add(child);

        var markdown = DependencyMermaid.Build(new DependencyGraph(root, 2, false, 1));

        Assert.IsTrue(markdown.Contains("3 functions used"), "The label must say what the number counts.");
        Assert.IsFalse(markdown.Contains("3 imports"));
    }

    [TestMethod, TestCategory("Unit")]
    public void The_functions_an_edge_uses_are_kept_not_just_counted()
    {
        // The count on a node provokes exactly one question — which ones? — and for any edge below
        // the root nothing else in the app can answer it: the Imports tab only knows the root's.
        var graph  = new DependencyWalker().Walk(PeFixtures.Notepad);
        var module = graph.Root.Children.First(c => c.ImportedFunctionCount > 0);

        Assert.AreEqual(module.ImportedFunctionCount, module.ImportedFunctions.Count);
        Assert.IsTrue(module.ImportedFunctions.All(f => !string.IsNullOrWhiteSpace(f)),
            "every function the edge uses is named");
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
    public void Expansion_is_declared_in_front_matter_not_smuggled_into_labels_or_hrefs()
    {
        // The walk always opens the root, so a root built by hand has to say so too.
        var root = new DependencyNode("app.exe", DependencyKind.Resolved, @"C:\app\app.exe") { Walked = true };
        var child = new DependencyNode("lib.dll", DependencyKind.Resolved, @"C:\app\lib.dll");
        root.Children.Add(child);

        var markdown = DependencyMermaid.Build(new DependencyGraph(root, 2, false, 1));

        // The child still has a subtree behind it, and that fact lives in the nexaflow config block —
        // which stock mermaid ignores — rather than in a label or an href.
        Assert.IsTrue(markdown.Contains("  nexaflow:"), "The expansion state is front-matter config.");
        Assert.IsTrue(markdown.Contains("    collapsed:") && markdown.Contains("\"lib.dll\""),
            "An unopened module is declared collapsed, keyed by its own name.");

        Assert.IsFalse(markdown.Contains("+ lib.dll"), "No marker is smuggled into the node label.");
        Assert.IsFalse(markdown.Contains("nexaflow-expand:"), "No private href scheme survives.");
        Assert.IsFalse(markdown.Contains("nexaflow-open:"), "A node's href is just its path.");

        // Both nodes are real files, so both keep an ordinary click target — a node no longer has to
        // choose between being openable and being expandable.
        Assert.IsTrue(markdown.Contains(@"click n0 href ""C:\app\app.exe"""));
        Assert.IsTrue(markdown.Contains(@"click n1 href ""C:\app\lib.dll"""));
    }

    [TestMethod, TestCategory("Unit")]
    public void The_root_offers_no_collapse_because_the_walk_always_opens_it()
    {
        // The walk always opens the binary you are inspecting, so there is no state in which the root
        // is closed. A chip for it did nothing when clicked, and left the diagram believing the whole
        // graph was folded away behind a node that could then never be opened again.
        var root  = new DependencyNode("app.exe", DependencyKind.Resolved, @"C:\app\app.exe") { Walked = true };
        var child = new DependencyNode("lib.dll", DependencyKind.Resolved, @"C:\app\lib.dll") { Walked = true };
        child.Children.Add(new DependencyNode("deep.dll", DependencyKind.Resolved, @"C:\app\deep.dll"));
        root.Children.Add(child);

        var lines  = DependencyMermaid.Build(new DependencyGraph(root, 3, false, 2))
                                      .Split('\n', StringSplitOptions.None);
        var cfg = NexaflowConfigParser.Parse(MermaidFrontmatter.RawBlock(string.Join("\n", lines[1..^2])));

        Assert.IsFalse(cfg.Expanded.ContainsKey("n0"), "the root is never declared collapsible");
        Assert.AreEqual("lib.dll", cfg.Expanded["n1"], "…but an opened module below it still is");
    }

    [TestMethod, TestCategory("Unit")]
    public void The_front_matter_parses_back_into_the_expansion_state_it_declared()
    {
        var root   = new DependencyNode("app.exe", DependencyKind.Resolved, @"C:\app\app.exe") { Walked = true };
        var opened = new DependencyNode("lib.dll", DependencyKind.Resolved, @"C:\app\lib.dll") { Walked = true };
        opened.Children.Add(new DependencyNode("shut.dll", DependencyKind.Resolved, @"C:\app\shut.dll"));
        root.Children.Add(opened);

        // The fence is markdown; the diagram source is what is inside it.
        var lines  = DependencyMermaid.Build(new DependencyGraph(root, 3, false, 2))
                                      .Split('\n', StringSplitOptions.None);
        var source = string.Join("\n", lines[1..^2]);

        var cfg = NexaflowConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        Assert.AreEqual("lib.dll",  cfg.Expanded["n1"],  "an opened module can be closed again");
        Assert.AreEqual("shut.dll", cfg.Collapsed["n2"], "an unopened one can be opened");
        Assert.AreEqual(DependencyMermaid.MaxFanOut, cfg.MaxFanOut);
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
