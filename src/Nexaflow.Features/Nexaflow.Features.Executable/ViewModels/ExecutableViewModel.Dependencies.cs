using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Executable.Models;
using Nexaflow.Features.Executable.Services;
using Nexaflow.IO.Pe;

namespace Nexaflow.Features.Executable.ViewModels;

/// <summary>
/// The dependency tab. The walk opens and parses every module it resolves, so it never runs until
/// the tab is actually looked at.
/// </summary>
public sealed partial class ExecutableViewModel
{
    private void EnsureDependencies()
    {
        if (_dependenciesRequested || _image is null) return;
        _dependenciesRequested = true;
        DependenciesLoading    = true;

        _shell.QueueBackgroundTask(new DependencyTask(this), ct: _cts.Token);
    }

    /// <summary>Modules the user has opened up, by name. The root is always expanded.</summary>
    private readonly HashSet<string> _expandedModules = new(StringComparer.OrdinalIgnoreCase);

    private sealed class DependencyTask(ExecutableViewModel owner) : IBackgroundTask
    {
        public string Description => $"Mapping dependencies of {owner.FileName}…";

        public async Task RunAsync(CancellationToken ct)
        {
            string path     = owner.FilePath;
            var    expanded = new HashSet<string>(owner._expandedModules, StringComparer.OrdinalIgnoreCase);

            // MaxDepth stays as a runaway guard only; what actually gets walked is the explicit
            // expansion set, so the graph only ever grows where the user pointed.
            var graph = await Task.Run(() => new DependencyWalker(maxDepth: 8).Walk(path, expanded, ct), ct);

            ct.ThrowIfCancellationRequested();
            await owner._shell.RunOnUiAsync(() => owner.PublishDependencies(graph));
        }
    }

    /// <summary>The walk behind the current diagram and tree, so a selection can be resolved back to
    /// the module it names.</summary>
    private DependencyGraph? _dependencyGraph;

    private void PublishDependencies(DependencyGraph graph)
    {
        DependenciesLoading = false;
        _dependencyGraph    = graph;
        DependencyMarkdown  = DependencyMermaid.Build(graph);

        DependencyNodes.Clear();
        DependencyNodes.Add(ToInspector(graph.Root));

        // A re-walk replaces every node, so the old selection is a dangling object; re-resolve it by
        // name, which is what the reader was actually pointing at.
        if (SelectedDependency is { } previous) SelectDependency(previous.Name);

        int expandable = Flatten(graph.Root).Count(n => n.CanExpand);
        DependencySummary = expandable == 0
            ? $"{graph.NodeCount} modules — everything reachable is shown."
            : $"{graph.NodeCount} modules · {expandable} can be expanded — click a node's + chip to open it up.";
    }

    private static IEnumerable<DependencyNode> Flatten(DependencyNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
    }

    // ── The detail pane ───────────────────────────────────────────────────────

    /// <summary>
    /// The module the reader has picked out, in the diagram or in the tree. Both point at the same
    /// thing, so both feed the same pane rather than each growing an answer of its own.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDependencySelection))]
    [NotifyPropertyChangedFor(nameof(SelectedDependencyDetail))]
    [NotifyPropertyChangedFor(nameof(SelectedDependencyFunctionsCaption))]
    [NotifyPropertyChangedFor(nameof(SelectedDependencyIsRoot))]
    private DependencyNode? _selectedDependency;

    public bool HasDependencySelection => SelectedDependency is not null;

