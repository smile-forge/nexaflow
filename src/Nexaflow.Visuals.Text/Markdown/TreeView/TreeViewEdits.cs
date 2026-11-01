using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Layout.TreeView;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.TreeView;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.TreeView;

/// <summary>
/// What an edit means in a tree view, and what it offers where it is right-clicked — an outline a reader works with as they would a
/// folder tree.
///
/// <para>
/// A label is typed into where it is drawn: bare for as long as nothing in it would read as something else, and put in quotes as
/// soon as something would — a label put in starts in quotes so it is there before anything is written in it, and is written bare
/// once what is typed into it can be. What is said beside a node is typed into where it is drawn too. Enter in a node puts one in
/// after it, the caret in its label; Tab and Shift+Tab take it a level in, under the node before it, and out, after the node it is
/// under; Page Up and Page Down move it up and down among its siblings. Backspace in a label with nothing written in it takes the node
/// out.
/// </para>
/// <para>
/// A right-click on a node offers to turn it into a folder or a file, to put one in before it, after it or inside it, to take it a
/// level in or out where it can go, to take it out, and words beside it where it has none; anywhere else in the tree, a node after
/// everything. Everywhere in the tree it offers to show or hide the icons beside every node, in the front matter. With whole
/// nodes picked out, Delete takes them out and Insert puts one in before them; and a node taken hold of by its row — picked out first
/// or not — and let go on another goes in front of it, after it or inside it, as the top, bottom or middle of that row says. A node
/// always goes with everything under it, and a tree drawn with a box drawing is drawn again round it (<see cref="TreeViewWriting"/>).
/// </para>
/// </summary>
internal sealed class TreeViewEdits : IContentLanguage, IOnEdit, IOnMove
{
    private const string Named = "treeview.";

    private const string Before = Named + "insert.before";
    private const string After = Named + "insert.after";
    private const string Inside = Named + "insert.inside";
    private const string Last = Named + "insert.end";
    private const string Deeper = Named + "level.in";
    private const string Shallower = Named + "level.out";
    private const string Delete = Named + "delete";
    private const string Describe = Named + "describe";

    /// <summary>The button turning a node into a folder, or a folder into a file.</summary>
    private const string Kind = Named + "kind";

    /// <summary>The button turning the icons beside every node on or off, and the front matter's field it writes.</summary>
    private const string Icons = Named + "icons";
    private static readonly string[] ShowIcons = ["config", "treeView", "showIcons"];

    /// <inheritdoc/>
    public IOnEdit OnEdit => this;

