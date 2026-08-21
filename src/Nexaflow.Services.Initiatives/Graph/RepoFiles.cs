using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibGit2Sharp;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Enumerates the repo's code files (those a tree-sitter grammar covers), honouring <c>.gitignore</c> when the
/// root is a git working tree, else skipping the usual build/junk directories. Order is stable (ordinal) so the
/// generated graph diffs cleanly between runs.
/// </summary>
public static class RepoFiles
{
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".claude", ".product", "bin", "obj", "node_modules", ".vs", ".idea", "test-samples", "packages",
    };

    /// <summary>Structured (XML-ish) project files a tree-sitter grammar doesn't cover, but whose explicit
    /// references we can extract with certainty: project files (<c>.csproj</c> → project/package dependencies)
    /// and solutions (<c>.slnx</c>/<c>.sln</c> → member projects).
    /// <para>
    /// <c>.xaml</c> used to live here. It is real code to the parser now (the xml grammar, built from
    /// <c>external/tree-sitter-xml</c>), so it belongs to the code layer — which also gains it the
    /// content-addressed cache this lane doesn't have. The three sets must stay disjoint: only
    /// <see cref="EnumerateOther"/> excludes both, so an extension in <em>both</em> this set and a grammar
    /// would be walked by two layers and survive on nothing but first-wins node dedup.
    /// </para></summary>
    private static readonly HashSet<string> StructuredExts = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".slnx", ".sln",
    };

    /// <summary>The repo's code files (those a tree-sitter grammar covers).</summary>
    public static IReadOnlyList<string> EnumerateSource(string root, int max) =>
        Enumerate(root, max, TreeSitterLanguages.IsCode);

    /// <summary>The repo's structured project files (<c>.xaml</c>/<c>.csproj</c>/<c>.slnx</c>/<c>.sln</c>).</summary>
    public static IReadOnlyList<string> EnumerateStructured(string root, int max) =>
        Enumerate(root, max, f => StructuredExts.Contains(Path.GetExtension(f)));

    /// <summary>Every other repo file — docs, config, images, resources — that neither a grammar nor the structured
    /// extractors cover. Given a bare file node so a <c>mentions</c> edge has a target and an agent can at least
    /// locate an asset it can't yet parse.</summary>
    public static IReadOnlyList<string> EnumerateOther(string root, int max) =>
        Enumerate(root, max, f => !TreeSitterLanguages.IsCode(f) && !StructuredExts.Contains(Path.GetExtension(f)));

    private static IReadOnlyList<string> Enumerate(string root, int max, Func<string, bool> include)
    {
        var result = new List<string>();
        Repository? repo = null;
        try { if (Repository.IsValid(root)) repo = new Repository(root); } catch { repo = null; }

        try
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0 && result.Count < max)
            {
                var dir = stack.Pop();
                string[] files, subdirs;
                try
                {
                    files = Directory.GetFiles(dir);
                    subdirs = Directory.GetDirectories(dir);
                }
                catch { continue; }

                Array.Sort(files, StringComparer.Ordinal);
                Array.Sort(subdirs, StringComparer.Ordinal);

                foreach (var f in files)
                {
                    if (result.Count >= max) break;
                    if (!include(f)) continue;
                    if (IsIgnored(repo, root, f, isDir: false)) continue;
                    result.Add(f);
                }

                // Push in reverse so the stack pops in ordinal order.
                for (var i = subdirs.Length - 1; i >= 0; i--)
                {
                    var sub = subdirs[i];
                    if (SkipDirs.Contains(Path.GetFileName(sub))) continue;
                    if (IsIgnored(repo, root, sub, isDir: true)) continue;
                    stack.Push(sub);
                }
            }
        }
        finally { repo?.Dispose(); }

        return result;
    }

    private static bool IsIgnored(Repository? repo, string root, string fullPath, bool isDir)
    {
        if (repo is null) return false;
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (isDir) rel += "/";
        try { return repo.Ignore.IsPathIgnored(rel); } catch { return false; }
    }
}
