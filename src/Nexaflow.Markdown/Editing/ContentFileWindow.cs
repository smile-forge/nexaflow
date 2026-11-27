using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

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
/// <strong>It stands on whole lines.</strong> The window starts where a line starts and ends where one ends, so its lines are the file's,
/// counted on from how many stand before it (<see cref="LinesBefore"/>) — what a line number beside it says, and what a line of the whole
/// file is gone to by (<see cref="OffsetOfLine"/>).
/// </para>
/// <para>
/// <strong>One history, in the file's offsets.</strong> Every step is kept where in the whole file it was written
/// (<see cref="EditHistory.Origin"/>), so a step written before the window moved is taken back where it was written: taking back or
/// writing again a step outside the window moves the window to it first (<see cref="EditedDocument.ReachUndo"/>).
/// </para>
/// <para>
/// <strong>Saved a stretch at a time.</strong> Saving (<see cref="Save"/>) writes every stretch in order — the file's own a block at a
/// time — beside the file and puts it in its place; the window then stands over the file just written, every step kept. Anything else
/// that keeps the whole of what is written — a file in an archive, on a location — is written through <see cref="WriteTo"/>.
/// </para>
/// </summary>
public sealed class ContentFileWindow
{
    /// <summary>How many characters a window holds, at most: room enough to read far around the reader, and far under what a document can hold.</summary>
    public const int DefaultSize = 512 * 1024 * 1024;

    /// <summary>How far past where the window would stand it looks for the end of a line to stand at instead.</summary>
    private const int LineReach = 64 * 1024;

    /// <summary>What lies before the window, in order.</summary>
    private List<Outside> _before = [];

    /// <summary>What lies after the window, in order.</summary>
    private List<Outside> _after = [];

    /// <summary>How many line breaks stand after the window.</summary>
    private long _breaksAfter;

    private ContentFileWindow(ContentFile file, int size, EditedDocument document)
    {
        this.File = file;
        this.Size = size;
        this.Document = document;
    }

    /// <summary>The file it is a window onto — the one last saved to, once it has been saved.</summary>
    public ContentFile File { get; private set; }

    /// <summary>What the window holds, as a document written in like any other.</summary>
    public EditedDocument Document { get; }

    /// <summary>How many characters the window holds, at most — give or take the rest of the line it ends on.</summary>
    public int Size { get; }

    /// <summary>Where in the whole file the window starts — always where a line does.</summary>
    public long Start { get; private set; }

    /// <summary>How many lines of the file stand before the window: what the window's lines are counted on from.</summary>
    public long LinesBefore { get; private set; }

    /// <summary>How many characters the whole file comes to, as it is written now.</summary>
    public long Length => this.Start + this.Document.Current.Length + _after.Sum(stretch => stretch.Length);

    /// <summary>How many lines the whole file comes to, as it is written now.</summary>
    public long LineCount => this.LinesBefore + this.Document.Current.LineCount + _breaksAfter;

    /// <summary>A window of at most <paramref name="size"/> characters onto <paramref name="file"/>, scanned whole, standing round <paramref name="at"/>.</summary>
    public static ContentFileWindow Open(ContentFile file, long at = 0, int size = DefaultSize) => Open(file, new EditedDocument(), at, size);

