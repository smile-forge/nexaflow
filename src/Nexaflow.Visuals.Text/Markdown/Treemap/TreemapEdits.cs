using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Treemap;

/// <summary>
/// What an edit means in a treemap, and what it offers where it is right-clicked — an outline a reader works with as they would any
/// other.
///
/// <para>
/// A label is typed into where it is drawn, and holds anything but the quote it is in: a quote typed into it puts it in the other
/// quote where it holds none of those, and is left out where it holds both. A value takes only what a number is written with. Enter
/// in an item puts a leaf in after it, the caret in its label; Tab and Shift+Tab take the item a level in, into the section before
/// it, and out, after the section it is in; Page Up and Page Down move it up and down among its siblings. Backspace in a label with
/// nothing written in it takes the item out, as it does a line with nothing on it.
/// </para>
/// <para>
/// A right-click on an item offers to put a leaf in before it, after it or inside it, to take it a level in or out, and to take it
/// out; anywhere else in the treemap, a leaf after everything. With whole items picked out, Delete takes them out and Insert puts a
/// leaf in before them; and an item taken hold of by its tile — picked out first or not — and let go on a section goes into it, and
/// on a leaf goes beside it. An item always goes with everything under it (<see cref="TreemapWriting"/>).
/// </para>
/// <para>
/// In its words the clipboard is offered as the engine does it, and over whole items only a copy (<see cref="Clipboard"/>).
/// </para>
/// </summary>
internal sealed class TreemapEdits : IContentLanguage, IOnEdit, IOnMove
{
    private const string Named = "treemap.";

