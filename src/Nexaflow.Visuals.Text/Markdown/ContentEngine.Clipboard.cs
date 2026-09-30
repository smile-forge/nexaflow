using System;
using System.Windows.Media.Imaging;
using Nexaflow.Markdown.Ast;

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
/// asks for a copy to be put on it (<see cref="Copying"/>) and asks for what is on it to be written here
/// (<see cref="Pasting"/>). A cut only takes the words away once somebody has answered, so nothing is lost to a copy that
/// went nowhere.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>Asked to put a copy on the clipboard. True where somebody did.</summary>
    internal Func<MarkdownClipboard.ContentCopy, bool>? Copying { get; set; }

    /// <summary>
    /// Asked for what is on the clipboard, which whoever holds it takes off and hands back as words and as markdown. Null
    /// where nobody answered; both empty where somebody did and there was nothing here to write.
    /// </summary>
    internal Func<(string Words, string Markdown)?>? Pasting { get; set; }

    /// <summary>
    /// Asked for a picture of a block. Deciding whether one belongs is the engine's — the language says — but drawing it is
    /// whatever painted it, since a picture is of what was painted and not of what was read.
    /// </summary>
    internal Func<ContentPart, BitmapSource?>? Picturing { get; set; }

    /// <summary>Asks for what is picked out to go on the clipboard — the whole content where nothing is — and says whether it did.</summary>
    internal bool Copied() => this.Copying?.Invoke(Copy()) == true;

    /// <summary>
    /// What goes on the clipboard. Pieces chosen whole are the words they draw: a slice's label is its label, and there is no
    /// markdown in it. Anything else is the stretch of the document that is picked out, or the whole of it where none is — and
    /// where that stretch is a whole block, whatever a copy of that block holds.
    /// </summary>
    private MarkdownClipboard.ContentCopy Copy()
    {
        if (this.PickedText is { } drawn) return new MarkdownClipboard.ContentCopy(drawn, drawn, string.Empty);
        if (Stretch() is not { } chosen) return MarkdownClipboard.Copied(_state.Source, null);

        return Blocked(chosen.Start) is { } block && block.Start == chosen.Start && block.Length == chosen.Length
            ? CopyOf(block)
            : MarkdownClipboard.Copied(_state.Source, chosen);
    }

    /// <summary>
    /// What a copy of the whole of <paramref name="block"/> holds: how the document writes it, and the picture it draws as well
    /// where its language says a picture of it is worth keeping — the same answer that leaves a code fence's Save button off.
    /// </summary>
    internal MarkdownClipboard.ContentCopy CopyOf(ContentPart block)
    {
        var copy = MarkdownClipboard.Copied(_state.Source, (block.Start, block.Length));

        return KeepsAPicture(block) && this.Picturing?.Invoke(block) is { } drawn ? copy with { Picture = drawn } : copy;
    }

    /// <summary>Copies what is picked out and then takes it away — but only once it has been put somewhere.</summary>
    internal bool Cut()
    {
        if (_readOnly || Stretch() is not { } chosen) return false;
        if (this.Copying?.Invoke(MarkdownClipboard.Copied(_state.Source, chosen)) != true) return false;

        Replace(_state.Write(string.Empty));
        return true;
    }

    /// <summary>
    /// Writes what is on the clipboard at the caret: the words go to the language the caret is in, which says what they come to
    /// there and whose parser spells them; where it says nothing, the markdown they were copied as is written as it stands.
    /// </summary>
    internal bool Paste()
    {
        if (_readOnly || this.Pasting?.Invoke() is not { } said) return false;

        if (said.Words.Length > 0 && Pasted(said.Words.ReplaceLineEndings("\n"))) return true;
        if (said.Markdown.Length > 0) Replace(_state.Insert(said.Markdown.ReplaceLineEndings("\n")));

        // Somebody answered, so the key was theirs whether or not anything came of it here.
        return true;
    }

    /// <summary>The stretch picked out, from where it starts and how long it is — null where nothing is.</summary>
    private (int Start, int Length)? Stretch() =>
        _state.HasSelection ? (_state.SelectionStart, _state.SelectionLength) : null;
}
