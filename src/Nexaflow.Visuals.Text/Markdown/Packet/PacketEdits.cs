using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using System.Windows.Input;
using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Layout.Packet;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Packet;

/// <summary>
/// What an edit means in a packet, and what it offers where it is right-clicked.
///
/// <para>
/// What is typed into a label is kept by a backslash where it would end it, and a label still to write is put in quotes as it is
/// written into. Backspace and Delete take a backslash and what it keeps together. A field's bits are never typed: they follow
/// from the fields before it and how long each is, and change only by what is offered below.
/// </para>
/// <para>
/// A right-click on a field opens a ribbon of how many bits it takes — a box holding its length, which any whole number typed in
/// replaces, and the usual lengths dropped down beside it — and buttons putting a field in before it or after it and taking it
/// out. Anywhere else in the packet, it offers a field after everything written. With whole fields picked out, Delete takes them
/// out and Insert puts a field in above them; fields carried and let go on another change places with it; and the end of a field,
/// taken hold of and let go over a bit, makes the field end there. Whatever changes, the fields stay contiguous and keep their
/// lengths, and only their numbers are written again (<see cref="PacketWriting"/>).
/// </para>
/// <para>
/// In its words the clipboard is offered as the engine does it — cut, copy and paste — and over whole fields only a copy
/// (<see cref="Clipboard"/>).
/// </para>
/// </summary>
internal sealed class PacketEdits : IContentLanguage, IOnEdit, IOnMove
{
    /// <summary>What a field's offers start with.</summary>
    private const string Named = "packet.";

    private const string Before = Named + "insert.before";
    private const string After = Named + "insert.after";
    private const string Last = Named + "insert.end";
    private const string Delete = Named + "delete";

    /// <summary>The entry a field's length is typed into.</summary>
    private const string Length = Named + "bits";

    /// <summary>The lengths listed: a bit, a few, and the widths fields usually are.</summary>
    private static readonly long[] Lengths = [1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 24, 32, 48, 64];

    /// <inheritdoc/>
    public IOnEdit OnEdit => this;

    /// <inheritdoc/>
    public IOnMove OnMove => this;

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => edit.Kind switch
    {
        EditKind.Typing => DiagramWriting.Typed(edit, Escaping),
        EditKind.Settling => DiagramWriting.Typed(edit, Escaping),
        EditKind.Pasting => Pasted(edit),
        EditKind.Erasing or EditKind.Deleting => Chosen(edit) is { Count: > 0 } chosen ? PacketWriting.Removed(edit.Root, chosen) : Erased(edit),
        EditKind.Inserting => Chosen(edit) is [var first, ..] ? PacketWriting.Inserted(edit.Root, first, after: false, Newline(edit)) : null,
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<MiniRibbonControl> Ribbon(ContentAsk ask)
    {
        if (ask.IsReadOnly || ask.Root is null) return [];

        if (PacketWriting.FieldOf(ask.Part) is not { } field)
            return [Pressing(Last, "Text.Packet.Field.End", "table_stack_right", static edit => PacketWriting.Appended(edit.Root, Newline(edit)))];

        return
        [
            new MiniRibbonEntry(Length, Str.Get("Text.Packet.Bits"), [.. Lengths.Select(Said)],
                                PacketWriting.Sized(field) ? Said(PacketWriting.Size(field)) : string.Empty, Lengthened)
            {
                Accepts = static typed => Counted(typed) is not null,
            },
            Pressing(Before, "Text.Packet.Field.Before", "table_stack_left",
                     static edit => Over(edit) is { } over ? PacketWriting.Inserted(edit.Root, over, after: false, Newline(edit)) : null),
            Pressing(After, "Text.Packet.Field.After", "table_stack_right",
                     static edit => Over(edit) is { } over ? PacketWriting.Inserted(edit.Root, over, after: true, Newline(edit)) : null),
            Pressing(Delete, "Text.Packet.Field.Delete", "delete",
                     static edit => Over(edit) is { } over ? PacketWriting.Removed(edit.Root, [over]) : null) with { Warns = true },
        ];

        // Done to the field under the pointer, so each stands beside the rest of what is done to it as its icon — never behind a
        // button of its own a reader has to open to find it — and is named to a reader hovering over it.
        static MiniRibbonButton Pressing(string id, string name, string icon, Func<ContentEdit, ContentChange?> pressed) =>
            new(id, Str.Get(name), pressed) { Shape = DiagramRibbon.Drawn(IconRef.Fluent(icon)) };
    }

    /// <summary>The field a control of the ribbon was used over, or null where it was used over none.</summary>
    private static ContentPart? Over(ContentEdit edit) => PacketWriting.FieldOf(edit.Part);

    /// <summary>A length typed or picked for the field the ribbon was opened over: the field made that long, the fields after it numbered on.</summary>
    private static ContentChange? Lengthened(ContentEdit edit, string typed) =>
        Counted(typed) is { } bits && Over(edit) is { } over ? PacketWriting.Resized(edit.Root, over, bits, edit.State.Caret) : null;

    /// <summary>How many bits <paramref name="typed"/> says — a whole number of one or more — or null where it says none.</summary>
    private static long? Counted(string typed) =>
        long.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var bits) && bits > 0 ? bits : null;