    private const string Before = Named + "insert.before";
    private const string After = Named + "insert.after";
    private const string Inside = Named + "insert.inside";
    private const string Last = Named + "insert.end";
    private const string Deeper = Named + "level.in";
    private const string Shallower = Named + "level.out";
    private const string Delete = Named + "delete";

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
        EditKind.Erasing or EditKind.Deleting => Chosen(edit) is { Count: > 0 } chosen ? TreemapWriting.Removed(edit.Root, chosen) : Erased(edit),
        EditKind.Inserting => Targets(edit) is [var first, ..] ? TreemapWriting.Added(edit.Root, first, OutlineSide.Before) : null,
        EditKind.Tabbing => Leveled(edit, TreemapWriting.Deeper),
        EditKind.TabbingBack => Leveled(edit, TreemapWriting.Shallower),
        EditKind.Raising => Leveled(edit, TreemapWriting.Raised),
        EditKind.Lowering => Leveled(edit, TreemapWriting.Lowered),
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask)
    {
        if (ask.IsReadOnly || ask.Root is null) return [];

        if (TreemapWriting.ItemOf(ask.Part) is null)
            return [Pressing(Last, "Text.Treemap.Item.End", "add_square", static edit => TreemapWriting.Added(edit.Root, null, OutlineSide.After))];

        return
        [
            Pressing(Before, "Text.Treemap.Item.Before", "table_stack_above",
                     static edit => Over(edit) is { } over ? TreemapWriting.Added(edit.Root, over, OutlineSide.Before) : null),
            Pressing(After, "Text.Treemap.Item.After", "table_stack_below",
                     static edit => Over(edit) is { } over ? TreemapWriting.Added(edit.Root, over, OutlineSide.After) : null),
            Pressing(Inside, "Text.Treemap.Item.Inside", "add_square",
                     static edit => Over(edit) is { } over ? TreemapWriting.AddedInside(edit.Root, over) : null),
            Pressing(Deeper, "Text.Treemap.Item.Deeper", "text_indent_increase_ltr",
                     static edit => Over(edit) is { } over ? TreemapWriting.Deeper(edit.Root, [over], edit.State.Caret) : null),
            Pressing(Shallower, "Text.Treemap.Item.Shallower", "text_indent_decrease_ltr",
                     static edit => Over(edit) is { } over ? TreemapWriting.Shallower(edit.Root, [over], edit.State.Caret) : null),
            Pressing(Delete, "Text.Treemap.Item.Delete", "delete",
                     static edit => Over(edit) is { } over ? TreemapWriting.Removed(edit.Root, [over]) : null) with { Warns = true },
        ];

        // Done to the item under the pointer, so each stands beside the rest as its icon, named to a reader hovering over it.
        static MiniRibbonButton Pressing(string id, string name, string icon, Func<ContentEdit, ContentChange?> pressed) =>
            new(id, Str.Get(name), pressed) { Shape = DiagramRibbon.Drawn(IconRef.Fluent(icon)) };
    }

    /// <summary>The item a control of the ribbon was used over, or null where it was used over none.</summary>
    private static ContentPart? Over(ContentEdit edit) => TreemapWriting.ItemOf(edit.Part);

    /// <inheritdoc/>
    /// <remarks>
    /// In a treemap's words — a label, a value, the title — every one of the clipboard's ways means what it says, and a paste goes in
    /// as the parser spells it for that place. Over whole items only a copy: taken away as characters they would leave the lines
    /// round them as they were, which is what taking an item out does instead.
    /// </remarks>
    public ClipboardOffers Clipboard(ContentAsk ask)
    {
        if (ask.Root is not { } root) return ClipboardOffers.None;

        if (ask.Chosen is { } chosen)
            return Written(root, root.Start + chosen.Start, root.Start + chosen.Start + chosen.Length) is not null ? ClipboardOffers.All : ClipboardOffers.Copy;

        return ask.Caret is { } caret && Written(root, caret, caret) is not null ? ClipboardOffers.Paste : ClipboardOffers.None;
    }

    /// <summary>A paste in a treemap's words: written where it was put, as words the parser makes safe there. Anywhere else, nothing.</summary>
    private static ContentChange Pasted(ContentEdit edit)
    {
        var state = edit.State;
        var (from, to) = state.HasSelection ? (state.Selection[0].Start, state.Selection[^1].End) : (state.Caret, state.Caret);

        if (state.Selection.Count > 1 || Written(edit.Root, from, to) is not { } words) return ContentChange.Stay(state);

        return new ContentChange([ContentWrite.Words(words, from, to - from, edit.Text)], from + edit.Text.Length);
    }

    /// <summary>The words the stretch from <paramref name="from"/> to <paramref name="to"/> lies in — a label's, a value's or the title's — or null.</summary>
    private static ContentPart? Written(ContentPart root, int from, int to) =>
        root.SelfAndDescendants().FirstOrDefault(part => part.Start <= from && to <= part.End && !part.Derived && part.Kind switch
        {
            Kinds.Words => part.Role is TreemapRoles.Label or MermaidRoles.Title,
            Kinds.Number => part.Role == TreemapRoles.Value,
            _ => false,
        });

    // ── Keys ────────────────────────────────────────────────────────────────

    /// <summary>Enter in an item: a leaf put in after it, the caret in its label. Anywhere else, what Enter does anywhere.</summary>
    private static ContentChange? Entered(ContentEdit edit) =>
        !edit.State.HasSelection && TreemapWriting.ItemOf(edit.Part) is { } item ? TreemapWriting.Added(edit.Root, item, OutlineSide.After) : null;

    /// <summary>
    /// Tab, Shift+Tab, Page Up or Page Down on the items picked out, or the item the caret is in: what <paramref name="moved"/> makes of
    /// them — and the key taken where it can make nothing of them, rather than written into a label.
    /// </summary>
    private static ContentChange? Leveled(ContentEdit edit, Func<ContentPart, IReadOnlyList<ContentPart>, int, ContentChange?> moved) =>
        Targets(edit) is { Count: > 0 } targets ? moved(edit.Root, targets, edit.State.Caret) ?? ContentChange.Stay(edit.State) : null;

    /// <summary>The items a key is about: the ones picked out whole, or else the one the caret is in.</summary>
    private static List<ContentPart> Targets(ContentEdit edit) =>
        Chosen(edit) is { Count: > 0 } chosen ? chosen : TreemapWriting.ItemOf(edit.Part) is { } item ? [item] : [];

    /// <summary>
    /// A key taking a character out of a label where the label cannot lose it — a quote round it — so nothing is taken; or, where nothing
    /// is written in the label, the item itself taken out, as a line with nothing on it is, and the caret at the end of the label before
    /// it. Null anywhere else.
    /// </summary>
    private static ContentChange? Erased(ContentEdit edit)
    {
        var state = edit.State;
        if (state.HasSelection || edit.Part is not { } part || TreemapParser.LabelOf(part) is not { } label) return null;

        var caret = state.Caret;
        var open = label.Children.First(child => child.Role == Roles.Open);
        var close = label.Children.Last(child => child.Role == Roles.Close);
        var erasing = edit.Kind == EditKind.Erasing;

        if (erasing ? caret > open.End : caret < close.Start) return null;
        if (label.Words() is { Length: > 0 } || !erasing || TreemapWriting.ItemOf(label) is not { } item) return ContentChange.Stay(state);

        var before = TreemapWriting.Items(edit.Root).TakeWhile(each => each.Start < item.Start).LastOrDefault();
        return TreemapWriting.Removed(edit.Root, [item], before is null ? null : TreemapWriting.AfterLabel(before)) ?? ContentChange.Stay(state);
    }

    /// <summary>
    /// Text typed at <paramref name="caret"/> in <paramref name="part"/> as a treemap writes it there — or null where it goes in as it
    /// is. A label holds anything but its quote, on one line: a quote typed into it puts the label in the other quote, where it holds
    /// none of those, and is left out where it holds both. A value holds only what a number is written with.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (TreemapParser.LabelOf(part) is { } label)
        {
            var quote = TreemapParser.Quote(label);
            var plain = TreemapParser.Unquoted(text, '\0');

            if (!plain.Contains(quote)) return plain == text ? null : new MermaidWriting(caret, caret, plain, caret + plain.Length);

            var other = quote == '"' ? '\'' : '"';
            var said = label.Words()?.Text ?? string.Empty;

            if (said.Contains(other) || plain.Contains(other))
            {
                var kept = TreemapParser.Unquoted(text, quote);
                return new MermaidWriting(caret, caret, kept, caret + kept.Length);
            }

            var at = Math.Clamp(caret - (label.Start + 1), 0, said.Length);
            var head = other + said[..at] + plain;

            return new MermaidWriting(label.Start, label.End, head + said[at..] + other, label.Start + head.Length);
        }

        return part.Role == TreemapRoles.Value || part.Parent?.Kind == MermaidKinds.Amount ? MermaidWriting.Only(caret, text, TreemapParser.Numeral) : null;
    }

    // ── Moving ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A press on an item's tile, away from its words, picks the item up at once — with every item picked out, where it is one of them
    /// — so an item is dragged in one gesture, picked out first or not. Anywhere else, what is picked out, as the engine carries it.
    /// </summary>
    public ContentLift Lift(ContentGrip grip)
    {
        if (grip.Piece.Kind != MermaidPiece.Shape || TreemapWriting.ItemOf(grip.Part) is not { } item) return ContentLift.Selection;

        var chosen = Picked(grip.Root, grip.State);
        return new ItemsCarried(chosen.Contains(item) ? chosen : [item]);
    }

    /// <summary>Whole items, carried: let go on a section they go into it, and on a leaf beside it.</summary>
    private sealed record ItemsCarried(IReadOnlyList<ContentPart> Items) : ContentCarry;

    /// <summary>Items carried — taken hold of by a tile, or picked out and carried — let go on another item: into it where it is a section, and beside it where it is a leaf.</summary>
    public ContentChange? Move(ContentMove move)
    {
        if (move.Carry is ItemsCarried carried)
            return TreemapWriting.ItemOf(move.Part) is { } onto ? TreemapWriting.Moved(move.Root, carried.Items, onto, move.State.Caret) : null;

        if (!move.Holds) return null;
        if (move.Carrying().Where(part => part.Kind == TreemapKinds.Node).ToList() is not { Count: > 0 } chosen) return null;
        if (TreemapWriting.ItemOf(move.Part) is not { } over) return ContentChange.Stay(move.State);

        return TreemapWriting.Moved(move.Root, chosen, over, move.State.Caret) ?? ContentChange.Stay(move.State);
    }

    /// <summary>The items whose whole stretch of characters is picked out.</summary>
    private static List<ContentPart> Chosen(ContentEdit edit) => Picked(edit.Root, edit.State);

    /// <summary>The items of <paramref name="root"/> whose whole stretch of characters <paramref name="state"/> has picked out.</summary>
    private static List<ContentPart> Picked(ContentPart root, EditState state) =>
        [.. TreemapWriting.Items(root).Where(item => state.Selection.Any(range => range.Start <= item.Start && item.End <= range.End))];
}
