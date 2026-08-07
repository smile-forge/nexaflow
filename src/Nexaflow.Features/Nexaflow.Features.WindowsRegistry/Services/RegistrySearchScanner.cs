using System.IO;
using System.Security;
using Microsoft.Win32;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsRegistry.Services;

/// <summary>
/// Walks a registry subtree looking for a query, the way regedit's Find does: a key matches on its own
/// name, on the name of any value it holds, or on that value's data as the viewer would show it.
/// <para>
/// Pure IO — it opens keys through a root handle it is given and never touches the page, so the whole walk
/// runs off the UI thread. It is also the only part of registry search that can be slow, so everything
/// that bounds it lives here: a visited-key cap, a hit cap, and a cancellation token that is checked per
/// key rather than per subtree.
/// </para>
/// </summary>
internal static class RegistrySearchScanner
{
    /// <summary>One key that satisfied the query. A key is the unit because a key is what the viewer can
    /// navigate to — several matching values in one key collapse to one hit naming the first.</summary>
    /// <param name="SubPath">Path below the hive root, "" for the root itself.</param>
    /// <param name="ValueName">The value that matched, or null when the key's own name did.</param>
    /// <param name="Preview">What matched, for the agent to judge relevance without a second call.</param>
    internal readonly record struct KeyMatch(string SubPath, string? ValueName, string Preview);

    internal readonly record struct ScanResult(
        IReadOnlyList<KeyMatch> Matches, int KeysVisited, bool Truncated);

    /// <summary>
    /// Depth-first from <paramref name="basePath"/> under <paramref name="root"/>. Subtrees that can't be
    /// opened are skipped rather than reported: a walk of HKLM crosses hundreds of protected keys, and
    /// surfacing each one would bury the actual matches.
    /// </summary>
    internal static ScanResult Scan(
        RegistryKey root, string basePath, SearchRequest request,
        int keyCap, int hitCap, CancellationToken ct)
    {
        var matches = new List<KeyMatch>();
        var stack   = new Stack<string>();
        stack.Push(basePath);

        var visited   = 0;
        var truncated = false;

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            if (visited >= keyCap || matches.Count >= hitCap) { truncated = true; break; }

            var subPath = stack.Pop();
            visited++;

            RegistryKey? key;
            try { key = subPath.Length == 0 ? root : root.OpenSubKey(subPath, false); }
            catch (SecurityException)             { continue; }
            catch (UnauthorizedAccessException)   { continue; }
            catch (IOException)                   { continue; }
            if (key is null) continue;

            try
            {
                if (Match(key, subPath, request) is { } hit) matches.Add(hit);

                foreach (var child in SubKeyNames(key))
                    stack.Push(subPath.Length == 0 ? child : $"{subPath}\\{child}");
            }
            finally
            {
                // The hive root handle is a process-wide singleton (RegistryRoot.Key) — disposing it would
                // break every later read on this hive, including the value list the user is looking at.
                if (subPath.Length > 0) key.Dispose();
            }
        }

        return new ScanResult(matches, visited, truncated);
    }

    /// <summary>The key's own name first, then its values by name, then by displayed data — so the preview
    /// names the most specific thing that actually matched.</summary>
    private static KeyMatch? Match(RegistryKey key, string subPath, SearchRequest request)
    {
        var leaf = LeafOf(subPath);
        if (leaf.Length > 0 && request.Matches(leaf))
            return new KeyMatch(subPath, null, "key name");

        string[] names;
        try { names = key.GetValueNames(); }
        catch (SecurityException)           { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException)                 { return null; }

        foreach (var name in names)
        {
            if (name.Length > 0 && request.Matches(name))
                return new KeyMatch(subPath, name, $"value name '{name}'");

            string data;
            try
            {
                var kind = key.GetValueKind(name);
                data = RegistryValueCodec.FormatForDisplay(
                    key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), kind);
            }
            catch { continue; }   // a value that can't be read can't be matched; the rest of the key still can

            if (request.Matches(data))
                return new KeyMatch(subPath, name,
                    $"{(name.Length == 0 ? "(Default)" : name)} = {Trim(data)}");
        }

        return null;
    }

    private static string[] SubKeyNames(RegistryKey key)
    {
        try { return key.GetSubKeyNames(); }
        catch (SecurityException)           { return []; }
        catch (UnauthorizedAccessException) { return []; }
        catch (IOException)                 { return []; }
    }

    private static string LeafOf(string subPath)
    {
        var i = subPath.LastIndexOf('\\');
        return i < 0 ? subPath : subPath[(i + 1)..];
    }

    private static string Trim(string data) =>
        data.Length <= 120 ? data : data[..120] + "…";
}
