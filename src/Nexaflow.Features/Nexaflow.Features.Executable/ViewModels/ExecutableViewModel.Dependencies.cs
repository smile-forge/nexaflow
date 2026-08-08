using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    private void PublishDependencies(DependencyGraph graph)
    {
        DependenciesLoading = false;
        DependencyMarkdown  = DependencyMermaid.Build(graph);

        DependencyNodes.Clear();
        DependencyNodes.Add(ToInspector(graph.Root));

        int expandable = Flatten(graph.Root).Count(n => n.CanExpand);
        DependencySummary = expandable == 0
            ? $"{graph.NodeCount} modules — everything reachable is shown."
            : $"{graph.NodeCount} modules · {expandable} can be expanded — click a + node to open it up.";
    }

    private static IEnumerable<DependencyNode> Flatten(DependencyNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Flatten(child))
                yield return descendant;
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

    /// <summary>Collapses everything back to the root's immediate imports.</summary>
    [RelayCommand]
    private void CollapseDependencies()
    {
        if (_expandedModules.Count == 0) return;

        _expandedModules.Clear();
        _dependenciesRequested = false;
        DependencyNodes.Clear();
        DependencyMarkdown = string.Empty;
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
        if (node.ImportCount > 0) detail = $"{node.ImportCount} imports · {detail}";
        if (node.IsDelayLoad)     detail = "delay-loaded · " + detail;

        // The + marker means the same thing in the tree as on the diagram.
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
        string? name = node?.Payload switch
        {
            PeImportModule module => module.Name,
            DependencyNode depend => depend.Name,
            _                     => null,
        };
        if (name is null) return;

        if (node!.Payload is PeImportModule { IsApiSet: true })
        {
            _shell.ShowNotification(
                $"{name} is an API set — a name the loader redirects through the API set schema, " +
                "not a file on disk.");
            return;
        }

        string? resolved = node.Payload is DependencyNode { Path: { Length: > 0 } known }
            ? known
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
