using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Pe;

namespace Nexaflow.Features.Executable.Services;

/// <summary>How a module in the dependency tree was resolved.</summary>
public enum DependencyKind
{
    /// <summary>Found on disk and parsed.</summary>
    Resolved,
    /// <summary>An <c>api-ms-win-*</c> / <c>ext-ms-*</c> name — a virtual module the loader redirects
    /// through the API set schema. Shown as a leaf; resolving it properly needs the schema from the PEB.</summary>
    ApiSet,
    /// <summary>Named by an import but not found along the search path.</summary>
    Missing,
    /// <summary>Already expanded elsewhere in the tree — the edge is drawn, the subtree is not repeated.</summary>
    Cycle,
    /// <summary>Not expanded because the depth or node limit was reached.</summary>
    Elided,
}

public sealed class DependencyNode(string name, DependencyKind kind, string? path = null)
{
    public string         Name     { get; } = name;
    public DependencyKind Kind     { get; } = kind;
    public string?        Path     { get; } = path;
    public List<DependencyNode> Children { get; } = [];

    /// <summary>Delay-loaded rather than bound at load time.</summary>
    public bool IsDelayLoad { get; init; }

    /// <summary>
    /// The <i>functions</i> the importing module pulls out of this one — not what sits behind it.
    /// The two are wildly different (three functions from <c>ole32</c>, which itself names eighty-odd
    /// modules), so anything user-facing has to say which it means.
    /// <para>
    /// Kept, not just counted: "which three?" is the question a count provokes, and for any edge
    /// below the root there is nowhere else in the app that could answer it — the Imports tab only
    /// knows what the binary you opened imports.
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ImportedFunctions { get; init; } = [];

    public int ImportedFunctionCount => ImportedFunctions.Count;

    /// <summary>True once the walk actually opened this module, whatever it found inside.</summary>
    public bool Walked { get; set; }

    /// <summary>A real module on disk that has not been opened up yet — it can be expanded.
    /// <para>
    /// Keyed off whether the walk has been <i>there</i>, not off whether it came back with anything:
    /// a module that imports nothing was still opened, and offering to open it again is an
    /// affordance that does nothing when clicked.
    /// </para></summary>
    public bool CanExpand => Kind == DependencyKind.Resolved && Path is { Length: > 0 } && !Walked;

    /// <summary>Already opened up, so the affordance is "close it again".</summary>
    public bool IsExpanded => Walked;
}

/// <param name="Truncated">True when a depth or node cap stopped the walk short of the full graph.</param>
public sealed record DependencyGraph(DependencyNode Root, int NodeCount, bool Truncated, int MaxDepthReached);

/// <summary>
/// Walks a binary's import tree, resolving each module the way the loader would.
/// <para>
/// The search order matters and is deliberately explicit: known DLLs are always taken from System32
/// regardless of what sits beside the binary (that is the whole point of the KnownDLLs list), and
/// only then does the application directory get a look in. Resolving in the wrong order would
/// produce a tree that disagrees with what actually loads at runtime.
/// </para>
/// </summary>
public sealed class DependencyWalker
{
    public const int DefaultMaxDepth = 3;
    public const int DefaultMaxNodes = 250;

    private readonly Dictionary<string, DependencyNode> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _expanding = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxDepth;
    private readonly int _maxNodes;

    private int  _nodeCount;
    private bool _truncated;
    private int  _deepest;

    public DependencyWalker(int maxDepth = DefaultMaxDepth, int maxNodes = DefaultMaxNodes)
    {
        _maxDepth = Math.Clamp(maxDepth, 1, 8);
        _maxNodes = Math.Clamp(maxNodes, 10, 5000);
    }

    /// <summary>
    /// Names the caller has chosen to open up. The root is always expanded; every other module is
    /// expanded only when it appears here.
    /// <para>
    /// Explicit expansion rather than a depth sweep, because a depth is the wrong control for this
    /// shape of graph: level two of a native binary is already unreadable, and the one subtree
    /// anybody actually wants to follow is buried in it.
    /// </para>
    /// </summary>
    private IReadOnlySet<string> _expanded = new HashSet<string>();

    /// <summary>Blocking — it opens and parses every module it resolves. Run it off the dispatcher.</summary>
    public DependencyGraph Walk(string rootPath, IReadOnlySet<string>? expanded = null,
                                CancellationToken ct = default)
    {
        _expanded = expanded ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var root = new DependencyNode(Path.GetFileName(rootPath), DependencyKind.Resolved, rootPath) { Walked = true };
        _resolved[root.Name] = root;
        _nodeCount = 1;

        Expand(root, rootPath, 0, ct);
        return new DependencyGraph(root, _nodeCount, _truncated, _deepest);
    }

