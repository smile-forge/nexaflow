namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// Sheets printed on both sides and cut into leaves, put into reading order. Each sheet's front is followed by its back —
/// the next sheet — turned over its long edge, so the part of the back behind a part of the front lies mirrored left to
/// right: a sheet cut two by two whose front holds pages 1, 3, 5 and 7 holds 4, 2, 8 and 6 on its back. Each leaf is read
/// front then back, the leaves in the order they lie on the front.
/// </summary>
internal static class PdfCutLeaves
{
    /// <summary>
    /// <paramref name="sheets"/> — each sheet's parts as they lie, left to right and top to bottom, and how many columns it
    /// is cut into; fronts and backs taking turns — in the order they are read. A last front with no back after it is read as
    /// it lies.
    /// </summary>
    public static IReadOnlyList<T> Order<T>(IReadOnlyList<(IReadOnlyList<T> Parts, int Columns)> sheets)
    {
        var read = new List<T>();
        for (var at = 0; at < sheets.Count; at += 2)
        {
            var (front, columns) = sheets[at];
            if (at + 1 >= sheets.Count)
            {
                read.AddRange(front);
                break;
            }

            var back = sheets[at + 1].Parts;
            for (var part = 0; part < front.Count; part++)
            {
                read.Add(front[part]);
                var behind = part / columns * columns + (columns - 1 - part % columns);
                if (behind < back.Count) read.Add(back[behind]);
            }
        }

        return read;
    }
}
