using System;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>What changed in the source, and how it came to change.</summary>
/// <param name="before">The content as it was.</param>
/// <param name="after">The content as it is now.</param>
/// <param name="kind">Whether it was written, taken back, written again, put there on the reader's behalf, or loaded.</param>
public sealed class ContentSourceChange(EditState before, EditState after, ContentChangeKind kind) : EventArgs
{
    private ContentChange? _change;

    public EditState Before { get; } = before;

    public EditState After { get; } = after;

    public ContentChangeKind Kind { get; } = kind;

    /// <summary>
    /// What was written: the change that makes <see cref="After"/> from <see cref="Before"/>, in the offsets of what it was. Known
    /// from the version the change made; found by comparing the two once only where the content was put there whole.
    /// </summary>
    public ContentChange Change => _change ??= this.After.Document.ChangesFrom(this.Before.Document).Forward;

    /// <summary>The stretch the change wrote over: from the first character it wrote over to the last, as it was and as it is.</summary>
    public (int Start, int Removed, int Inserted) Span
    {
        get
        {
            var writes = this.Change.Writes;
            if (writes.Count == 0) return (this.After.Caret, 0, 0);

            var (start, end) = (writes.Min(write => write.Start), writes.Max(write => write.End));
            var grown = writes.Sum(write => write.Text.Length - write.Length);

            return (start, end - start, end - start + grown);
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

    /// <summary>Something else was shown in its place, whole: a file opened, a reply that grew, a note loaded. Nothing to take back.</summary>
    Loaded,
}
