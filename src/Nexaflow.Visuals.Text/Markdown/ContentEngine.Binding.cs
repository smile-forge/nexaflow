using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Binding;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What bindings standing where content would be (<c>{{Path}}</c>) come to, and following what they come to as it changes.
///
/// <para>
/// <strong>Read into the tree before anything works it over.</strong> The language says where its bindings stand and how
/// what they supply reads (<see cref="ContentLanguage.Bind"/>); the engine asks what the host showed the content against
/// what each comes to, so everything after sees supplied content as it sees the rest, and the source still prints as written.
/// </para>
/// <para>
/// <strong>Content that grows is followed, not polled.</strong> A binding coming to <see cref="IBoundContent"/> — a graph
/// opened a node at a time — says when it has changed; only the blocks holding a binding to it are laid out again, so
/// opening a node keeps everything else on the page as it was.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>The bound content the last readings came to, with every path that named each one.</summary>
    private readonly Dictionary<IBoundContent, HashSet<string>> _bound = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Raised when bound content the content was read against has changed — on whatever thread it changed on, so whatever
    /// shows the content moves to its own and hands it back to <see cref="Rebind"/>.
    /// </summary>
    internal event EventHandler<IBoundContent>? Rebound;

    /// <summary>
    /// <paramref name="tree"/> with what each binding in it comes to read into its place — as its language reads bindings,
    /// where it reads any.
    /// </summary>
    private ContentNode Bound(ContentLanguage language, ContentNode tree) =>
        language.Bind is { } bind ? bind(tree, Supplied) : tree;

    /// <summary>
    /// What the binding naming <paramref name="path"/> comes to, or null where the host showed the content against nothing
    /// that names it.
    /// </summary>
    private string? Supplied(string path)
    {
        if (_inputs.Data is not { } data || !data.TryGet(path, out var value) || value is null) return null;
        if (value is not IBoundContent bound) return BoundText.Said(value);

        Follow(bound, path);
        return bound.Text;
    }

    /// <summary>Follows <paramref name="bound"/> from now on, as what <paramref name="path"/> comes to.</summary>
    private void Follow(IBoundContent bound, string path)
    {
        if (!_bound.TryGetValue(bound, out var paths))
        {
            _bound[bound] = paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bound.Changed += OnBoundChanged;
        }

        paths.Add(path);
    }

    private void OnBoundChanged(object? sender, EventArgs args)
    {
        if (sender is IBoundContent bound) Rebound?.Invoke(this, bound);
    }

    /// <summary>
    /// Lays the content out again because <paramref name="changed"/> has — setting down again every block holding a binding
    /// to it, and every other block as it was.
    /// </summary>
    internal void Rebind(IBoundContent changed)
    {
        if (!_bound.TryGetValue(changed, out var paths)) return;

        _unchanged.Forget(block => Binds(block, paths));
        Relay();
    }

    /// <summary>Stops following everything bound content came to, for content shown against something else.</summary>
    private void Unbind()
    {
        foreach (var bound in _bound.Keys) bound.Changed -= OnBoundChanged;
        _bound.Clear();
    }

    /// <summary>Whether <paramref name="block"/> holds a binding naming any of <paramref name="paths"/>.</summary>
    private static bool Binds(ContentNode block, IReadOnlySet<string> paths)
    {
        var written = block.Print();
        return BoundText.Binds(written) && paths.Any(path => written.Contains(path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Tells the bound content every binding in <paramref name="diagram"/> comes to that the node <paramref name="key"/> names
    /// was opened, or closed again — what a chip pressed on it means. True where any was told, which then says when it has
    /// changed and so has the diagram laid out again.
    /// </summary>
    internal bool Expand(ContentPart diagram, string key, bool open)
    {
        var told = false;

        foreach (var node in diagram.Node.SelfAndDescendants())
        {
            if (node.Kind != Kinds.BoundContent || BoundText.Path(node.Text) is not { } path) continue;
            if (_inputs.Data is not { } data || !data.TryGet(path, out var value) || value is not IBoundContent bound) continue;

            bound.Expand(key, open);
            told = true;
        }

        return told;
    }
}