    /// <inheritdoc/>
    /// <remarks>
    /// In a packet's words — a label, the title — what is picked out is text, so every one of the clipboard's ways means
    /// what it says there, and a paste at the caret goes in as the parser spells it for that place. Over anything else picked out —
    /// whole fields — only a copy: taken away as characters they would leave a gap the fields after them do not close up, which is
    /// what taking a field out does instead.
    /// </remarks>
    public ClipboardOffers Clipboard(ContentAsk ask)
    {
        if (ask.Root is not { } root) return ClipboardOffers.None;

        if (ask.Chosen is { } chosen)
            return Written(root, root.Start + chosen.Start, root.Start + chosen.Start + chosen.Length) is not null ? ClipboardOffers.All : ClipboardOffers.Copy;

        return ask.Caret is { } caret && Written(root, caret, caret) is not null ? ClipboardOffers.Paste : ClipboardOffers.None;
    }

    /// <summary>
    /// A paste in a packet's words: what is pasted, written where it was put as words the parser makes safe there — kept by a
    /// backslash in a label, on one line in the title. Anywhere else, nothing.
    /// </summary>
    private static ContentChange Pasted(ContentEdit edit)
    {
        var state = edit.State;
        var (from, to) = state.HasSelection ? (state.Selection[0].Start, state.Selection[^1].End) : (state.Caret, state.Caret);

        if (state.Selection.Count > 1 || Written(edit.Root, from, to) is not { } words) return ContentChange.Stay(state);

        return new ContentChange([ContentWrite.Words(words, from, to - from, edit.Text)], from + edit.Text.Length);
    }

    /// <summary>The words the stretch from <paramref name="from"/> to <paramref name="to"/> lies in — a label's or the title's — or null.</summary>
    private static ContentPart? Written(ContentPart root, int from, int to) =>
        root.SelfAndDescendants().FirstOrDefault(part => part.Start <= from && to <= part.End && !part.Derived && part.Kind switch
        {
    
            Kinds.Words => PacketParser.LabelOf(part) is not null || part.Role == MermaidRoles.Title,
            _ => false,
        });

    /// <summary>
    /// A press on the end of a field picks up its length; one on a field's box, away from its words, picks the field up at once — with
    /// every chosen field, where it is one of them — so a field is dragged in one gesture, picked out first or not. Anywhere else, what
    /// is picked out, as the engine carries it.
    /// </summary>
    public ContentLift Lift(ContentGrip grip)
    {
        if (PacketWriting.FieldOf(grip.Part) is not { } field) return ContentLift.Selection;
        if (grip.Piece.Kind == PacketPiece.End) return new Ending(field);
        if (grip.Piece.Kind != MermaidPiece.Shape) return ContentLift.Selection;

        var chosen = Picked(grip.Root, grip.State);
        return new FieldsCarried(chosen.Contains(field) ? chosen : [field]);
    }

    /// <summary>The end of a field, carried: the bit it is let go over is the bit the field then ends at.</summary>
    private sealed record Ending(ContentPart Field) : ContentCarry
    {
        /// <summary>A field's end moves across the row, so the pointer over it says so.</summary>
        public override Cursor? Pointer => Cursors.SizeWE;
    }

    /// <summary>Whole fields, carried: let go on another field, they and it change places.</summary>
    private sealed record FieldsCarried(IReadOnlyList<ContentPart> Fields) : ContentCarry;