    /// <inheritdoc/>
    public IOnMove OnMove => this;

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Typing => DiagramWriting.Typed(edit, Escaping),
        EditKind.Settling => edit.Text == "\n" ? Entered(edit) : DiagramWriting.Typed(edit, Escaping),
        EditKind.Pasting => Pasted(edit),
        EditKind.Erasing or EditKind.Deleting => Chosen(edit) is { Count: > 0 } chosen ? TreeViewWriting.Removed(edit.Root, chosen) : Erased(edit),
        EditKind.Inserting => Targets(edit) is [var first, ..] ? TreeViewWriting.Added(edit.Root, first, OutlineSide.Before) : null,
        EditKind.Tabbing => Leveled(edit, TreeViewWriting.Deeper),
        EditKind.TabbingBack => Leveled(edit, TreeViewWriting.Shallower),
        EditKind.Raising => Leveled(edit, TreeViewWriting.Raised),
        EditKind.Lowering => Leveled(edit, TreeViewWriting.Lowered),
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask)
    {
        if (ask.IsReadOnly || ask.Root is not { } root) return [];

        var icons = Pressing(Icons, Shown(root) ? "Text.TreeView.Icons.Hide" : "Text.TreeView.Icons.Show", Shown(root) ? "eye_off" : "eye",
                             static edit => FrontMatterWriting.Configuring(edit, ShowIcons, Shown(edit.Root) ? "false" : "true"));

        if (TreeViewWriting.ItemOf(ask.Part) is not { } item)
            return [Pressing(Last, "Text.TreeView.Node.End", "add_square", static edit => TreeViewWriting.Added(edit.Root, null, OutlineSide.After)), icons];

        var outline = TreeViewWriting.Outline(root);
        var folder = TreeViewWriting.Folder(item);

        List<MiniRibbonControl> controls =
        [
            Pressing(Kind, folder ? "Text.TreeView.Node.AsFile" : "Text.TreeView.Node.AsFolder", folder ? "document" : "folder",
                     static edit => Over(edit) is { } over ? TreeViewWriting.Foldered(over, !TreeViewWriting.Folder(over), edit.State.Caret) : null)
                with { Caption = Str.Get("Text.TreeView.Node.As") },
            Pressing(Before, "Text.TreeView.Node.Before", "table_stack_above",
                     static edit => Over(edit) is { } over ? TreeViewWriting.Added(edit.Root, over, OutlineSide.Before) : null),
            Pressing(After, "Text.TreeView.Node.After", "table_stack_below",
                     static edit => Over(edit) is { } over ? TreeViewWriting.Added(edit.Root, over, OutlineSide.After) : null),
            Pressing(Inside, "Text.TreeView.Node.Inside", "add_square",
                     static edit => Over(edit) is { } over ? TreeViewWriting.AddedInside(edit.Root, over) : null),
            Pressing(Deeper, "Text.TreeView.Node.Deeper", "text_indent_increase_ltr",
                     static edit => Over(edit) is { } over ? TreeViewWriting.Deeper(edit.Root, [over], edit.State.Caret) : null)
                with { Disabled = !outline.CanGoDeeper([item]) },
            Pressing(Shallower, "Text.TreeView.Node.Shallower", "text_indent_decrease_ltr",
                     static edit => Over(edit) is { } over ? TreeViewWriting.Shallower(edit.Root, [over], edit.State.Caret) : null)
                with { Disabled = !outline.CanGoShallower([item]) },
        ];

        if (TreeViewWriting.Describable(item))
            controls.Add(Pressing(Describe, "Text.TreeView.Node.Describe", "text_description", static edit => Over(edit) is { } over ? TreeViewWriting.Described(over) : null));

        controls.Add(icons);
        controls.Add(Pressing(Delete, "Text.TreeView.Node.Delete", "delete",
                              static edit => Over(edit) is { } over ? TreeViewWriting.Removed(edit.Root, [over]) : null) with { Warns = true });

        return controls;

        // Done to the node under the pointer, so each stands beside the rest as its icon, named to a reader hovering over it. What a node
        // is turned into is drawn after "As": the folder or the file alone would read as a choice of the icon it is drawn with.
        static MiniRibbonButton Pressing(string id, string name, string icon, Func<ContentEdit, ContentChange?> pressed) =>
            new(id, Str.Get(name), pressed) { Shape = DiagramRibbon.Drawn(IconRef.Fluent(icon)) };
    }

    /// <summary>Whether the tree's front matter shows an icon beside every node.</summary>
    private static bool Shown(ContentPart root) => ((root.Node as ConfiguredNode<TreeViewConfig>)?.Config ?? TreeViewConfig.Default).ShowIcons;

    /// <summary>The node a control of the ribbon was used over, or null where it was used over none.</summary>
    private static ContentPart? Over(ContentEdit edit) => TreeViewWriting.ItemOf(edit.Part);

    /// <inheritdoc/>
    /// <remarks>
    /// In a tree view's words — a label, what is said beside a node, the title — every one of the clipboard's ways means what it says,
    /// and a paste goes in as the parser spells it there. Over whole nodes only a copy: taken away as characters they would leave the
    /// lines round them as they were, which is what taking a node out does instead.
    /// </remarks>
    public ClipboardOffers Clipboard(ContentAsk ask)
    {
        if (ask.Root is not { } root) return ClipboardOffers.None;

        if (ask.Chosen is { } chosen)
            return Written(root, root.Start + chosen.Start, root.Start + chosen.Start + chosen.Length) is not null ? ClipboardOffers.All : ClipboardOffers.Copy;

        return ask.Caret is { } caret && Written(root, caret, caret) is not null ? ClipboardOffers.Paste : ClipboardOffers.None;
    }

    /// <summary>
    /// A paste in a tree view's words, written where it was put: into a label, the whole label written again from what it then says,
    /// so it is put in quotes where what is pasted needs them; anywhere else in words, as the parser spells it there. Anywhere else,
    /// nothing.
    /// </summary>
    private static ContentChange Pasted(ContentEdit edit)
    {
        var state = edit.State;
        var (from, to) = state.HasSelection ? (state.Selection[0].Start, state.Selection[^1].End) : (state.Caret, state.Caret);

        if (state.Selection.Count > 1 || Written(edit.Root, from, to) is not { } words) return ContentChange.Stay(state);

        if (TreeViewParser.LabelOf(words) is { } label)
        {
            var said = words.Text[..(from - words.Start)] + edit.Text + words.Text[(to - words.Start)..];
            var spelled = TreeViewParser.Labelled(said, TreeViewParser.Quote(label), TreeViewParser.Folder(label));
            var opened = spelled[0] is '"' or '\'' ? 1 : 0;

            return new ContentChange([ContentWrite.Words(label, label.Start, label.Length, said)],
                                     label.Start + Math.Min(spelled.Length, opened + (from - words.Start) + edit.Text.Length));
        }

        return new ContentChange([ContentWrite.Words(words, from, to - from, edit.Text)], from + edit.Text.Length);
    }

    /// <summary>The words the stretch from <paramref name="from"/> to <paramref name="to"/> lies in — a label's, a description's or the title's — or null.</summary>
    private static ContentPart? Written(ContentPart root, int from, int to) =>
        root.SelfAndDescendants().FirstOrDefault(part => part.Start <= from && to <= part.End && !part.Derived && part.Kind == Kinds.Words
                                                         && part.Role is TreeViewRoles.Label or TreeViewRoles.Description or MermaidRoles.Title);

    // ── Keys ────────────────────────────────────────────────────────────────

    /// <summary>Enter in a node: a node put in after it, the caret in its label. Anywhere else, what Enter does anywhere.</summary>
    private static ContentChange? Entered(ContentEdit edit) =>
        !edit.State.HasSelection && TreeViewWriting.ItemOf(edit.Part) is { } item ? TreeViewWriting.Added(edit.Root, item, OutlineSide.After) : null;

    /// <summary>
    /// Tab, Shift+Tab, Page Up or Page Down on the nodes picked out, or the node the caret is in: what <paramref name="moved"/> makes of
    /// them — and the key taken where it can make nothing of them, rather than written into a label.
    /// </summary>
    private static ContentChange? Leveled(ContentEdit edit, Func<ContentPart, IReadOnlyList<ContentPart>, int, ContentChange?> moved) =>
        Targets(edit) is { Count: > 0 } targets ? moved(edit.Root, targets, edit.State.Caret) ?? ContentChange.Stay(edit.State) : null;

    /// <summary>The nodes a key is about: the ones picked out whole, or else the one the caret is in.</summary>
    private static List<ContentPart> Targets(ContentEdit edit) =>
        Chosen(edit) is { Count: > 0 } chosen ? chosen : TreeViewWriting.ItemOf(edit.Part) is { } item ? [item] : [];

    /// <summary>
    /// A key taking a character out of a label where the label cannot lose it — a quote round it, or past either end of a label written
    /// bare, which would take a character of what says where the node stands, or join it to the next line — so nothing is taken; or,
    /// where nothing is written in the label, the node itself taken out, and the caret at the end of the label before it. Null
    /// anywhere else.
    /// </summary>
    private static ContentChange? Erased(ContentEdit edit)
    {
        var state = edit.State;
        if (state.HasSelection || edit.Part is not { } part || TreeViewParser.LabelOf(part) is not { } label || label.Words() is not { } words) return null;

        var caret = state.Caret;
        var erasing = edit.Kind == EditKind.Erasing;

        // Only at the edge of the label's words is there anything a key must not take.
        if (erasing ? caret > words.Start : caret < words.End) return null;

        if (erasing && words.Length == 0 && TreeViewWriting.ItemOf(label) is { } item)
        {
            var before = TreeViewWriting.Items(edit.Root).TakeWhile(each => each.Start < item.Start).LastOrDefault();
            return TreeViewWriting.Removed(edit.Root, [item], before is null ? null : TreeViewWriting.AfterLabel(before)) ?? ContentChange.Stay(state);
        }

        // Delete takes the slash after the words, which makes the node a file; and what follows on the line, where anything does.
        if (!erasing && (TreeViewParser.Folder(label) || label.Parent is { } stated && caret < stated.End && TreeViewParser.Quote(label) is null)) return null;

        return ContentChange.Stay(state);
    }
    /// <summary>
    /// Text typed at <paramref name="caret"/> in <paramref name="part"/> as a tree view writes it there — or null where it goes in as it
    /// is. In a label written bare, as long as what it would then say can be bare; the label put in quotes where it cannot. In a label
    /// in quotes, anything but its quote — the label put in the other quote where it holds none of those, the quote left out where it
    /// holds both — and a label in quotes with nothing in it yet written bare where what is typed can be. On one line, everywhere.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        var plain = text.Replace("\r", string.Empty, StringComparison.Ordinal).Replace('\n', ' ');

        if (TreeViewParser.LabelOf(part) is not { } label || label.Words() is not { } words)
            return plain == text ? null : new MermaidWriting(caret, caret, plain, caret + plain.Length);

        var said = words.Text;
        var at = Math.Clamp(caret - words.Start, 0, said.Length);
        var next = said[..at] + plain + said[at..];
        var slash = TreeViewParser.Folder(label) ? TreeViewParser.Slash : string.Empty;

        if (TreeViewParser.Quote(label) is not { } quote)
        {
            if (TreeViewParser.Bare(next.TrimEnd())) return plain == text ? null : new MermaidWriting(caret, caret, plain, caret + plain.Length);

            var wrap = next.Contains(TreeViewParser.Opening) ? '\'' : TreeViewParser.Opening;
            var kept = TreeViewParser.Unquoted(next, wrap);
            return new MermaidWriting(label.Start, label.End, wrap + kept + slash + wrap, label.Start + 1 + Math.Min(at + plain.Length, kept.Length));
        }

        // Nothing written yet: what is typed goes in bare where it can, as a label is written wherever it may be.
        if (said.Length == 0 && TreeViewParser.Bare(next.Trim()))
            return new MermaidWriting(label.Start, label.End, next + slash, label.Start + next.Length);

        if (!plain.Contains(quote)) return plain == text ? null : new MermaidWriting(caret, caret, plain, caret + plain.Length);

        var other = quote == '"' ? '\'' : '"';
        if (next.Contains(other))
        {
            var dropped = TreeViewParser.Unquoted(plain, quote);
            return new MermaidWriting(caret, caret, dropped, caret + dropped.Length);
        }

        return new MermaidWriting(label.Start, label.End, other + next + slash + other, label.Start + 1 + at + plain.Length);
    }

    // ── Moving ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A press on a node's row away from its words, or on its icon, picks the node up at once — with every node picked out, where it is
    /// one of them — so a node is dragged in one gesture, picked out first or not. Anywhere else, what is picked out, as the engine carries it.
    /// </summary>
    public ContentLift Lift(ContentGrip grip)
    {
        if (grip.Piece.Kind is not (MermaidPiece.Shape or TreeViewPiece.Icon) || TreeViewWriting.ItemOf(grip.Part) is not { } item) return ContentLift.Selection;

        var chosen = Picked(grip.Root, grip.State);
        return new NodesCarried(chosen.Contains(item) ? chosen : [item]);
    }

    /// <summary>Whole nodes, carried: let go on another, they go in front of it, after it or inside it, as where on its row says.</summary>
    private sealed record NodesCarried(IReadOnlyList<ContentPart> Items) : ContentCarry;

    /// <summary>
    /// Nodes carried — taken hold of by a row, or picked out and carried — let go on another node's row: in front of it over the top of
    /// the row, after it over the bottom, and inside it over the middle.
    /// </summary>
    public ContentChange? Move(ContentMove move)
    {
        IReadOnlyList<ContentPart> carried;

        if (move.Carry is NodesCarried nodes) carried = nodes.Items;
        else if (!move.Holds || move.Carrying().Where(part => part.Kind == TreeViewKinds.Node).ToList() is not { Count: > 0 } chosen) return null;
        else carried = chosen;

        if (TreeViewWriting.ItemOf(move.Part) is not { } onto || TreeViewPlaces.SideAt(move.Piece, move.At) is not { } side)
            return move.Carry is null ? ContentChange.Stay(move.State) : null;

        return TreeViewWriting.Moved(move.Root, carried, onto, side, move.State.Caret) ?? (move.Carry is null ? ContentChange.Stay(move.State) : null);
    }

    /// <summary>The nodes whose whole stretch of characters is picked out.</summary>
    private static List<ContentPart> Chosen(ContentEdit edit) => Picked(edit.Root, edit.State);

    /// <summary>The nodes of <paramref name="root"/> whose whole stretch of characters <paramref name="state"/> has picked out.</summary>
    private static List<ContentPart> Picked(ContentPart root, EditState state) =>
        [.. TreeViewWriting.Items(root).Where(item => state.Selection.Any(range => range.Start <= item.Start && item.End <= range.End))];
}
