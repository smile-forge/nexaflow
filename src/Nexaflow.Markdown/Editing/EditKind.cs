namespace Nexaflow.Markdown.Editing;

/// <summary>
/// What a key did: wrote something, ended what was half-written, broke a line, took back the character on one side of the caret,
/// moved on to the next place to write in, asked for something new where the caret is, or chose from what was offered.
/// </summary>
public enum EditKind
{
    /// <summary>Text written at the caret — a key, a palette's symbol.</summary>
    Typing,

    /// <summary>Words pasted at the caret, or over what is picked out: the clipboard's, as plain words.</summary>
    Pasting,

    /// <summary>Space or Enter: what ends whatever is half-written.</summary>
    Settling,

    /// <summary>Shift+Enter: a line broken inside what is being written, rather than what comes after it.</summary>
    Breaking,

    /// <summary>Backspace: the character before the caret.</summary>
    Erasing,

    /// <summary>Delete: the character after it.</summary>
    Deleting,

    /// <summary>Tab: on to the next place to write in.</summary>
    Tabbing,

    /// <summary>Shift+Tab: back to the one before.</summary>
    TabbingBack,

    /// <summary>Insert: something new, where the caret is or before what is picked out.</summary>
    Inserting,

    /// <summary>One of what the language offered was chosen; <see cref="ContentEdit.Text"/> is its verb.</summary>
    Choosing,

    /// <summary>What is picked out, carried and let go over the piece the edit applied to.</summary>
    Dropping,

    /// <summary>Page Up: a step up of whatever the content makes one of — an octave, in a tune.</summary>
    Raising,

    /// <summary>Page Down: a step down of the same.</summary>
    Lowering,
}
