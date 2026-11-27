using System;
using System.Threading.Tasks;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Markdown.Export;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The clipboard, from the content's side: what a copy holds, and what a cut takes away.
///
/// <para>
/// <strong>What is copied is the engine's to decide.</strong> Only it knows what is picked out and what language that was
/// written in, so only it can say what a copy carries: pieces chosen whole are the words they draw, and a stretch of a
/// document is the markdown it was written as, the plain words it reads as, and the marked-up text it sets as.
/// </para>
/// <para>
/// <strong>The clipboard itself is the application's</strong>, shared with everything else in the window, so the engine
/// asks for a copy to be put on it and for what is on it to be written here (<see cref="IContentEvents"/>). A cut only takes
/// the words away once the copy is there, so nothing is lost to a copy that went nowhere.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>
    /// Whoever is showing the content, which is the only thing the engine asks anything of
    /// (<see cref="IContentEvents"/>). Null where nothing is showing it — a reading taken for its own sake.
    /// </summary>
    internal IContentEvents? Events { get; set; }

    /// <summary>Asks for what is picked out to go on the clipboard — the whole content where nothing is — and says whether anybody took it.</summary>
    internal bool Copied() => this.Events?.OnCopy(Copy()) is not null;

    /// <summary>
    /// What goes on the clipboard. Pieces chosen whole are the words they draw: a slice's label is its label, and there is no
    /// markdown in it. Anything else is the stretch of the document that is picked out, or the whole of it where none is — where
    /// that stretch is a whole block, whatever a copy of that block holds, and where it is code inside one, that code.
    /// </summary>
    private ContentCopy Copy()
    {
        if (this.PickedText is { } drawn) return new ContentCopy(drawn);

        // A block of columns is its lines, said to be a block, so pasting it back puts it back as one.
        if (this.State.Selected is ColumnRanges columns)
        {
            var lines = ColumnSelection.Text(this.State.Document, columns);
            return new ContentCopy(lines) { Column = true };
        }

        // Where the source is formatting nobody reads, a copy is the words the reader picked out, not the formatting round them.
        if (ReadsAsDrawn)
        {
            var read = DrawnWords;
            var words = Stretch() is { } picked ? read.Within(picked.Start, picked.Length) : read.Text;

            return new ContentCopy(words);
        }
        if (Stretch() is not { } chosen) return CopyOf(this.State.Document, null);

        if (Blocked(chosen.Start) is { } block)
        {
            if (block.Start == chosen.Start && block.Length == chosen.Length) return CopyOf(block);
            if (CopyWritten(block, chosen) is { } written) return written;
        }

        return CopyOf(this.State.Document, chosen);
    }

    /// <summary>
    /// What a copy of the whole of <paramref name="block"/> holds: how the document writes it, and the picture it draws as well
    /// where its language says a picture of it is worth keeping — the same answer that leaves a code fence's Save button off.
    /// </summary>
    internal ContentCopy CopyOf(ContentPart block)
    {
        var copy = CopyOf(this.State.Document, (block.Start, block.Length));

        return KeepsAPicture(block) && this.Events?.OnPicture(block) is { } drawn ? copy with { Picture = drawn } : copy;
    }

    /// <summary>
    /// What a copy of <paramref name="chosen"/>, inside <paramref name="block"/>, holds where the block's language draws every
    /// character written (<see cref="IContentLanguage.ShowsWhatWasWritten"/>) — code: those characters as they stand, in every
    /// flavour. Laid out as markdown on their own they would read as prose, which runs one line of code into the next, and there
    /// is nothing marked up in them to carry. Null for any other language, or a stretch reaching past what the block's language
    /// wrote into the fence round it.
    /// </summary>
    private ContentCopy? CopyWritten(ContentPart block, (int Start, int Length) chosen)
    {
        var (language, body) = _named is not null
            ? (Language(_named), block)
            : (ContentLanguages.Held(block), block.Part(Roles.Body));

        if (language?.Editing.ShowsWhatWasWritten != true || body is null) return null;
        if (chosen.Start < body.Start || chosen.Start + chosen.Length > body.End) return null;

        var characters = this.State.Document.Slice(chosen.Start, chosen.Length);

        return new ContentCopy(characters);
    }

    /// <summary>
    /// The look what is copied out of the content is laid out in, to be spelled for a clipboard: a white page's, whatever the content
    /// is shown in, so what is pasted reads on the page it lands on. The look the content was first laid out in, where nobody says.
    /// </summary>
    public LayoutStyle PaperStyle { get; init; } = style;

    /// <summary>
    /// What a copy of <paramref name="chosen"/> out of <paramref name="document"/> holds — of the whole document where nothing is
    /// chosen, or what was chosen does not lie inside it.
    /// </summary>
    internal ContentCopy CopyOf(ContentDocument document, (int Start, int Length)? chosen) =>
        CopyOf(chosen is { Length: > 0 } picked && picked.Start >= 0 && picked.Start + picked.Length <= document.Length
            ? document.Slice(picked.Start, picked.Length)
            : document.ToString());

    /// <summary>
    /// <paramref name="markdown"/> as a copy holds it: as written, and as it is shown — laid out on the page <see cref="PaperStyle"/>
    /// draws, for whatever puts it somewhere to spell as words and as marked-up text. Laid out for a copy, what another language
    /// drew is the code it is written in, which is what pasting it anywhere that cannot draw the language can use; a picture of it is
    /// kept only for a block copied whole (<see cref="CopyOf(ContentPart)"/>).
    /// </summary>
    internal ContentCopy CopyOf(string markdown) =>
        new(markdown, LayoutExport.Exported(markdown, new ExportOccasion { Style = this.PaperStyle }, _medium));

    /// <summary>Copies what is picked out and then takes it away — but only once it has been put somewhere.</summary>
    internal bool Cut()
    {
        if (_readOnly || Stretch() is not { } chosen) return false;
        var copy = this.State.Selected is ColumnRanges ? Copy() : CopyOf(this.State.Document, chosen);
        if (this.Events?.OnCopy(copy) is not { } copied) return false;

        _ = TakeAwayOnceCopiedAsync(copied, this.State);
        return true;
    }

    /// <summary>
    /// Takes away what was cut once its copy is on the clipboard — and only where nothing was written meanwhile, so what goes is
    /// what was copied.
    /// </summary>
    private async Task TakeAwayOnceCopiedAsync(Task<bool> copied, EditState cutFrom)
    {
        if (await copied && ReferenceEquals(this.State, cutFrom)) Replace(cutFrom.Write(string.Empty));
    }

    /// <summary>Asks for what is on the clipboard to be written at the caret, and says whether anybody took it on.</summary>
    internal bool Paste() => !_readOnly && this.Events?.OnPaste() == true;

    /// <summary>
    /// Writes what was pasted at the caret: the words go to the language the caret is in, which says what they come to there and
    /// whose parser spells them; where it says nothing, the markdown they were copied as is written as it stands.
    /// </summary>
    internal void WritePasted(string words, string markdown)
    {
        if (_readOnly) return;

        // Over a block of columns: on every line of it, a line of what was pasted on each line where it has as many.
        if (this.State.Selected is ColumnRanges)
        {
            Replace(this.State.Write((words.Length > 0 ? words : markdown).ReplaceLineEndings("\n")));
            return;
        }

        if (words.Length > 0 && Pasted(words.ReplaceLineEndings("\n"))) return;
        if (markdown.Length > 0) Replace(this.State.Insert(markdown.ReplaceLineEndings("\n")));
    }

    /// <summary>The stretch picked out, from where it starts and how long it is — null where nothing is.</summary>
    private (int Start, int Length)? Stretch() =>
        this.State.HasSelection ? (this.State.SelectionStart, this.State.SelectionLength) : null;
}
