using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Languages;

/// <summary>What plain text shows and offers: every character a writer typed, the keys a text editor has, and no picture of it.</summary>
internal sealed class TextEditing : IContentLanguage
{
    /// <summary>Text is its characters: every one a writer typed is on the page.</summary>
    public bool ShowsWhatWasWritten => true;

    /// <summary>Enter carries a line's indent on, and Tab and Shift+Tab indent, as in a text editor (<see cref="LineIndenting"/>).</summary>
    public IOnEdit? OnEdit => LineIndenting.Instance;

    /// <summary>Every way of the clipboard, as the engine does it: what its edits write beyond the characters typed is only indent.</summary>
    public ClipboardOffers Clipboard(ContentAsk ask) => ClipboardOffers.All;

    /// <summary>A picture of text is a worse copy of the text, so that is the one usual button this does not offer.</summary>
    public BlockCorner Corner(ContentAsk ask) => new(Saves: false);
}