    /// <summary>The one-line "what is this" under the module's name.</summary>
    public string SelectedDependencyDetail
    {
        get
        {
            if (SelectedDependency is not { } node) return string.Empty;

            var parts = new List<string>(3)
            {
                SelectedDependencyIsRoot ? "Current file" : node.Kind switch
                {
                    DependencyKind.ApiSet  => "API set — resolved by the loader through the API set schema",
                    DependencyKind.Missing => "Not found on the loader search path",
                    DependencyKind.Cycle   => "Already shown elsewhere in the tree",
                    DependencyKind.Elided  => "Not expanded — the walk hit its limit",
                    _                      => node.Path ?? "Resolved",
                },
            };
            if (node.IsDelayLoad) parts.Add("delay-loaded");
            if (node.Walked)      parts.Add($"{node.Children.Count} modules behind it");

            return string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// What the function list is a list <i>of</i>. Worth spelling out: the functions belong to the
    /// edge, not to the module — they are what its importer uses, not everything it offers.
    /// </summary>
    /// <summary>True for the binary being inspected — the root of its own import tree, which nothing
    /// in this graph imports from.</summary>
    public bool SelectedDependencyIsRoot =>
        SelectedDependency is not null && ReferenceEquals(SelectedDependency, _dependencyGraph?.Root);

    public string SelectedDependencyFunctionsCaption => SelectedDependency switch
    {
        null                                     => string.Empty,
        // The root is the file you opened: the list is empty because nothing here imports *from* it,
        // not because it is a leaf. Saying "nothing is imported" would read as a fact about the file.
        _ when SelectedDependencyIsRoot          => "This is the file you are inspecting, so nothing here imports from it.",
        { ImportedFunctionCount: 0 } n when n.Kind == DependencyKind.Cycle
                                                 => "Shown here as a repeat — see the first occurrence for what it is used for.",
        { ImportedFunctionCount: 0 }             => "Nothing is imported from it by name (it may be bound, or imported by ordinal only).",
        { ImportedFunctionCount: 1 }             => "The 1 function used from it:",
        var n                                    => $"The {n.ImportedFunctionCount} functions used from it:",
    };

    /// <summary>Jumps to the tab that does have the root's function detail.</summary>
    [RelayCommand]
    private void ShowImportsSection() => SelectedSection = Sections.ImportsExports;

    [RelayCommand]
    private void OpenSelectedDependency()
    {
        if (SelectedDependency?.Path is { Length: > 0 } path) OpenDependency(path);
    }

    [RelayCommand]
    private void LocateSelectedDependency()
    {
        if (SelectedDependency is { } node) LocateByName(node.Name, node.Path, node.Kind == DependencyKind.ApiSet);
    }

    /// <summary>Picks out a module by name — the one thing the diagram and the tree agree on.</summary>
    public void SelectDependency(string? moduleName)
    {
        SelectedDependency = moduleName is null || _dependencyGraph is null
            ? null
            : Flatten(_dependencyGraph.Root)
                .FirstOrDefault(n => string.Equals(n.Name, moduleName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Opens up one module. Re-walks rather than grafting onto the existing graph: the walk already
    /// owns cycle detection and the shared-module rules, and re-running it is cheap next to keeping
    /// a second, subtly different merge path correct.
    /// </summary>
    public void ExpandModule(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) return;
        if (!_expandedModules.Add(moduleName)) return;

        _dependenciesRequested = false;
        DependenciesLoading    = true;
        EnsureDependencies();
    }

    /// <summary>
    /// Raised when the diagram should forget where the reader had got to — what they had opened,
    /// selected and zoomed to. Only "collapse all" means that; a refresh keeps its expansions and so
    /// should keep the view with them.
    /// </summary>
    public event Action? DependencyViewResetRequested;

    /// <summary>Collapses everything back to the root's immediate imports.</summary>
    [RelayCommand]
    private void CollapseDependencies()
    {
        _expandedModules.Clear();
        _dependenciesRequested = false;
        DependencyNodes.Clear();
        DependencyMarkdown = string.Empty;
        SelectedDependency = null;
        DependencyViewResetRequested?.Invoke();
        EnsureDependencies();
    }

    /// <summary>Closes one module back up, leaving the rest of the graph as it is.</summary>
    public void CollapseModule(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName)) return;
        if (!_expandedModules.Remove(moduleName)) return;

        _dependenciesRequested = false;
        DependenciesLoading    = true;
        EnsureDependencies();
    }

    /// <summary>Whether a module is currently opened up — the state a +/− affordance reflects.</summary>
    public bool IsModuleExpanded(string moduleName) => _expandedModules.Contains(moduleName);

    [RelayCommand]
    private void ExpandModuleNode(InspectorNode? node)
    {
        if (node?.Payload is DependencyNode { CanExpand: true } dependency)
            ExpandModule(dependency.Name);
    }

    [RelayCommand]
    private void CollapseModuleNode(InspectorNode? node)
    {
        if (node?.Payload is DependencyNode { IsExpanded: true } dependency)
            CollapseModule(dependency.Name);
    }

    private static InspectorNode ToInspector(DependencyNode node)
    {
        string detail = node.Kind switch
        {
            DependencyKind.ApiSet  => "API set — resolved by the loader through the API set schema",
            DependencyKind.Missing => "Not found on the loader search path",
            DependencyKind.Cycle   => $"already shown above{(node.Path is { } p ? $" — {p}" : "")}",
            DependencyKind.Elided  => "not expanded (limit reached)",
            _                      => node.Path ?? "",
        };
        // "N functions used", not "N imports": the number counts what the parent pulls out of this
        // module, which says nothing about how many modules open up behind it.
        if (node.ImportedFunctionCount > 0)
            detail = $"{node.ImportedFunctionCount} function{(node.ImportedFunctionCount == 1 ? "" : "s")} used · {detail}";
        if (node.Walked && node.Children.Count > 0)
            detail = $"{node.Children.Count} modules · {detail}";
        if (node.IsDelayLoad)     detail = "delay-loaded · " + detail;

        // A tree row is text, so it marks an unopened module with a "+" where the diagram draws a chip.
        var inspector = new InspectorNode(node.CanExpand ? $"+ {node.Name}" : node.Name, detail)
        {
            Payload    = node,
            IsExpanded = true,
        };
        foreach (var child in node.Children) inspector.Children.Add(ToInspector(child));
        return inspector;
    }

    /// <summary>Re-walks from scratch, keeping whatever is currently expanded.</summary>
    [RelayCommand]
    private void RefreshDependencies()
    {
        _dependenciesRequested = false;
        DependencyNodes.Clear();
        DependencyMarkdown = string.Empty;
        EnsureDependencies();
    }

    /// <summary>
    /// A clicked mermaid node, or a double-clicked tree row: open that dependency in its own
    /// inspector tab. The new page builds its own breadcrumbs from the path, so the trail back to
    /// the module's folder comes for free.
    /// </summary>
    public bool OpenDependency(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        if (string.Equals(path, FilePath, StringComparison.OrdinalIgnoreCase)) return true;

        _shell.OpenTab(ExecutableTabRegistration.StaticPageKind,
                       new Dictionary<string, string> { ["path"] = path });
        return true;
    }

    [RelayCommand]
    private void OpenDependencyNode(InspectorNode? node)
    {
        if (node?.Payload is DependencyNode dependency) OpenDependency(dependency.Path);
    }

    /// <summary>
    /// Opens a file-browser tab at the folder an imported module would actually load from, resolved
    /// through the same loader search order the dependency walk uses. Handed to the shell's object
    /// dispatch, so the file-system feature claims it and this one stays ignorant of it.
    /// </summary>
    [RelayCommand]
    private void LocateModule(InspectorNode? node)
    {
        switch (node?.Payload)
        {
            case PeImportModule module: LocateByName(module.Name, null, module.IsApiSet); break;
            case DependencyNode depend: LocateByName(depend.Name, depend.Path, depend.Kind == DependencyKind.ApiSet); break;
        }
    }

    /// <summary>Opens the folder a module would actually load from, resolved through the same loader
    /// search order the walk uses.</summary>
    private void LocateByName(string name, string? knownPath, bool isApiSet)
    {
        if (isApiSet)
        {
            _shell.ShowNotification(
                $"{name} is an API set — a name the loader redirects through the API set schema, " +
                "not a file on disk.");
            return;
        }

        string? resolved = knownPath is { Length: > 0 }
            ? knownPath
            : DependencyWalker.Resolve(name, Path.GetDirectoryName(FilePath));

        if (resolved is null || !File.Exists(resolved))
        {
            _shell.ShowError($"{name} was not found on the loader search path.");
            return;
        }

        string? folder = Path.GetDirectoryName(resolved);
        if (folder is null || !_shell.HandleObject(folder))
            _shell.ShowError($"Could not open the folder containing {name}.");
    }
}
