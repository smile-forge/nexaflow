using System;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// A document being written in: the version it is at, what was written so it can be taken back, and whether that is what was
/// last saved. What a page holds to show content and to hear what became of it — the same object whatever shows it.
///
/// <para>
/// <strong>Nothing here is a control's.</strong> It is a plain object a view-model holds and binds whatever shows the content to,
/// saying what changed through <see cref="Changed"/> and <see cref="INotifyPropertyChanged"/>, which any interface toolkit
/// can listen to. So the document, its history and whether it was saved stay where they are when what draws them does not.
/// </para>
/// <para>
/// <strong>The characters are handed over only when asked.</strong> Writing in it makes <see cref="Current"/> another version
/// and says what changed; nothing builds a string of it to say so. A page learns whether what is shown differs from what was
/// saved (<see cref="IsDirty"/>) and what was just written (<see cref="Changed"/>), and reads the characters when it saves them
/// (<see cref="WriteTo"/>) or has to have them (<see cref="ToString"/>).
/// </para>
/// <para>
/// <strong>Two ways in, one way out.</strong> Whatever shows it writes what the reader does through <see cref="Wrote"/>,
/// <see cref="Undo"/> and <see cref="Redo"/>, since only it knows where the caret was; a page writes through <see cref="Load"/>
/// and <see cref="Write"/>. Either way <see cref="Changed"/> is raised once, with what changed, and whatever shows it takes up a
/// change it did not make.
/// </para>
/// </summary>
public sealed class EditedDocument : INotifyPropertyChanged
{
    private ContentDocument _current;

    /// <summary>The version last saved or loaded: what <see cref="IsDirty"/> holds what is written up against.</summary>
    private ContentDocument _saved;

    private bool _dirty;

    /// <summary>A document holding <paramref name="text"/>, saved as it is.</summary>
    public EditedDocument(string? text = null)
    {
        _current = _saved = ContentDocument.Of(text);
        this.History.Changed += (_, _) => this.Restated();
    }

    /// <summary>The version it is at.</summary>
    public ContentDocument Current => _current;

    /// <summary>What was written, so it can be taken back.</summary>
    public EditHistory History { get; } = new();

    /// <summary>
    /// Whether what it holds differs from what was last saved or loaded — the characters, not the steps: a change written and then
    /// written back by hand is no change. Asked of the history first, which knows at no cost when every step since was taken back,
    /// then of the lengths, which differ after nearly every keystroke; only an edit that leaves the length as it was compares the
    /// characters.
    /// </summary>
    public bool IsDirty => _dirty;

    /// <summary>Whether there is anything to take back.</summary>
    public bool CanUndo => this.History.CanUndo;

    /// <summary>Whether anything taken back could be written again.</summary>
    public bool CanRedo => this.History.CanRedo;

    /// <summary>Raised once for every version it moves to, with what changed and how it came to.</summary>
    public event EventHandler<ContentSourceChange>? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    // ── For the page ────────────────────────────────────────────────────────

    /// <summary>
    /// Shows <paramref name="text"/> in its place, whole: a file opened, a reply that grew. Nothing written before it can be taken
    /// back, and what it holds now is what was saved.
    /// </summary>
    public void Load(string? text)
    {
        var before = new EditState(_current, 0);

        _current = _saved = ContentDocument.Of(text);
        this.History.Clear();

        this.Told(before, new EditState(_current, 0), ContentChangeKind.Loaded);
        this.Restated();
    }

    /// <summary>
    /// Writes <paramref name="change"/> on the reader's behalf — an edit an assistant made, a block a page put in — as a step of its
    /// own that can be taken back. Whatever shows the document takes it up, carrying the caret through it.
    /// </summary>
    public void Write(ContentChange change)
    {
        if (change.Writes.Count == 0) return;

        var before = new EditState(_current, change.Writes.Min(write => write.Start));
        var after = before.Made(change);
        if (ReferenceEquals(after.Document, _current)) return;

        this.History.Break();
        this.History.Record(before, after);
        this.History.Break();

        _current = after.Document;
        this.Told(before, after, ContentChangeKind.Replaced);
    }

    /// <summary>Says that what it holds now has been saved: <see cref="IsDirty"/> is false here and until something else is written.</summary>
    public void MarkSaved()
    {
        _saved = _current;
        this.History.MarkSaved();
    }

    /// <summary>Writes what it holds to <paramref name="writer"/> — what saving it is, without a copy of it being made to hand over.</summary>
    public void WriteTo(TextWriter writer) => _current.WriteTo(writer);

    /// <summary>What it holds, all of it.</summary>
    public override string ToString() => _current.ToString();

    // ── For whatever shows it ───────────────────────────────────────────────

    /// <summary>
    /// What the reader wrote: the document went from <paramref name="before"/> to <paramref name="after"/>, a step to take back —
    /// joining the step being written where it carries on from it.
    /// </summary>
    public void Wrote(EditState before, EditState after, ContentChangeKind kind = ContentChangeKind.Written)
    {
        if (ReferenceEquals(after.Document, _current) && ReferenceEquals(before.Document, _current)) return;

        this.History.Record(before, after);

        _current = after.Document;
        this.Told(before, after, kind);
    }

    /// <summary>The state to go back to from <paramref name="now"/>, now what it holds — or null where there is nothing to take back.</summary>
    public EditState? Undo(EditState now) => this.Stepped(now, this.History.Undo(now), ContentChangeKind.Undone);

    /// <summary>The state to go forward to again from <paramref name="now"/>, now what it holds — or null where nothing was taken back.</summary>
    public EditState? Redo(EditState now) => this.Stepped(now, this.History.Redo(now), ContentChangeKind.Redone);

    private EditState? Stepped(EditState from, EditState? to, ContentChangeKind kind)
    {
        if (to is null) return null;

        _current = to.Document;
        this.Told(from, to, kind);

        return to;
    }

    private void Told(EditState before, EditState after, ContentChangeKind kind)
    {
        this.Restated();
        this.Changed?.Invoke(this, new ContentSourceChange(before, after, kind));
        this.Said(nameof(this.Current));
    }

    /// <summary>What the history now says about taking back and about what was saved, said to whoever is listening where it moved.</summary>
    private void Restated()
    {
        var dirty = !this.History.IsAtSaved && (_current.Length != _saved.Length || !_current.Equals(_saved));

        if (dirty != _dirty)
        {
            _dirty = dirty;
            this.Said(nameof(this.IsDirty));
        }

        this.Said(nameof(this.CanUndo));
        this.Said(nameof(this.CanRedo));
    }

    private void Said(string property) => this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