    private void Expand(DependencyNode parent, string path, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _deepest = Math.Max(_deepest, depth);

        if (depth >= _maxDepth) { _truncated = true; return; }

        // Guard against a module that imports something that imports it back.
        if (!_expanding.Add(path)) return;

        try
        {
            using var image = PeReader.Read(path, new PeReadOptions
            {
                IncludeExports    = false,
                IncludeResources  = false,
                IncludeEntropy    = false,
                IncludeFileHashes = false,
                IncludeSectionHashes = false,
            });
            if (!image.IsPe) return;

            string? directory = Path.GetDirectoryName(path);

            foreach (var module in image.Imports.Concat(image.DelayImports))
            {
                ct.ThrowIfCancellationRequested();

                if (_nodeCount >= _maxNodes)
                {
                    _truncated = true;
                    parent.Children.Add(new DependencyNode("…", DependencyKind.Elided));
                    return;
                }

                // Already somewhere in the tree: draw the edge, do not re-expand. Without this a
                // graph of any real binary explodes — kernel32 and ntdll are reachable from
                // practically every node.
                if (_resolved.TryGetValue(module.Name, out var existing))
                {
                    parent.Children.Add(new DependencyNode(module.Name, DependencyKind.Cycle, existing.Path)
                    { IsDelayLoad = module.IsDelayLoad, ImportedFunctions = module.Functions.Select(f => f.Display).ToList() });
                    continue;
                }

                if (module.IsApiSet)
                {
                    var apiSet = new DependencyNode(module.Name, DependencyKind.ApiSet)
                    { IsDelayLoad = module.IsDelayLoad, ImportedFunctions = module.Functions.Select(f => f.Display).ToList() };
                    _resolved[module.Name] = apiSet;
                    parent.Children.Add(apiSet);
                    _nodeCount++;
                    continue;
                }

                string? resolvedPath = Resolve(module.Name, directory);
                var child = new DependencyNode(
                    module.Name,
                    resolvedPath is null ? DependencyKind.Missing : DependencyKind.Resolved,
                    resolvedPath)
                {
                    IsDelayLoad = module.IsDelayLoad,
                    ImportedFunctions = module.Functions.Select(f => f.Display).ToList(),
                };

                _resolved[module.Name] = child;
                parent.Children.Add(child);
                _nodeCount++;

                // Only follow what the caller asked to open up.
                if (resolvedPath is not null && _expanded.Contains(module.Name))
                {
                    child.Walked = true;
                    Expand(child, resolvedPath, depth + 1, ct);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable module is a leaf, not a failure of the whole walk.
        }
        finally
        {
            _expanding.Remove(path);
        }
    }

    /// <summary>
    /// The loader's search order, in order: KnownDLLs (always System32), the importing module's own
    /// directory, System32, the Windows directory, then PATH. Null when nothing on that path
    /// provides the module.
    /// <para>
    /// Public because the Imports tab needs the same answer for a single module — "where would this
    /// actually load from?" must not have two implementations that can disagree.
    /// </para>
    /// </summary>
    public static string? Resolve(string name, string? applicationDirectory)
    {
        if (!OperatingSystem.IsWindows()) return null;

        string system = Environment.SystemDirectory;

        // A known DLL is taken from System32 whatever sits next to the binary — that is precisely
        // what the KnownDLLs mechanism exists to guarantee, and it defeats side-by-side hijacking.
        if (KnownDlls.Contains(name))
        {
            string known = Path.Combine(system, name);
            if (File.Exists(known)) return known;
        }

        foreach (var directory in Candidates(applicationDirectory, system))
        {
            if (string.IsNullOrEmpty(directory)) continue;
            try
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { /* an unusable PATH entry */ }
        }
        return null;
    }

    private static IEnumerable<string?> Candidates(string? applicationDirectory, string system)
    {
        yield return applicationDirectory;
        yield return system;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "")
                              .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            yield return entry.Trim();
    }

    /// <summary>
    /// The core of HKLM\System\CurrentControlSet\Control\Session Manager\KnownDLLs. Hard-coded
    /// rather than read from the registry: this list has been stable for two decades, and a feature
    /// must not reach into the registry for something this small.
    /// </summary>
    private static readonly HashSet<string> KnownDlls = new(StringComparer.OrdinalIgnoreCase)
    {
        "ntdll.dll", "kernel32.dll", "kernelbase.dll", "advapi32.dll", "gdi32.dll", "user32.dll",
        "ole32.dll", "oleaut32.dll", "rpcrt4.dll", "shell32.dll", "shlwapi.dll", "msvcrt.dll",
        "combase.dll", "sechost.dll", "ws2_32.dll", "imagehlp.dll", "psapi.dll", "difxapi.dll",
        "clbcatq.dll", "coml2.dll", "comdlg32.dll", "normaliz.dll", "setupapi.dll", "wldap32.dll",
    };
}
