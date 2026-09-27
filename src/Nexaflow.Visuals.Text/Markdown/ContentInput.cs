using System;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// What the reader did to content, said in the content's own units — what whatever shows content hands its engine
/// (<see cref="ContentEngine.Input"/>), and all it hands it. A real key and a test's key are the same thing by the time they
/// get here, so what one does is what the other does.
/// </summary>
public abstract record ContentInput;

/// <summary>A key pressed, with the modifiers held.</summary>
public sealed record ContentKey(Key Key, ModifierKeys Modifiers = ModifierKeys.None) : ContentInput;

/// <summary>Characters typed — what a key, a composition or a dictation comes to.</summary>
public sealed record ContentText(string Text) : ContentInput;

/// <summary>A press at a point, the second of two in quick succession where <paramref name="Clicks"/> says so.</summary>
public sealed record ContentPress(Point At, int Clicks = 1, ModifierKeys Modifiers = ModifierKeys.None) : ContentInput;

/// <summary>
/// The pointer moved while pressed, far enough to be a drag rather than a hand's tremor — how far that is being the
/// pointer's to say, since it is counted in the pixels of the screen.
/// </summary>
public sealed record ContentDrag(Point At) : ContentInput;

/// <summary>The press was let go.</summary>
public sealed record ContentRelease : ContentInput;

/// <summary>The pointer is over the content at a point, or — null — has left it.</summary>
public sealed record ContentHover(Point? At) : ContentInput;

/// <summary>What changed in the source, and how it came to change.</summary>
/// <param name="Before">The content as it was.</param>
/// <param name="After">The content as it is now.</param>
/// <param name="Kind">Whether it was written, taken back, written again, or put there by whatever shows it.</param>
public sealed class ContentSourceChange(EditState before, EditState after, ContentChangeKind kind) : EventArgs
{
    public EditState Before { get; } = before;

    public EditState After { get; } = after;

    public ContentChangeKind Kind { get; } = kind;

    /// <summary>Where the two differ: from the first character that is not the same to the last, as it was and as it is.</summary>
    public (int Start, int Removed, int Inserted) Span
    {
        get
        {
            var (was, now) = (Before.Source, After.Source);
            var shorter = Math.Min(was.Length, now.Length);

            var start = 0;
            while (start < shorter && was[start] == now[start]) start++;

            var same = 0;
            while (same < shorter - start && was[was.Length - 1 - same] == now[now.Length - 1 - same]) same++;

            return (start, was.Length - start - same, now.Length - start - same);
        }
    }
}

/// <summary>How a change to the source came about.</summary>
public enum ContentChangeKind
{
    /// <summary>The reader wrote it.</summary>
    Written,

    /// <summary>The reader took back what they last wrote.</summary>
    Undone,

    /// <summary>The reader wrote again what they took back.</summary>
    Redone,

    /// <summary>Whatever shows the content put it there on the reader's behalf — a paste, a drop, a block put in.</summary>
    Replaced,
}
