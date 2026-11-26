using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// A file longer than one document can hold, shown as a document over a window of it (<see cref="Document"/>) that moves as the reader
/// does (<see cref="MoveTo"/>) — so everything that shows and writes in a document sees an ordinary one, and only this knows the window.
///
/// <para>
/// <strong>What is outside the window is held as stretches.</strong> Before and after it lie stretches of the file as it was opened —
/// only where they lie in it — or what was written in a stretch while the window was over it, as the document it came to, sharing its
/// leaves. Moving the window is one change to its document: what leaves it at one end is cut away and kept as a stretch, and what comes
/// into it at the other is written in as the leaves of the stretches it was held in, so the caret, what is picked out, the marks and the
/// anchors are carried through it as through any change, and nothing written is lost.
/// </para>
/// <para>
/// <strong>One history, in the file's offsets.</strong> Every step is kept where in the whole file it was written
/// (<see cref="EditHistory.Origin"/>), so a step written before the window moved is taken back where it was written: taking back or
/// writing again a step outside the window moves the window to it first (<see cref="EditedDocument.ReachUndo"/>).
/// </para>
/// <para>
/// <strong>Saved a stretch at a time.</strong> Saving (<see cref="Save"/>) writes every stretch in order — the file's own a block at a
/// time — beside the file and puts it in its place; the window then stands over the file just written, every step kept.
/// </para>
/// </summary>
public sealed class ContentFileWindow
{
    /// <summary>How many characters a window holds, at most: room enough to read far around the reader, and far under what a document can hold.</summary>
    public const int DefaultSize = 512 * 1024 * 1024;

    /// <summary>What lies before the window, in order.</summary>
    private List<Outside> _before = [];

    /// <summary>What lies after the window, in order.</summary>
    private List<Outside> _after = [];

    private ContentFileWindow(ContentFile file, int size)
    {
        this.File = file;
        this.Size = size;
        this.Document = new EditedDocument();
    }

    /// <summary>The file it is a window onto — the one last saved to, once it has been saved.</summary>
    public ContentFile File { get; private set; }

    /// <summary>What the window holds, as a document written in like any other.</summary>
    public EditedDocument Document { get; }

    /// <summary>How many characters the window holds, at most.</summary>
    public int Size { get; }

    /// <summary>Where in the whole file the window starts.</summary>
    public long Start { get; private set; }

    /// <summary>How many characters the whole file comes to, as it is written now.</summary>
    public long Length => this.Start + this.Document.Current.Length + _after.Sum(stretch => stretch.Length);

    /// <summary>A window of at most <paramref name="size"/> characters onto <paramref name="file"/>, scanned whole, standing round <paramref name="at"/>.</summary>
    public static ContentFileWindow Open(ContentFile file, long at = 0, int size = DefaultSize)
    {
        var window = new ContentFileWindow(file, Math.Max(size, 1));
        var total = file.Characters;
        var start = Math.Clamp(at - (window.Size / 2), 0, Math.Max(0, total - window.Size));
        var length = (int)Math.Min(window.Size, total - start);

        if (start > 0) window._before = [new Outside(0, start, null)];
        if (start + length < total) window._after = [new Outside(start + length, total - start - length, null)];

        window.Start = start;
        window.Document.Load(window.Original(start, length));
        window.Document.Window = window;

        return window;
    }

    /// <summary>Whether the window holds the characters from <paramref name="start"/> to <paramref name="end"/>, offsets in the whole file.</summary>
    public bool Covers(long start, long end) => start >= this.Start && end <= this.Start + this.Document.Current.Length;

    /// <summary>Moves the window, where it does not hold them, so the characters from <paramref name="start"/> to <paramref name="end"/> stand in its middle.</summary>
    public void Reach(long start, long end)
    {
        if (!this.Covers(start, end)) this.MoveTo(start + ((end - start) / 2));
    }

    /// <summary>
    /// Moves the window so <paramref name="centre"/>, an offset in the whole file, stands in its middle — as near as the ends of the file let it —
    /// as one change to its document, which every view of it carries the caret, what is picked out and the marks through.
    /// </summary>
    public void MoveTo(long centre)
    {
        var current = this.Document.Current;
        var (start, length, total) = (this.Start, current.Length, this.Length);
        var to = Math.Clamp(centre - (this.Size / 2), 0, Math.Max(0, total - this.Size));
        var reaching = (int)Math.Min(this.Size, total - to);
        var (end, reaches) = (start + length, to + reaching);

        if (to == start && reaching == length) return;

        // The whole of what is written, with the window as what it holds now.
        List<Outside> all = [.. _before, new Outside(0, length, current), .. _after];

        // Where the windows overlap, what both hold stays as it is, and only each end is written; where they do not, all of it is.
        var (kept, keptEnd) = (Math.Max(start, to), Math.Min(end, reaches));
        var writes = new List<ContentWrite>();

        if (kept < keptEnd)
        {
            if (to != start) writes.Add(new ContentWrite(0, (int)(kept - start), this.Joined(Slice(all, to, kept))));
            if (reaches != end) writes.Add(new ContentWrite((int)(keptEnd - start), (int)(end - keptEnd), this.Joined(Slice(all, keptEnd, reaches))));
        }
        else
        {
            writes.Add(new ContentWrite(0, length, this.Joined(Slice(all, to, reaches))));
        }

        (_before, _after) = (Slice(all, 0, to), Slice(all, reaches, total));
        this.Start = to;
        this.Document.Move(new ContentChange(writes, 0), to - start);
    }

