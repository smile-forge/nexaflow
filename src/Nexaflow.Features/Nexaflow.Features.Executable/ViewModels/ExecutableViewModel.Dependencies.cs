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
using Nexaflow.Markdown.Binding;
using Nexaflow.Visuals.Common.Localization;

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

        Dependencies.Walk();
    }

    /// <summary>The markdown the diagram is shown from: one binding, to the graph as far as the reader has opened it.</summary>
    private const string BoundDependencies = "```mermaid\n{{Dependencies}}\n```\n";

    private BoundGraph<DependencyGraph>? _dependencies;

    /// <summary>
    /// The import graph, walked only as far as the reader has opened it — what the diagram is bound to
    /// (<c>{{Dependencies}}</c>). A chip pressed in the diagram opens a module here without passing through this view-model,
    /// and every walk that lands is published to the tree and the detail pane as well.
    /// </summary>
    public BoundGraph<DependencyGraph> Dependencies => _dependencies ??= Bound();

    private BoundGraph<DependencyGraph> Bound()
    {
        var graph = new BoundGraph<DependencyGraph>(Walk, DependencyMermaid.Build, StringComparer.OrdinalIgnoreCase);
        graph.Walked += (_, walked) => _ = _shell.RunOnUiAsync(() => PublishDependencies(walked));
        return graph;
    }

    /// <summary>
    /// One walk, as a task in the shell's activity area. The walk opens and parses every module it resolves; what it walks is
    /// only what the reader opened, so the graph only ever grows where they pointed.
    /// </summary>
    private Task<DependencyGraph> Walk(IReadOnlySet<string> opened, CancellationToken ct)
    {
        var walked = new TaskCompletionSource<DependencyGraph>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => walked.TrySetCanceled(ct));

        _shell.QueueBackgroundTask(new DependencyTask(this, opened, walked), ct: ct);
        return walked.Task;
    }

    private sealed class DependencyTask(ExecutableViewModel owner, IReadOnlySet<string> opened,
                                        TaskCompletionSource<DependencyGraph> walked) : IBackgroundTask
    {
        public string Description => Str.Format("Executable.Task.MappingFormat", owner.FileName);

        public async Task RunAsync(CancellationToken ct)
        {
            using var either = CancellationTokenSource.CreateLinkedTokenSource(ct, owner._cts.Token);
            string path = owner.FilePath;

            try
            {
                // MaxDepth stays as a runaway guard only; what actually gets walked is the set opened.
                walked.TrySetResult(await Task.Run(() => new DependencyWalker(maxDepth: 8).Walk(path, opened, either.Token), either.Token));
            }
            catch (OperationCanceledException)
            {
                walked.TrySetCanceled(either.Token);
                throw;
            }
            catch (Exception error)
            {
                walked.TrySetException(error);
                throw;
            }
        }
    }

    /// <summary>The walk behind the current diagram and tree, so a selection can be resolved back to
    /// the module it names.</summary>
    private DependencyGraph? _dependencyGraph;

    private void PublishDependencies(DependencyGraph graph)
    {
        DependenciesLoading = false;
        _dependencyGraph    = graph;
        DependencyMarkdown  = BoundDependencies;

        DependencyNodes.Clear();
        DependencyNodes.Add(ToInspector(graph.Root));

        // A re-walk replaces every node, so the old selection is a dangling object; re-resolve it by
        // name, which is what the reader was actually pointing at.
        if (SelectedDependency is { } previous) SelectDependency(previous.Name);

        int expandable = Flatten(graph.Root).Count(n => n.CanExpand);
        DependencySummary = expandable == 0
            ? Str.Format("Executable.Deps.AllShownFormat", graph.NodeCount)
            : Str.Format("Executable.Deps.ExpandableFormat", graph.NodeCount, expandable);
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
                SelectedDependencyIsRoot ? Str.Get("Executable.Inspector.CurrentFile") : node.Kind switch
                {
                    DependencyKind.ApiSet  => Str.Get("Executable.Inspector.APISetResolvedByTheLoader"),
                    DependencyKind.Missing => Str.Get("Executable.Inspector.NotFoundOnTheLoaderSearch"),
                    DependencyKind.Cycle   => Str.Get("Executable.Inspector.AlreadyShownElsewhereInTheTree"),
                    DependencyKind.Elided  => Str.Get("Executable.Inspector.NotExpandedTheWalkHitIts"),
                    _                      => node.Path ?? Str.Get("Executable.Inspector.Resolved"),
                },
            };
            if (node.IsDelayLoad) parts.Add(Str.Get("Executable.Deps.DelayLoaded"));
            if (node.Walked)      parts.Add(Str.Format("Executable.Deps.ModulesBehindFormat", node.Children.Count));

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
        _ when SelectedDependencyIsRoot          => Str.Get("Executable.Inspector.ThisIsTheFileYouAre"),
        { ImportedFunctionCount: 0 } n when n.Kind == DependencyKind.Cycle
                                                 => Str.Get("Executable.Inspector.ShownHereAsARepeatSee"),
        { ImportedFunctionCount: 0 }             => Str.Get("Executable.Inspector.NothingIsImportedFromItBy"),
        { ImportedFunctionCount: 1 }             => Str.Get("Executable.Inspector.The1FunctionUsedFromIt"),
        var n                                    => Str.Format("Executable.Deps.FunctionsUsedCaptionFormat", n.ImportedFunctionCount),
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
    /// Opens up one module. Walks again rather than grafting onto the existing graph: the walk already owns cycle detection and
    /// the shared-module rules, and walking again is cheap next to keeping a second, subtly different merge path correct.
    /// </summary>
    public void ExpandModule(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || IsModuleExpanded(moduleName)) return;

        DependenciesLoading = true;
        Dependencies.Expand(moduleName, open: true);
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
        DependencyNodes.Clear();
        DependencyMarkdown = string.Empty;
        SelectedDependency = null;
        DependencyViewResetRequested?.Invoke();

        if (_image is null) return;

        _dependenciesRequested = true;
        DependenciesLoading    = true;
        Dependencies.Reset();
    }

    /// <summary>Closes one module back up, leaving the rest of the graph as it is.</summary>
    public void CollapseModule(string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleName) || !IsModuleExpanded(moduleName)) return;

        DependenciesLoading = true;
        Dependencies.Expand(moduleName, open: false);
    }

    /// <summary>Whether a module is currently opened up — the state a +/− affordance reflects.</summary>
    public bool IsModuleExpanded(string moduleName) => Dependencies.Opened.Contains(moduleName);

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
            DependencyKind.ApiSet  => Str.Get("Executable.Inspector.APISetResolvedByTheLoader"),
            DependencyKind.Missing => Str.Get("Executable.Inspector.NotFoundOnTheLoaderSearch"),
            DependencyKind.Cycle   => node.Path is { } p ? Str.Format("Executable.Deps.AlreadyShownAboveFormat", p) : Str.Get("Executable.Deps.AlreadyShownAbove"),
            DependencyKind.Elided  => Str.Get("Executable.Inspector.NotExpandedLimitReached"),
            _                      => node.Path ?? "",
        };
        // "N functions used", not "N imports": the number counts what the parent pulls out of this
        // module, which says nothing about how many modules open up behind it.
        if (node.ImportedFunctionCount > 0)
            detail = node.ImportedFunctionCount == 1 ? Str.Format("Executable.Deps.FunctionUsedFormat", node.ImportedFunctionCount, detail) : Str.Format("Executable.Deps.FunctionsUsedFormat", node.ImportedFunctionCount, detail);
        if (node.Walked && node.Children.Count > 0)
            detail = Str.Format("Executable.Deps.ModulesPrefixFormat", node.Children.Count, detail);
        if (node.IsDelayLoad)     detail = Str.Format("Executable.Deps.DelayLoadedPrefixFormat", detail);

        // A tree row is text, so it marks an unopened module with a "+" where the diagram draws a chip.
        var inspector = new InspectorNode(node.CanExpand ? $"+ {node.Name}" : node.Name, detail)
        {
            Payload    = node,
            IsExpanded = true,
        };
        foreach (var child in node.Children) inspector.Children.Add(ToInspector(child));
        return inspector;
    }

    /// <summary>Walks again from scratch, keeping whatever is currently opened.</summary>
    [RelayCommand]
    private void RefreshDependencies()
    {
        DependencyNodes.Clear();
        DependencyMarkdown = string.Empty;

        if (_image is null) return;

        _dependenciesRequested = true;
        DependenciesLoading    = true;
        Dependencies.Walk();
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
                Str.Format("Executable.Deps.ApiSetNoticeFormat", name));
            return;
        }

        string? resolved = knownPath is { Length: > 0 }
            ? knownPath
            : DependencyWalker.Resolve(name, Path.GetDirectoryName(FilePath));

        if (resolved is null || !File.Exists(resolved))
        {
            _shell.ShowError(Str.Format("Executable.Deps.NotFoundFormat", name));
            return;
        }

        string? folder = Path.GetDirectoryName(resolved);
        if (folder is null || !_shell.HandleObject(folder))
            _shell.ShowError(Str.Format("Executable.Deps.OpenFolderFailedFormat", name));
    }
}
