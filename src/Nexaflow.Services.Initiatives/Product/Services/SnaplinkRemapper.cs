using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Rewrites snaplink targets across a whole tree — the safe way to follow a file rename or a directory move,
/// so a churn like <c>Profile → Workspace</c> is one command instead of hand-edited JSON. Walks both node and
/// concern snaplinks; the caller persists the mutated <see cref="ProductState"/> via <see cref="ProductStore"/>
/// (whose serializer is canonical) and re-validates.
/// </summary>
public static class SnaplinkRemapper
{
    /// <summary>
    /// For every snaplink whose <c>doc</c> is (or sits under) <paramref name="oldPath"/>, rewrites that prefix
    /// to <paramref name="newPath"/> — so an exact file path renames one file, and a directory prefix moves a
    /// whole folder. When <paramref name="newClass"/> / <paramref name="newMethod"/> are given they are set on
    /// every affected link too (meaningful only for a single-file remap where the type was also renamed).
    /// Returns the number of snaplinks changed.
    /// </summary>
    public static int Remap(ProductState state, string oldPath, string newPath,
                            string? newClass = null, string? newMethod = null)
    {
        var prefix = Norm(oldPath);
        var replacement = Norm(newPath);
        var changed = 0;

        foreach (var node in state.Nodes.Values)
        {
            changed += RemapList(node.Snaplinks, prefix, replacement, newClass, newMethod);
            foreach (var concern in node.Concerns ?? [])
                changed += RemapList(concern.Snaplinks, prefix, replacement, newClass, newMethod);
        }
        return changed;
    }

    private static int RemapList(List<Snaplink>? links, string prefix, string replacement,
                                 string? newClass, string? newMethod)
    {
        if (links is null) return 0;
        var n = 0;
        foreach (var link in links)
        {
            if (link.Doc is not { } doc || !TryRewrite(doc, prefix, replacement, out var rewritten)) continue;
            link.Doc = rewritten;
            if (!string.IsNullOrEmpty(newClass)) link.Class = newClass;
            if (!string.IsNullOrEmpty(newMethod)) link.Method = newMethod;
            n++;
        }
        return n;
    }

    /// <summary>
    /// A doc matches when it equals the prefix (an exact file) or sits under it (a directory), using a path
    /// boundary so <c>Foo.cs</c> never accidentally matches <c>Foo.csproj</c>.
    /// </summary>
    private static bool TryRewrite(string doc, string prefix, string replacement, out string rewritten)
    {
        if (string.Equals(doc, prefix, StringComparison.OrdinalIgnoreCase))
        {
            rewritten = replacement;
            return true;
        }
        if (doc.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
        {
            rewritten = replacement + doc[prefix.Length..];
            return true;
        }
        rewritten = doc;
        return false;
    }

    private static string Norm(string p) => p.Replace('\\', '/').TrimEnd('/');
}