    /// <summary>
    /// Saves the whole of what is written to <paramref name="path"/> in <paramref name="encoding"/>, with its mark where <paramref name="preamble"/>
    /// says — every stretch in order, the file's own a block at a time — beside the file and then in its place; the window then stands over
    /// the file just written, at the same place, with every step kept, and that file, held against other writers where <paramref name="hold"/>
    /// says, is what comes back. The file it stood over before is let go of once nothing holds it.
    /// </summary>
    public ContentFile Save(string path, Encoding encoding, bool preamble, bool hold = true)
    {
        var full = ContentFile.Replace(path, encoding, preamble, writer =>
        {
            foreach (var stretch in _before) this.WriteTo(writer, stretch);
            this.Document.WriteTo(writer);
            foreach (var stretch in _after) this.WriteTo(writer, stretch);
        });

        var saved = ContentFile.Open(full, encoding, preamble ? encoding.GetPreamble().Length : 0, this.File.Access);
        saved.Scan();
        if (hold) saved.HoldForEdit();

        var (start, length, total) = (this.Start, this.Document.Current.Length, saved.Characters);
        this.File = saved;
        _before = start > 0 ? [new Outside(0, start, null)] : [];
        _after = start + length < total ? [new Outside(start + length, total - start - length, null)] : [];

        this.Document.Rebase(this.Original(start, length));
        this.Document.MarkSaved();

        return saved;
    }

    /// <summary>The file's own characters from <paramref name="start"/> over <paramref name="length"/>, as a document over its blocks.</summary>
    private ContentDocument Original(long start, long length)
    {
        if (length == 0) return ContentDocument.Empty;

        var (first, last, skip) = this.File.BlocksOf(start, length);
        return ContentDocument.Of(this.File, first, last).Stretch(skip, (int)length);
    }

    /// <summary><paramref name="stretches"/> as one document: the file's own read over its blocks, what was written as it stands.</summary>
    private ContentDocument Joined(List<Outside> stretches) =>
        ContentDocument.Joined([.. stretches.Select(stretch => stretch.Held ?? this.Original(stretch.From, stretch.Length))]);

    /// <summary>Writes <paramref name="stretch"/> to <paramref name="writer"/>: the file's own a block at a time, what was written as it stands.</summary>
    private void WriteTo(TextWriter writer, Outside stretch)
    {
        if (stretch.Held is { } held)
        {
            held.WriteTo(writer);
            return;
        }

        var (first, last, skip) = this.File.BlocksOf(stretch.From, stretch.Length);
        var left = stretch.Length;

        for (var block = first; block < last && left > 0; block++)
        {
            var text = this.File.Block(block);
            var taken = (int)Math.Min(text.Length - skip, left);

            writer.Write(text.AsSpan(skip, taken));
            (left, skip) = (left - taken, 0);
        }
    }

    /// <summary>
    /// The stretches of <paramref name="stretches"/>, the whole of what is written in order, from <paramref name="from"/> to <paramref name="to"/>:
    /// each cut to what of it lies there, and the file's own that follow on from each other joined into one.
    /// </summary>
    private static List<Outside> Slice(List<Outside> stretches, long from, long to)
    {
        var sliced = new List<Outside>();
        var at = 0L;

        foreach (var stretch in stretches)
        {
            var (low, high) = (Math.Max(from, at) - at, Math.Min(to, at + stretch.Length) - at);
            at += stretch.Length;
            if (low >= high) continue;

            var piece = stretch.Held is { } held
                ? new Outside(0, high - low, held.Stretch((int)low, (int)(high - low)))
                : new Outside(stretch.From + low, high - low, null);

            if (piece.Held is null && sliced.Count > 0 && sliced[^1] is { Held: null } before && before.From + before.Length == piece.From)
                sliced[^1] = before with { Length = before.Length + piece.Length };
            else
                sliced.Add(piece);
        }

        return sliced;
    }

    /// <summary>
    /// A stretch of the whole of what is written outside the window: the file's own characters from <paramref name="From"/> over
    /// <paramref name="Length"/>, or — where <paramref name="Held"/> holds them — what was written there, as the document it came to.
    /// </summary>
    private readonly record struct Outside(long From, long Length, ContentDocument? Held);
}