    /// <summary>
    /// A field's end let go over a bit: the field made long enough to end there, every field after it numbered on — or nothing, over no
    /// bit, before the field starts, or where it already ends.
    /// </summary>
    private static ContentChange? Ended(ContentMove move, ContentPart field)
    {
        if (PacketPlaces.BitAt(move.Piece, move.At) is not { } bit || field.Node is not PacketFieldNode said) return null;

        var bits = bit - said.First + 1;
        if (bits < 1 || (PacketWriting.Sized(field) && bits == PacketWriting.Size(field))) return null;

        return PacketWriting.Resized(move.Root, field, bits, move.State.Caret);
    }

    /// <summary>
    /// A field's end carried, let go where the field is then to end (<see cref="Ended"/>); fields carried and let go on another, they
    /// and it change places, and every field is numbered on in its new place.
    /// </summary>
    public ContentChange? Move(ContentMove move)
    {
        if (move.Carry is Ending ending) return Ended(move, ending.Field);
        if (move.Carry is FieldsCarried carried) return PacketWriting.FieldOf(move.Part) is { } on ? PacketWriting.Moved(move.Root, carried.Fields, on) : null;
        if (!move.Holds) return null;
        if (move.Carrying().Where(part => PacketWriting.FieldOf(part) == part).ToList() is not { Count: > 0 } chosen) return null;
        if (PacketWriting.FieldOf(move.Part) is not { } over) return ContentChange.Stay(move.State);

        return PacketWriting.Moved(move.Root, chosen, over) ?? ContentChange.Stay(move.State);
    }

    /// <summary>
    /// Text typed at <paramref name="caret"/> in <paramref name="part"/> as a packet writes it there — or null where it goes in as
    /// it is. Spelled as the parser spells a label (<see cref="PacketParser.Escaped"/>), so a keystroke and a paste agree.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {


        if (PacketParser.LabelOf(part) is not { } label) return null;

        if (PacketParser.Opened(label) is { } quote)
        {
            var kept = PacketParser.Escaped(text, quote);
            return kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        // Nothing is written after the colon yet: what is typed there is a label, so it goes in its quotes, the caret inside them.
        var opened = PacketParser.Opening + PacketParser.Escaped(text, PacketParser.Opening);
        return new MermaidWriting(caret, caret, opened + PacketParser.Opening, caret + opened.Length);
    }

    /// <summary>
    /// A key taking a character out of a label where the character is one the label cannot lose: a quote round it — a label still to
    /// write is its quotes and nothing else, and one taken leaves a label that never ends — so nothing is taken; or half of what a
    /// backslash keeps, so the backslash and what it keeps go together. Null anywhere else, where a key takes one character as it does in
    /// any words.
    /// </summary>
    private static ContentChange? Erased(ContentEdit edit)
    {
        var state = edit.State;
        if (state.HasSelection || edit.Part is not { } part || PacketParser.LabelOf(part) is not { } label) return null;

        var caret = state.Caret;
        var erasing = edit.Kind == EditKind.Erasing;

        if (erasing
                ? label.Children.FirstOrDefault(child => child.Role == Roles.Open) is { } open && caret <= open.End
                : label.Children.LastOrDefault(child => child.Role == Roles.Close) is { } close && caret >= close.Start)
            return ContentChange.Stay(state);

        foreach (var escape in label.SelfAndDescendants())
            if (escape.Kind == PacketKinds.Escape && (erasing ? escape.Start < caret && caret <= escape.End : escape.Start <= caret && caret < escape.End))
                return ContentChange.Write(escape.Start, escape.Length, string.Empty, escape.Start);

        return null;
    }

    /// <summary>The fields whose whole stretch of characters is picked out.</summary>
    private static List<ContentPart> Chosen(ContentEdit edit) => Picked(edit.Root, edit.State);

    /// <summary>The fields of <paramref name="root"/> whose whole stretch of characters <paramref name="state"/> has picked out.</summary>
    private static List<ContentPart> Picked(ContentPart root, EditState state) =>
        [.. PacketWriting.Fields(root).Where(field => state.Selection.Any(range => range.Start <= field.Start && field.End <= range.End))];

    private static string Said(long bits) => bits.ToString(CultureInfo.InvariantCulture);

    /// <summary>The line ending the packet is written with.</summary>
    private static string Newline(ContentEdit edit) => edit.Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
