using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Venn.Stages;

/// <summary>
/// Works out which region each part of a Venn diagram stands for, and says so where one names a region that is not there.
///
/// <para>
/// None of it is in a line's own characters. <c>union A,B</c> is the overlap of two sets only where <c>set A</c> and
/// <c>set B</c> were written above it; <c>style A</c> styles a set or an item of that name wherever in the block it is;
/// and an item not gathered into a region (<see cref="GroupRegions"/>) sits in the one it names, or else the one written
/// last. So it is worked out here, in the order written, and hung underneath as a key (<see cref="VennRoles.Key"/>): on
/// each region its set's name, or its union's names sorted with a comma between each — so <c>B,A</c> and <c>A,B</c> are
/// one overlap — on each item standing on its own the region it sits in, and on each style what it styles.
/// </para>
/// <para>
/// An item written against Mermaid's indentation rule is still put where it plainly belongs, with the reason: the
/// drawing should not lose it for being indented wrongly.
/// </para>
/// </summary>
public sealed class ResolveRegions : IAstStage
{
    public string Name => "venn:resolve-regions";

    public ContentNode Run(ContentNode tree)
    {
        // What the block names anywhere — which is what a style may style, wherever it is written.
        var sets = new HashSet<string>(StringComparer.Ordinal);
        var unions = new HashSet<string>(StringComparer.Ordinal);
        var items = new HashSet<string>(StringComparer.Ordinal);

        foreach (var said in Statements(tree))
        {
            switch (said.Kind)
            {
                case VennKinds.Set: sets.Add(Named(said)); break;
                case VennKinds.Union: unions.Add(Key(Names(said))); break;
                case VennKinds.Text: items.Add(Named(said)); break;
            }
        }

        var known = new HashSet<string>(StringComparer.Ordinal);
        string? current = null;

        // Mermaid's rule: after a set or a union, indented items are in it — until a line is written at the start of its row.
        var indenting = false;

        var parts = new List<ContentNode>(tree.Children.Count);
        var moved = false;

        foreach (var part in tree.Children)
        {
            var read = part;

            if (part.Kind == VennKinds.Region)
            {
                read = Region(part);
                indenting = true;
            }
            else if (GroupRegions.Said(part) is { Kind: not (Kinds.Comment or MermaidKinds.Directive) } said)
            {
                var indented = GroupRegions.Indented(part);
                if (!indented) indenting = false;

                var resolved = said.Kind switch
                {
                    VennKinds.Text => Item(said, indented),
                    VennKinds.Style => Styled(said),
                    _ => said,
                };

                read = Replaced(part, said, resolved);
            }

            moved |= !ReferenceEquals(read, part);
            parts.Add(read);
        }

        return moved ? tree.With(parts) : tree;

        ContentNode Region(ContentNode region)
        {
            var first = region.Children[0];
            var said = GroupRegions.Said(first)!;

            string key;
            ContentNode resolved;

            if (said.Kind == VennKinds.Set)
            {
                key = Named(said);
                known.Add(key);
                resolved = said;
            }
            else
            {
                var names = Names(said);
                key = Key(names);
                resolved = said;

                var unknown = names.Where(name => name.Length > 0 && !known.Contains(name)).ToHashSet(StringComparer.Ordinal);
                if (unknown.Count > 0)
                    resolved = AstRewrite.Each(resolved, node =>
                        node is { Kind: VennKinds.Name, Role: VennRoles.Id } && unknown.Contains(node.Text)
                            ? node.Saying($"'{node.Text}' is not a set written above this union.")
                            : node);

                // One still being written is not yet a union of too few.
                if (names.All(name => name.Length > 0) && names.Distinct(StringComparer.Ordinal).Count() < 2)
                    resolved = Within(resolved, VennKinds.Sets, list => list.Saying("A union is where two sets or more overlap: union A,B."));
            }

            current = key;

            var line = Replaced(first, said, resolved);
            var regrouped = ReferenceEquals(line, first) ? region : region.With([line, .. region.Children.Skip(1)]);
            return regrouped.Saying(VennKinds.Fact, VennRoles.Key, key);
        }

        ContentNode Item(ContentNode item, bool indented)
        {
            if (item.Part(VennRoles.Region) is { } region)
            {
                var key = Key(Names(region));

                if (indenting && indented)
                    item = item.Saying("Indented under a set or a union, a text item is its name and its label: it sits in the region above it.");
                else if (!sets.Contains(key) && !unions.Contains(key))
                    item = Within(item, VennKinds.Sets, names => names.Saying($"No set or union {key} is written for this item to sit in."));

                return item.Saying(VennKinds.Fact, VennRoles.Key, key);
            }

            if (current is null)
                return item.Saying("A text item sits in the set or union it is indented under, or names its region first: text A,B AB1[\"OpenAPI\"].");

            if (!(indenting && indented))
                item = item.Saying("A text item written at the start of a line names its region first: text A,B AB1[\"OpenAPI\"].");

            return item.Saying(VennKinds.Fact, VennRoles.Key, current);
        }

        ContentNode Styled(ContentNode style)
        {
            if (style.Part(VennRoles.Target) is not { } target) return style;

            var names = Names(target);
            var key = Key(names);
            var there = names.Count(name => name.Length > 0) > 1 ? unions.Contains(key) : sets.Contains(key) || items.Contains(key);

            if (!there && names.All(name => name.Length > 0))
                style = Within(style, VennKinds.Sets, list => list.Saying($"Nothing called {key} is written to style."));

            return style.Saying(VennKinds.Fact, VennRoles.Key, key);
        }
    }

    /// <summary>Everything the block's lines say, in the order written — inside the regions and out.</summary>
    private static IEnumerable<ContentNode> Statements(ContentNode tree) =>
        tree.Children
            .SelectMany(part => part.Kind == VennKinds.Region ? part.Children : [part])
            .Select(GroupRegions.Said)
            .OfType<ContentNode>();

    /// <summary>The name a set or an item is written with, without its quotes.</summary>
    private static string Named(ContentNode said) =>
        said.Children.FirstOrDefault(child => child.Kind == VennKinds.Id)?.Children
            .FirstOrDefault(child => child.Kind == VennKinds.Name)?.Text ?? string.Empty;

    /// <summary>Every name a line or a list of names lists, in the order written — an empty one where a name is still to be written.</summary>
    private static List<string> Names(ContentNode node)
    {
        var list = node.Kind == VennKinds.Sets ? node : node.Children.FirstOrDefault(child => child.Kind == VennKinds.Sets);
        if (list is null) return [];

        return [.. list.Children
            .Where(child => child.Kind == VennKinds.Id)
            .Select(id => id.Children.FirstOrDefault(child => child.Kind == VennKinds.Name)?.Text ?? string.Empty)];
    }

    /// <summary>
    /// The key a region is known by: its names, sorted, with a comma between each — as Mermaid knows it, so the same overlap
    /// written in another order is the same overlap.
    /// </summary>
    public static string Key(IEnumerable<string> names) =>
        string.Join(",", names.Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    /// <summary>The same statement with its part of <paramref name="kind"/> made over.</summary>
    private static ContentNode Within(ContentNode said, string kind, Func<ContentNode, ContentNode> made) =>
        said.With([.. said.Children.Select(child => child.Kind == kind ? made(child) : child)]);

    /// <summary>The same line with what it says replaced — or the line itself, where nothing was.</summary>
    private static ContentNode Replaced(ContentNode line, ContentNode said, ContentNode resolved) =>
        ReferenceEquals(said, resolved) ? line : line.With([.. line.Children.Select(child => ReferenceEquals(child, said) ? resolved : child)]);
}