    /// <summary>
    /// A window of at most <paramref name="size"/> characters onto <paramref name="file"/>, scanned whole, standing round <paramref name="at"/> —
    /// shown as <paramref name="document"/>, the page's own, which keeps whatever shows it through every move of the window.
    /// </summary>
    public static ContentFileWindow Open(ContentFile file, EditedDocument document, long at = 0, int size = DefaultSize)
    {
        var window = new ContentFileWindow(file, Math.Max(size, 1), document);
        var total = file.Characters;
        List<Outside> all = [new Outside(0, total, null)];

        var start = window.LineStartFrom(all, Math.Clamp(at - (window.Size / 2), 0, Math.Max(0, total - window.Size)), total);
        var end = window.LineStartFrom(all, Math.Min(total, start + window.Size), total);

        if (start > 0) window._before = [new Outside(0, start, null)];
        if (end < total) window._after = [new Outside(end, total - end, null)];

        window.Start = start;
        window.Document.Load(window.Original(start, end - start));
        window.Document.Window = window;
        window.Counted();

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
    /// Moves the window so <paramref name="centre"/>, an offset in the whole file, stands in its middle — as near as the ends of the file let it,
    /// and on whole lines — as one change to its document, which every view of it carries the caret, what is picked out and the marks through.
    /// </summary>
    public void MoveTo(long centre)
    {
        var current = this.Document.Current;
        var (start, length, total) = (this.Start, current.Length, this.Length);

        // The whole of what is written, with the window as what it holds now.
        List<Outside> all = [.. _before, new Outside(0, length, current), .. _after];

        var to = this.LineStartFrom(all, Math.Clamp(centre - (this.Size / 2), 0, Math.Max(0, total - this.Size)), total);
        var reaches = this.LineStartFrom(all, Math.Min(total, to + this.Size), total);
        var end = start + length;

        if (to == start && reaches == end) return;

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
        this.Counted();
        this.Document.Move(new ContentChange(writes, 0), to - start);
    }

    /// <summary>
    /// Where line <paramref name="line"/> of the whole file starts, counting from nought, as it is written now — or null for a line it does not
    /// have. Found from what the file's blocks say of their lines, reading only the block the line starts in.
    /// </summary>
    public long? OffsetOfLine(long line)
    {
        if (line < 0) return null;
        if (line == 0) return 0;

        var (at, breaks) = (0L, 0L);
        List<Outside> all = [.. _before, new Outside(0, this.Document.Current.Length, this.Document.Current), .. _after];

        foreach (var stretch in all)
        {
            var within = this.BreaksIn(stretch);

            if (breaks + within >= line)
            {
                // The line starts in this stretch: after its (line - breaks)th break.
                var wanted = (int)(line - breaks);
                return at + (stretch.Held is { } held ? held.OffsetOf(wanted) : this.AfterBreak(stretch, wanted));
            }

            (at, breaks) = (at + stretch.Length, breaks + within);
        }

        return null;
    }

    /// <summary>
    /// Saves the whole of what is written to <paramref name="path"/> in <paramref name="encoding"/>, with its mark where <paramref name="preamble"/>
    /// says — every stretch in order, the file's own a block at a time — beside the file and then in its place; the window then stands over
    /// the file just written, at the same place, with every step kept, and that file, held against other writers where <paramref name="hold"/>
    /// says, is what comes back. The file it stood over before is let go of once nothing holds it.
    /// </summary>
    public ContentFile Save(string path, Encoding encoding, bool preamble, bool hold = true)
    {
        var saved = this.WriteOut(path, encoding, preamble, hold);
        this.StandOver(saved);

        return saved;
    }

    /// <summary>
    /// The writing half of <see cref="Save"/>: the whole of what is written put in <paramref name="path"/>'s place, and the file it now is — touching
    /// nothing the window shows, so it can be done away from whatever shows it, which then stands the window over the file
    /// (<see cref="StandOver"/>).
    /// </summary>
    public ContentFile WriteOut(string path, Encoding encoding, bool preamble, bool hold = true)
    {
        var full = ContentFile.Replace(path, encoding, preamble, this.WriteTo);

        var saved = ContentFile.Open(full, encoding, preamble ? encoding.GetPreamble().Length : 0, this.File.Access);
        saved.Scan();
        if (hold) saved.HoldForEdit();

        return saved;
    }

    /// <summary>Stands the window over <paramref name="saved"/>, what it was just written to: at the same place, with every step kept, and nothing left to save.</summary>
    public void StandOver(ContentFile saved)
    {
        var (start, length, total) = (this.Start, this.Document.Current.Length, saved.Characters);
        this.File = saved;
        _before = start > 0 ? [new Outside(0, start, null)] : [];
        _after = start + length < total ? [new Outside(start + length, total - start - length, null)] : [];

        this.Document.Rebase(this.Original(start, length));
        this.Document.MarkSaved();
        this.Counted();
    }

    /// <summary>
    /// Writes the whole of what is written to <paramref name="writer"/>, in order — the file's own a block at a time, what was written as it
    /// stands — as it was when it was asked, whatever the window does meanwhile: so it can be written, or read, away from whatever moves it.
    /// </summary>
    public void WriteTo(TextWriter writer)
    {
        var (before, now, after) = (_before, this.Document.Current, _after);

        foreach (var stretch in before) this.WriteTo(writer, stretch);
        now.WriteTo(writer);
        foreach (var stretch in after) this.WriteTo(writer, stretch);
    }

    /// <summary>The <paramref name="length"/> characters of the whole of what is written from <paramref name="start"/> — inside the window or not.</summary>
    public string Text(long start, int length)
    {
        var current = this.Document.Current;
        List<Outside> all = [.. _before, new Outside(0, current.Length, current), .. _after];
        var end = Math.Min(this.Length, start + Math.Max(0, length));

        return start >= end ? string.Empty : this.Joined(Slice(all, Math.Max(0, start), end)).ToString();
    }

    /// <summary>
    /// Hands <paramref name="each"/> every line of the whole of what is written, in order — its number, counting from nought, where in the file it
    /// starts, and what is written on it without its line end — read a block at a time, so nothing is held but the line being read. Stops where
    /// <paramref name="cancel"/> says.
    /// </summary>
    public void EachLine(Action<long, long, string> each, CancellationToken cancel = default)
    {
        using var lines = new ContentLineSplitter(each, cancel);

        this.WriteTo(lines);
        lines.Ended();
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

    /// <summary>How many lines stand before the window and how many line breaks after it, said again after the window moved or was saved.</summary>
    private void Counted()
    {
        this.LinesBefore = _before.Sum(this.BreaksIn);
        _breaksAfter = _after.Sum(this.BreaksIn);
    }

    /// <summary>
    /// How many line breaks <paramref name="stretch"/> holds — a return and a line feed together being one, as a document counts them: what its
    /// blocks say, where it holds them whole, and its characters counted where it holds part of one.
    /// </summary>
    private long BreaksIn(Outside stretch)
    {
        if (stretch.Held is { } held) return held.LineCount - 1;

        var (first, last, skip) = this.File.BlocksOf(stretch.From, stretch.Length);
        var (left, breaks, before) = (stretch.Length, 0L, '\0');

        for (var block = first; block < last && left > 0; block++)
        {
            var said = this.File.Said(block);
            var taken = (int)Math.Min(said.Chars - skip, left);

            if (skip == 0 && taken == said.Chars)
            {
                breaks += said.Breaks - (before == '\r' && said.First == '\n' ? 1 : 0);
                before = said.Last;
            }
            else
            {
                var text = this.File.Block(block).AsSpan(skip, taken);
                breaks += ContentRope.Counted(text).Breaks - (before == '\r' && text.Length > 0 && text[0] == '\n' ? 1 : 0);
                before = text.Length > 0 ? text[^1] : before;
            }

            (left, skip) = (left - taken, 0);
        }

        return breaks;
    }

    /// <summary>Where in <paramref name="stretch"/>, of the file's own characters, the character after its <paramref name="breaks"/>th line break stands.</summary>
    private long AfterBreak(Outside stretch, int breaks)
    {
        var (first, last, skip) = this.File.BlocksOf(stretch.From, stretch.Length);
        var (left, at, seen, before) = (stretch.Length, 0L, 0, '\0');

        for (var block = first; block < last && left > 0; block++)
        {
            var text = this.File.Block(block).AsSpan(skip, (int)Math.Min(this.File.Said(block).Chars - skip, left));

            for (var index = 0; index < text.Length; index++)
            {
                var character = text[index];

                // A line feed after a return ends the line the return began to end.
                if (character == '\n' && before == '\r')
                {
                    before = character;
                    continue;
                }

                before = character;
                if (character is not ('\n' or '\r')) continue;

                var next = index + 1 < text.Length ? text[index + 1] : '\0';
                if (++seen == breaks) return at + index + (character == '\r' && next == '\n' ? 2 : 1);
            }

            (left, skip, at) = (left - text.Length, 0, at + text.Length);
        }

        return stretch.Length;
    }

    /// <summary>
    /// <paramref name="at"/>, or the start of the next line after it where it is not where one starts — looked for no further than
    /// <see cref="LineReach"/> along; a line longer than that is stood in the middle of.
    /// </summary>
    private long LineStartFrom(List<Outside> all, long at, long total)
    {
        if (at <= 0 || at >= total) return Math.Clamp(at, 0, total);

        var reach = (int)Math.Min(LineReach, total - at + 1);
        var text = this.Joined(Slice(all, at - 1, at - 1 + reach)).ToString();

        // Just after a line feed, or a return no line feed follows: a line starts here.
        if (text[0] == '\n' || (text[0] == '\r' && (text.Length < 2 || text[1] != '\n'))) return at;

        for (var index = 1; index < text.Length; index++)
            if (text[index] == '\n' || (text[index] == '\r' && (index + 1 >= text.Length || text[index + 1] != '\n')))
                return Math.Min(total, at - 1 + index + 1);

        return at;
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
