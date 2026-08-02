using System.IO;
using System.Runtime.InteropServices;

namespace Nexaflow.Features.WindowsSearch.Services;

/// <summary>How much of a location the Windows indexer actually covers.</summary>
public enum IndexCoverageKind
{
    /// <summary>The indexer couldn't be asked — service down, or the path isn't one it understands.
    /// Distinct from <see cref="None"/>: not knowing is not the same as knowing there is nothing.</summary>
    Unknown,

    /// <summary>Outside the crawl scope entirely. An empty result here says nothing about the files.</summary>
    None,

    /// <summary>In scope, but something beneath it carries its own rule — so part of the tree may be
    /// excluded and an empty result is only partly meaningful.</summary>
    Partial,

    /// <summary>In scope with no rules underneath. An empty result here really does mean "not found".</summary>
    Full,
}

/// <summary>One rule from the indexer's crawl scope.</summary>
/// <param name="PatternOrUrl">The path or pattern the rule applies to.</param>
/// <param name="IsIncluded">True to index, false to exclude.</param>
/// <param name="IsDefault">True for a rule Windows shipped, false for one someone added.</param>
public sealed record IndexScopeRule(string PatternOrUrl, bool IsIncluded, bool IsDefault)
{
    public override string ToString() =>
        $"{(IsIncluded ? "include" : "exclude")} {PatternOrUrl}{(IsDefault ? " (Windows default)" : "")}";
}

/// <summary>
/// Reads the indexer's crawl scope: whether a location is covered, and the rules that decide it.
/// <para>
/// This is what turns "no results" into an answer. The index returning nothing means something completely
/// different for an indexed folder than for one the indexer was never pointed at, and without asking there
/// is no way to tell the two apart — so the offer of a slow folder scan was made just as eagerly when it
/// was almost certainly a waste of the user's time.
/// </para>
/// <para>
/// Strictly read-only. Every mutating method on the underlying COM interface is left undeclared: this
/// explains the user's machine, it does not reconfigure it.
/// </para>
/// </summary>
public sealed class IndexCoverageReader : IDisposable
{
    private readonly Lock _gate = new();
    private ISearchCrawlScopeManager? _scope;
    private bool _initialised;
    private bool _disposed;

    /// <summary>How much of <paramref name="path"/> the indexer covers.</summary>
    public IndexCoverageKind Coverage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return IndexCoverageKind.Unknown;

        lock (_gate)
        {
            var scope = Scope();
            if (scope is null) return IndexCoverageKind.Unknown;

            try
            {
                var url = ToUrl(path);

                if (scope.IncludedInCrawlScope(url, out var included) != 0) return IndexCoverageKind.Unknown;
                if (!included) return IndexCoverageKind.None;

                // A rule below an included folder can only narrow what is covered — an exclusion, or an
                // inclusion with different settings. Either way the tree is no longer uniform.
                if (scope.HasChildScopeRule(url, out var hasChild) != 0) return IndexCoverageKind.Unknown;

                return hasChild ? IndexCoverageKind.Partial : IndexCoverageKind.Full;
            }
            catch (COMException) { return IndexCoverageKind.Unknown; }
        }
    }

    /// <summary>
    /// Every scope rule, most specific first. The answer to "why isn't my folder indexed?" — which is a
    /// question about rules, not about the search that just ran.
    /// </summary>
    public IReadOnlyList<IndexScopeRule> Rules()
    {
        lock (_gate)
        {
            var scope = Scope();
            if (scope is null) return [];

            IEnumSearchScopeRules? rules = null;
            try
            {
                rules = scope.EnumerateScopeRules();
                var found  = new List<IndexScopeRule>();
                var buffer = new ISearchScopeRule?[1];

                while (rules.Next(1, buffer, out var fetched) == 0 && fetched == 1)
                {
                    var rule = buffer[0];
                    buffer[0] = null;
                    if (rule is null) continue;

                    try
                    {
                        if (rule.get_PatternOrURL(out var pattern) != 0 || string.IsNullOrEmpty(pattern)) continue;
                        rule.get_IsIncluded(out var included);
                        rule.get_IsDefault(out var isDefault);
                        found.Add(new IndexScopeRule(pattern, included, isDefault));
                    }
                    finally { Marshal.ReleaseComObject(rule); }
                }

                return found;
            }
            catch (COMException) { return []; }
            finally { if (rules is not null) Marshal.ReleaseComObject(rules); }
        }
    }

    /// <summary>The rules that bear on <paramref name="path"/> — the ones a user asking "why isn't this
    /// indexed?" actually needs, rather than the machine's entire configuration.</summary>
    public IReadOnlyList<IndexScopeRule> RulesFor(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Rules();

        var normalised = path.Replace('/', '\\').TrimEnd('\\');

        return Rules()
            .Where(r => Mentions(r.PatternOrUrl, normalised))
            .ToList();
    }

    /// <summary>A rule bears on a path when either contains the other — a rule ON the folder, a rule on an
    /// ancestor that pulled it in, or a rule underneath that carved a hole in it.</summary>
    private static bool Mentions(string patternOrUrl, string path)
    {
        var rule = patternOrUrl.Replace('/', '\\').TrimEnd('\\');

        // Strip the protocol so "file:///C:/temp" and "C:\temp" compare as the same place.
        var at = rule.IndexOf(":\\\\", StringComparison.Ordinal);
        if (at >= 0) rule = rule[(at + 3)..].TrimStart('\\');

        return rule.StartsWith(path, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(rule, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The crawl scope speaks URLs; a folder path has to be presented as one.</summary>
    private static string ToUrl(string path)
    {
        if (path.Contains("://", StringComparison.Ordinal)) return path;

        var full = path.Replace('/', '\\');
        if (!full.EndsWith('\\') && Directory.Exists(full)) full += '\\';
        return "file:///" + full.Replace('\\', '/');
    }

    private ISearchCrawlScopeManager? Scope()
    {
        if (_initialised) return _scope;
        _initialised = true;

        try
        {
            var manager = (ISearchManager)new CSearchManager();
            _scope = manager.GetCatalog("SystemIndex").GetCrawlScopeManager();
        }
        catch (COMException)         { _scope = null; }
        catch (InvalidCastException) { _scope = null; }
        catch (UnauthorizedAccessException) { _scope = null; }

        return _scope;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            if (_scope is not null) Marshal.ReleaseComObject(_scope);
            _scope = null;
            _initialised = true;
        }
    }
}
