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
/// What an edit means in a treemap, and what it offers where it is right-clicked — boxes a reader splits, merges, groups and moves
/// between groups as they see them drawn, rather than lines of an outline: where a tile is drawn follows from what it is worth, so the
/// order its line is written in is nothing a reader sees (<see cref="TreemapWriting"/>).
///
/// <para>
/// A label is typed into where it is drawn, and holds anything but the quote it is in: a quote typed into it puts it in the other
/// quote where it holds none of those, and is left out where it holds both. A value takes only what a number is written with, and
/// is shown as written once the caret is in it, the tiles laid out as it was until the caret leaves it, so nothing moves under the
/// caret while it is typed. Enter ends the writing, the item picked out whole — but in the label of a box whose value is still to
/// write it goes on into the value, so a box is made by its name, Enter, its value, Enter. Backspace in a label with nothing written
/// in it takes the box back into the one before it, as it joins a line with nothing on it to the line before — so a split taken back
/// is the box it was — and Delete there takes it out.
/// </para>
/// <para>
/// A right-click on a box offers to split it, to merge the boxes picked out into it, to put it — or the boxes picked out, where it is
/// one of them — in a new group, and to take it out; on a group's band, to put a box in it, to let its items out into the group around
/// it, and to take it out; anywhere else in the treemap, a box at its top. With whole items picked out Delete takes them out. A box or
/// group taken hold of by its tile — picked out first or not — and let go on a group, its band or any box in it, goes into that group,
/// and let go on a box at the top, to the top; while it is carried the treemap stands still with that group outlined
/// (<see cref="DropTarget"/>), since laid out finished at every point it would move under the pointer.
/// </para>
/// <para>
/// In its words the clipboard is offered as the engine does it, and over whole items only a copy (<see cref="Clipboard"/>).
/// </para>
/// </summary>
internal sealed class TreemapEdits : IContentLanguage, IOnEdit, IOnMove
{
    private const string Named = "treemap.";

    private const string Split = Named + "box.split";
    private const string Merge = Named + "box.merge";
    private const string Group = Named + "box.group";
    private const string AddInside = Named + "group.add";
    private const string Ungroup = Named + "group.ungroup";
    private const string Add = Named + "add";
    private const string Delete = Named + "delete";

    /// <inheritdoc/>
    public IOnEdit OnEdit => this;

