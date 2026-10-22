namespace Nexaflow.IO.Documents.Processing;

/// <summary>How text too long to hand a model at once is cut, and how pieces are gathered into what it can be handed.</summary>
public static class DocumentCuts
{
    /// <summary>What ends a sentence, where a cut is best made.</summary>
    private static readonly string[] Ends = [". ", "! ", "? ", "。", "！", "？", ".\n", "!\n", "?\n", "\n"];

    /// <summary>
    /// <paramref name="text"/> cut into pieces of at most <paramref name="room"/> characters: after the last sentence ending
    /// inside the room, failing that after its last space, failing that at the room's edge. Nothing is lost or added — the
    /// pieces joined are the text.
    /// </summary>
    public static IReadOnlyList<string> Cut(string text, int room)
    {
        room = Math.Max(1, room);
        var pieces = new List<string>();
        var from = 0;

        while (text.Length - from > room)
        {
            var span = text.AsSpan(from, room);
            var at = -1;

            foreach (var end in Ends)
                if (span.LastIndexOf(end, StringComparison.Ordinal) is var found and > 0 && found + end.Length > at)
                    at = found + end.Length;

            if (at <= 0) at = span.LastIndexOf(' ') is var space and > 0 ? space + 1 : room;

            pieces.Add(text.Substring(from, at));
            from += at;
        }

        pieces.Add(text[from..]);
        return pieces;
    }

    /// <summary>
    /// <paramref name="pieces"/> gathered, in order and a blank line apart, into stretches of at most <paramref name="room"/>
    /// characters; a piece longer than that is cut (<see cref="Cut"/>), each of its parts a stretch of its own.
    /// </summary>
    public static IReadOnlyList<string> Pack(IEnumerable<string> pieces, int room)
    {
        var stretches = new List<string>();
        var stretch = new System.Text.StringBuilder();

        foreach (var piece in pieces)
        {
            if (piece.Length > room)
            {
                Close();
                stretches.AddRange(Cut(piece, room));
                continue;
            }

            if (stretch.Length > 0 && stretch.Length + 2 + piece.Length > room) Close();
            if (stretch.Length > 0) stretch.Append("\n\n");
            stretch.Append(piece);
        }

        Close();
        return stretches;

        void Close()
        {
            if (stretch.Length == 0) return;

            stretches.Add(stretch.ToString());
            stretch.Clear();
        }
    }

    /// <summary>
    /// Which of <paramref name="count"/> things to take when only <paramref name="most"/> may be: all of them where they are
    /// that few, otherwise the first, the last, and the rest spread evenly between — so what is read of a long document is
    /// from all through it.
    /// </summary>
    public static IReadOnlyList<int> Spread(int count, int most)
    {
        if (count <= most) return [.. Enumerable.Range(0, count)];
        if (most <= 1) return count > 0 ? [0] : [];

        return [.. Enumerable.Range(0, most).Select(step => (int)Math.Round(step * (count - 1) / (double)(most - 1))).Distinct()];
    }
}
