using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What moving part of some content came to: the content as it reads with what is carried let go there — a version made from the
/// one being written in by the move's change, so letting go writes exactly that change and taking it back undoes exactly it —
/// the change itself, and the stretch it wrote, what to mark out while it is still being carried.
/// </summary>
public readonly record struct Moved(EditState State, ContentChange Change, EditRange Wrote)
{
    /// <summary>Where the caret goes.</summary>
    public int Caret => State.Caret;

    /// <summary>Whether <paramref name="other"/> is the same move: the same change, and the caret left in the same place.</summary>
    public bool Alike(Moved? other) => other is { } was && this.Caret == was.Caret && this.Change.WritesAlike(was.Change);
}
