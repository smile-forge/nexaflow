using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
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
/// written into; a bit number takes only digits.
/// </para>
/// <para>
/// A right-click on a field offers to put a field in before it or after it, to take it out, and how many bits it takes — each
/// length written as its number, the one it takes now marked. Anywhere else in the packet, it offers a field after everything
/// written. With whole fields picked out, Delete takes them out and Insert puts a field in above them; and fields carried and let
/// go on another change places with it. Whatever changes, the fields stay contiguous and keep their lengths, and only their
/// numbers are written again (<see cref="PacketWriting"/>).
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

    /// <summary>What an offer of a field's length starts with, the length after it.</summary>
    private const string Sized = Named + "bits.";

    /// <summary>The choice a field's length is one of.</summary>
    private const string Bits = "Bits";

    /// <summary>The lengths offered: a bit, and the widths fields usually are.</summary>
    private static readonly long[] Lengths = [1, 2, 4, 8, 16, 32];

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
        EditKind.Erasing or EditKind.Deleting => Chosen(edit) is { Count: > 0 } chosen ? PacketWriting.Removed(edit.Root, chosen) : null,
        EditKind.Inserting => Chosen(edit) is [var first, ..] ? PacketWriting.Inserted(edit.Root, first, after: false, Newline(edit)) : null,
        EditKind.Choosing => Chosen(edit, edit.Text),
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<LayoutIntent> Offers(ContentAsk ask)
    {
        if (ask.IsReadOnly || ask.Root is null) return [];

        if (PacketWriting.FieldOf(ask.Part) is not { } field) return [Inserting(Last, "Text.Packet.Field.End")];

        var size = PacketWriting.Size(field);
        var sized = PacketWriting.Sized(field);

        return
        [
            Inserting(Before, "Text.Packet.Field.Before"),
            Inserting(After, "Text.Packet.Field.After"),
            new LayoutIntent(Delete, null, Str.Get("Text.Packet.Field.Delete")),
            .. Lengths.Append(size).Distinct().Order().Select(bits => new LayoutIntent(Sized + Said(bits), null, Length(bits))
            {
                Group = Bits,
                GroupTip = Str.Get("Text.Packet.Bits"),
                Current = sized && bits == size,
                Letters = Said(bits),
            }),
        ];

        static LayoutIntent Inserting(string verb, string tip) => new(verb, null, Str.Get(tip)) { Offer = LayoutOffer.Insert };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// In a packet's words — a label, a bit number, the title — what is picked out is text, so every one of the clipboard's ways means
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
    /// backslash in a label, only a whole number in a bit number, which refuses the paste where it is not one. Anywhere else, nothing.
    /// </summary>
    private static ContentChange Pasted(ContentEdit edit)
    {
        var state = edit.State;
        var (from, to) = state.HasSelection ? (state.Selection[0].Start, state.Selection[^1].End) : (state.Caret, state.Caret);

        if (state.Selection.Count > 1 || Written(edit.Root, from, to) is not { } words) return ContentChange.Stay(state);

        return new ContentChange([ContentWrite.Words(words, from, to - from, edit.Text)], from + edit.Text.Length);
    }

    /// <summary>The words the stretch from <paramref name="from"/> to <paramref name="to"/> lies in — a label's, a bit number's or the title's — or null.</summary>
    private static ContentPart? Written(ContentPart root, int from, int to) =>
        root.SelfAndDescendants().FirstOrDefault(part => part.Start <= from && to <= part.End && !part.Derived && part.Kind switch
        {
            Kinds.Number => true,
            Kinds.Words => PacketParser.LabelOf(part) is not null || part.Role == MermaidRoles.Title,
            _ => false,
        });

    /// <summary>What the right-click offered and was chosen, made on the field it was offered over.</summary>
    private static ContentChange? Chosen(ContentEdit edit, string verb)
    {
        if (!verb.StartsWith(Named, StringComparison.Ordinal)) return null;
        if (verb == Last) return PacketWriting.Appended(edit.Root, Newline(edit));
        if (PacketWriting.FieldOf(edit.Part) is not { } field) return null;

        return verb switch
        {
            Before => PacketWriting.Inserted(edit.Root, field, after: false, Newline(edit)),
            After => PacketWriting.Inserted(edit.Root, field, after: true, Newline(edit)),
            Delete => PacketWriting.Removed(edit.Root, [field]),
            _ when verb.StartsWith(Sized, StringComparison.Ordinal)
                   && long.TryParse(verb[Sized.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var bits) && bits > 0
                => PacketWriting.Resized(edit.Root, field, bits, edit.State.Caret),
            _ => null,
        };
    }

    /// <summary>Fields carried and let go on another: they and it change places, and every field is numbered on in its new place.</summary>
    public ContentChange? Move(ContentMove move)
    {
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
        if (part.Kind == Kinds.Number || part.Parent is { Kind: MermaidKinds.Amount }) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

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

    /// <summary>The fields whose whole stretch of characters is picked out.</summary>
    private static List<ContentPart> Chosen(ContentEdit edit) =>
        [.. PacketWriting.Fields(edit.Root).Where(field => edit.State.Selection.Any(range => range.Start <= field.Start && field.End <= range.End))];

    /// <summary>What a length is called where a reader reads it.</summary>
    private static string Length(long bits) => bits == 1 ? Str.Get("Text.Packet.Bits.One") : Str.Format("Text.Packet.Bits.Many", bits);

    private static string Said(long bits) => bits.ToString(CultureInfo.InvariantCulture);

    /// <summary>The line ending the packet is written with.</summary>
    private static string Newline(ContentEdit edit) => edit.Source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
