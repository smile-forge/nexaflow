namespace Nexaflow.Interaction;

/// <summary>What a row puts on the clipboard when it is copied out of a list: the row as a line of text.</summary>
public interface IClipboardText
{
    string ClipboardText { get; }
}