    /// <inheritdoc/>
    public IOnMove OnMove => this;

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Typing => Holding(edit, DiagramWriting.Typed(edit, Escaping)),
        EditKind.Settling => edit.Text == "\n" ? Finished(edit) : Holding(edit, DiagramWriting.Typed(edit, Escaping)),
        EditKind.Pasting => Pasted(edit),
        EditKind.Erasing or EditKind.Deleting => Chosen(edit) is { Count: > 0 } chosen ? TreemapWriting.Removed(edit.Root, chosen) : Erased(edit),
        EditKind.Tabbing or EditKind.TabbingBack or EditKind.Raising or EditKind.Lowering => Taken(edit),
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask)
    {
        if (ask.IsReadOnly || ask.Root is not { } root) return [];

        if (TreemapWriting.ItemOf(ask.Part) is not { } item)
            return [Pressing(Add, "Text.Treemap.Add", "add_square", static edit => TreemapWriting.Added(edit.Root))];

        var deleting = Pressing(Delete, "Text.Treemap.Item.Delete", "delete",
                                static edit => Over(edit) is { } over ? TreemapWriting.Removed(edit.Root, With(edit, over)) : null) with { Warns = true };

        if (TreemapWriting.Holds(item))
            return
            [
                Pressing(AddInside, "Text.Treemap.Group.Add", "add_square",
                         static edit => Over(edit) is { } over ? TreemapWriting.AddedInside(edit.Root, over) : null),
                Pressing(Ungroup, "Text.Treemap.Group.Ungroup", "group_dismiss",
                         static edit => Over(edit) is { } over ? TreemapWriting.Ungrouped(edit.Root, over) : null),
                deleting,
            ];

        var merges = TreemapWriting.Merges(With(Picked(root, ask.Selection), item));

        return
        [
            Pressing(Split, "Text.Treemap.Box.Split", "table_cells_split",
                     static edit => Over(edit) is { } over ? TreemapWriting.Split(edit.Root, over) : null)
                with { Disabled = !TreemapWriting.Splits(item) },
            Pressing(Merge, merges ? "Text.Treemap.Box.Merge" : "Text.Treemap.Box.MergeHow", "table_cells_merge",
                     static edit => Over(edit) is { } over ? TreemapWriting.Merged(edit.Root, With(edit, over), over) : null)
                with { Disabled = !merges },
            Pressing(Group, "Text.Treemap.Box.Group", "group",
                     static edit => Over(edit) is { } over ? TreemapWriting.Grouped(edit.Root, With(edit, over)) : null),
            deleting,
        ];

        // Done to the item under the pointer, so each stands beside the rest as its icon, named to a reader hovering over it.
        static MiniRibbonButton Pressing(string id, string name, string icon, Func<ContentEdit, ContentChange?> pressed) =>
            new(id, Str.Get(name), pressed) { Shape = DiagramRibbon.Drawn(IconRef.Fluent(icon)) };
    }

    /// <summary>The item a control of the ribbon was used over, or null where it was used over none.</summary>
    private static ContentPart? Over(ContentEdit edit) => TreemapWriting.ItemOf(edit.Part);

    /// <summary>What something done to <paramref name="over"/> is done to: the items picked out, where it is one of them — else it alone.</summary>
    private static List<ContentPart> With(ContentEdit edit, ContentPart over) => With(Chosen(edit), over);

    private static List<ContentPart> With(List<ContentPart> picked, ContentPart over) => picked.Contains(over) ? picked : [over];

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

    /// <summary>
    /// Enter in an item's words: the writing ended, the item picked out whole as a press on its tile picks it — except in the label of a
    /// box whose value is still to write, where the caret goes on into the value; so a box put in is made by its name, Enter, its value,
    /// Enter. Anywhere else, what Enter does anywhere.
    /// </summary>
    private static ContentChange? Finished(ContentEdit edit)
    {
        if (edit.State.HasSelection || TreemapWriting.ItemOf(edit.Part) is not { } item) return null;

        if (TreemapParser.LabelOf(edit.Part) is not null && !TreemapWriting.Holds(item) && TreemapWriting.Value(item)?.Hole() is { } hole)
            return new ContentChange([], hole.Start);

        return new ContentChange([], item.Start) { Chooses = new WholeRanges([new EditRange(item.Start, item.End - item.Start)]) };
    }

    /// <summary>
    /// What is typed into a value, as <paramref name="typed"/> writes it, with the value held open while the caret stays in it and keeping
    /// what it said as it was begun — so the tiles it sizes stand still until the caret leaves it, and move once (<see cref="RawZone.Was"/>).
    /// A value opened already, by a press or a key, is kept open as it was; anything else is <paramref name="typed"/> as it is.
    /// </summary>
    private static ContentChange? Holding(ContentEdit edit, ContentChange? typed)
    {
        if (typed is not { Writes: [var write] } || typed.Raw is not null || TreemapParser.LabelOf(edit.Part) is not null
            || TreemapWriting.ItemOf(edit.Part) is not { } item || TreemapWriting.Value(item) is not { } amount
            || write.Start < amount.Start || write.Start > amount.End)
            return typed;

        var lead = write.Text.Length - write.Text.TrimStart().Length;
        var (start, end) = amount.Inner(Kinds.Number) is { Length: > 0 } number
            ? (number.Start, number.End + write.Text.Length - write.Length)
            : (write.Start + lead, write.Start + write.Text.Length);

        return typed with { Raw = new RawZone(start, end) { Was = amount.Inner(Kinds.Number)?.Text ?? string.Empty } };
    }

    /// <summary>
    /// Tab, Shift+Tab, Page Up or Page Down in an item, taken rather than written into its label: they move a line of an outline, and
    /// where a tile is drawn follows from what it is worth, not from where its line is written. Anywhere else, what the key does anywhere.
    /// </summary>
    private static ContentChange? Taken(ContentEdit edit) => TreemapWriting.ItemOf(edit.Part) is not null ? ContentChange.Stay(edit.State) : null;

    /// <summary>
    /// A key taking a character out of a label where the label cannot lose it — a quote round it — so nothing is taken; or, where nothing
    /// is written in the label, Backspace taking the box back into the one beside it and Delete taking it out, the caret at the end of the
    /// label before it. Null anywhere else.
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
        if (label.Words() is { Length: > 0 } || TreemapWriting.ItemOf(label) is not { } item || TreemapWriting.Holds(item)) return ContentChange.Stay(state);

        if (erasing) return TreemapWriting.MergedBack(edit.Root, item) ?? ContentChange.Stay(state);

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

    if (part.Role != TreemapRoles.Value && part.Parent?.Kind != MermaidKinds.Amount) return null;

        var only = MermaidWriting.Only(caret, text, TreemapParser.Numeral);
        var digits = only ?? new MermaidWriting(caret, caret, text, caret + text.Length);

        // A value begun straight after its colon is written a space after it, as Mermaid writes one.
        if (digits.Text.Length == 0 || TreemapWriting.ItemOf(part) is not { } item || caret <= item.Children[0].Start
            || item.Children[0].Print()[caret - item.Children[0].Start - 1] is not (':' or ',')) return only;

        return digits with { Text = " " + digits.Text, Caret = digits.Caret + 1 };
    }

    // ── Moving ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A press on an item's tile, away from its words, picks the item up at once — with every item picked out, where it is one of them
    /// — so an item is dragged in one gesture, picked out first or not. Anywhere else, what is picked out, as the engine carries it.
    /// </summary>
    public ContentLift Lift(ContentGrip grip)
    {
        if (grip.Piece.Kind != MermaidPiece.Shape || TreemapWriting.ItemOf(grip.Part) is not { } item) return ContentLift.Selection;

        var chosen = Picked(grip.Root, grip.State.Selection);
        return new ItemsCarried(chosen.Contains(item) ? chosen : [item]);
    }

    /// <summary>Whole items, carried: let go on a group or anything in it, they go into it.</summary>
    private sealed record ItemsCarried(IReadOnlyList<ContentPart> Items) : ContentCarry;

    /// <summary>Items carried — taken hold of by a tile, or picked out and carried — let go on the treemap: into the group the drop is on (<see cref="Into"/>).</summary>
    public ContentChange? Move(ContentMove move)
    {
        if (move.Carry is ItemsCarried carried) return TreemapWriting.MovedInto(move.Root, carried.Items, Into(move), move.State.Caret);

        if (!move.Holds) return null;
        if (move.Carrying().Where(part => part.Kind == TreemapKinds.Node).ToList() is not { Count: > 0 } chosen) return null;

        return TreemapWriting.MovedInto(move.Root, chosen, Into(move), move.State.Caret) ?? ContentChange.Stay(move.State);
    }

    /// <inheritdoc/>
    /// <remarks>The group a carried item would go into, outlined where it stands — the treemap itself, for its top.</remarks>
    public ContentPart? DropTarget(ContentMove move) => move.Carry is ItemsCarried ? Into(move) : null;

    /// <summary>
    /// The group a drop is on: the group whose band it is on, or the group the box it is on is in — the treemap itself for a box at its
    /// top, and anywhere in it on no item.
    /// </summary>
    private static ContentPart Into(ContentMove move) => TreemapWriting.ItemOf(move.Part) switch
    {
        { } on when TreemapWriting.Holds(on) => on,
        { } box => TreemapWriting.HolderOf(move.Root, box),
        null => move.Root,
    };

    /// <summary>The items whose whole stretch of characters is picked out.</summary>
    private static List<ContentPart> Chosen(ContentEdit edit) => Picked(edit.Root, edit.State.Selection);

    /// <summary>The items of <paramref name="root"/> whose whole stretch of characters <paramref name="selection"/> picks out.</summary>
    private static List<ContentPart> Picked(ContentPart root, IReadOnlyList<EditRange> selection) =>
        [.. TreemapWriting.Items(root).Where(item => selection.Any(range => range.Start <= item.Start && item.End <= range.End))];
}
