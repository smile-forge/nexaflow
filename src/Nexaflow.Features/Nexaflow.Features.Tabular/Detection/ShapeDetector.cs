using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Features.Tabular.Detection;

/// <summary>
/// Scores candidate (separator, quote) combinations + an optional fixed-width fallback
/// from an 8-line sample (4 head + 4 tail). Returns shapes ranked by score.
/// </summary>
public static class ShapeDetector
{
    private static readonly char[]  SeparatorCandidates = { ',', '\t', '|', ';' };
    private static readonly char?[] QuoteCandidates     = { null, '"', '\'' };

    public static IReadOnlyList<CsvShape> DetectAll(FileSample sample)
    {
        // Empty file → single trivial shape.
        var allLines = sample.HeadLines.Concat(sample.TailLines).ToList();
        if (allLines.Count == 0)
        {
            return new[]
            {
                new CsvShape
                {
                    Separator = null, Quote = null, HasHeader = false,
                    ColumnTypes = new[] { CsvDataType.String }, FieldCount = 1,
                    Score = 1f, Label = "Single column",
                    Description = "Empty file"
                }
            };
        }

        var results = new List<CsvShape>();

        foreach (var sep in SeparatorCandidates)
        foreach (var quote in QuoteCandidates)
        {
            // A quoted interpretation tokenizes identically to the no-quote one unless the quote
            // char actually appears in the sample — so emitting it just creates a redundant
            // near-tie candidate that forces a needless disambiguation. Skip absent quotes.
            if (quote is char qc && !allLines.Any(l => l.IndexOf(qc) >= 0))
                continue;

            var shape = Score(allLines, sample.HeadLines.Count, sep, quote);
            if (shape is not null) results.Add(shape);
        }

        // Single-column fallback (no separator). Always present so even pathological files render.
        results.Add(SingleColumnShape(allLines, sample.HeadLines.Count));

        // Fixed-width fallback if best separator score is low.
        if (results.Count == 0 || results.Max(r => r.Score) < 0.5f)
        {
            var fixedW = FixedWidth(allLines, sample.HeadLines.Count);
            if (fixedW is not null) results.Add(fixedW);
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    private static CsvShape? Score(IReadOnlyList<string> lines, int headCount, char sep, char? quote)
    {
        var fieldCounts = lines.Select(l => CsvTokenizer.Split(l, sep, quote).Length).ToArray();

        // Reject shapes that produce only 1 column on more than half the rows (user invariant).
        int multiCol = fieldCounts.Count(c => c > 1);
        if (multiCol <= lines.Count / 2) return null;

        // Consistency score = fraction of rows whose field count matches the mode.
        var mode  = fieldCounts.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        if (mode < 2) return null;
        float consistency = fieldCounts.Count(c => c == mode) / (float)lines.Count;

        // Re-tokenize on the mode for type inference.
        var rows = lines.Select(l => CsvTokenizer.Split(l, sep, quote)).ToArray();

        // Header detection: for every column whose body has a *specific* (non-String) inferred
        // type, ask whether row[0]'s cell at that position parses as that type. Failure to
        // parse is strong evidence that row[0] is a header label, not data.
        //
        // We use *only the head rows* (excluding rows[0]) for this discrimination signal —
        // short files cause the tail reader to repeat the head's content, which would let
        // the header row re-appear in the inferred body type and wash out the signal.
        bool hasHeader = false;
        IReadOnlyList<CsvDataType> colTypes;
        // Column types used for the descriptor still combine head + tail for robustness.
        var modeBody = rows.Skip(1).Where(r => r.Length == mode).ToArray();
        colTypes = modeBody.Length > 0
            ? InferColumnTypes(modeBody, mode)
            : InferColumnTypes(rows.Where(r => r.Length == mode).ToArray(), mode);

        var headBody = new List<string[]>();
        for (int i = 1; i < headCount && i < rows.Length; i++)
            if (rows[i].Length == mode) headBody.Add(rows[i]);
        if (headBody.Count > 0)
        {
            var headBodyTypes = InferColumnTypes(headBody, mode);
            int signal = 0, considered = 0;
            int compareCols = System.Math.Min(mode, rows[0].Length);
            for (int col = 0; col < compareCols; col++)
            {
                if (headBodyTypes[col] == CsvDataType.String) continue;
                considered++;
                var headRaw   = rows[0][col];
                var headClass = ColumnTypeInferrer.ClassifyCell(headRaw);
                if (!FitsType(headClass, headBodyTypes[col])) signal++;
            }
            hasHeader = considered > 0 && (float)signal / considered >= 0.5f;
        }

        // Bonus for quotes when quote appears anywhere; light penalty otherwise.
        bool quotesObserved = quote is char q && lines.Any(l => l.IndexOf(q) >= 0);
        float quoteBonus    = quote is null ? 0.05f : (quotesObserved ? 0.10f : -0.10f);

        // Slight bias toward conventional CSV separators.
        float sepBonus = sep switch { ',' => 0.03f, '\t' => 0.02f, _ => 0f };

        // Field-count richness: a separator that produces 26 columns at 87 % consistency
        // beats one that produces 2 columns at 100 % consistency — humans pick the richer
        // split for things like semicolon CSVs containing European decimals (e.g. "2,60").
        float richness = System.Math.Min(mode, 30) / 30f;

        // Weighted combination: consistency dominates, richness breaks near-ties.
        var score = System.Math.Clamp(
            consistency * 0.60f + richness * 0.30f + quoteBonus + sepBonus,
            0f, 1f);
        var label = $"{NameOfSep(sep)} separated, {(quote is null ? "no quotes" : $"{quote}-quoted")}, {(hasHeader ? "with header" : "no header")}";
        var desc  = $"{mode} columns; consistency {consistency:P0}";

        return new CsvShape
        {
            Separator   = sep,
            Quote       = quote,
            HasHeader   = hasHeader,
            ColumnTypes = colTypes,
            FieldCount  = mode,
            Score       = score,
            Consistency = consistency,
            Label       = label,
            Description = desc,
        };
    }

    /// <summary>
    /// Decides whether the ranked <paramref name="shapes"/> have a confident winner that does NOT
    /// need a model disambiguation call. Confident when: a clear score lead, a strictly more
    /// internally consistent top parse, the runner-up is the same structural parse, or we're only
    /// in low-confidence fallback territory. Only genuinely close, structurally-different
    /// multi-column candidates return false (→ ask the model).
    /// </summary>
    public static bool TryPickConfident(IReadOnlyList<CsvShape> shapes, out CsvShape pick)
    {
        pick = shapes[0];
        if (shapes.Count == 1) return true;

        var top    = shapes[0];
        var second = shapes[1];

        if (top.Score - second.Score > 0.2f)                       return true;  // clear score lead
        if (top.Consistency - second.Consistency > 0.05f)          return true;  // more consistent parse
        if (top.Separator == second.Separator
            && top.FieldCount == second.FieldCount)                return true;  // same structure
        if (top.Score < 0.5f)                                      return true;  // fallback territory

        return false;   // genuinely ambiguous — different structures with comparable scores
    }

    /// <summary>True iff <paramref name="actual"/> is a valid concrete fit for the column type
    /// <paramref name="bodyType"/> — used to ask "would this cell value be valid data in a
    /// column the detector inferred as <paramref name="bodyType"/>?".</summary>
    private static bool FitsType(CsvDataType actual, CsvDataType bodyType) =>
        actual == bodyType ||
        (bodyType == CsvDataType.Decimal  && actual is CsvDataType.Integer or CsvDataType.Currency) ||
        (bodyType == CsvDataType.Currency && actual is CsvDataType.Integer or CsvDataType.Decimal) ||
        (bodyType == CsvDataType.DateTime && actual is CsvDataType.Date or CsvDataType.Time);

    private static IReadOnlyList<CsvDataType> InferColumnTypes(IReadOnlyList<string[]> bodyRows, int columnCount)
    {
        var types = new CsvDataType[columnCount];
        for (int c = 0; c < columnCount; c++)
        {
            types[c] = ColumnTypeInferrer.AggregateColumn(bodyRows.Select(r => c < r.Length ? r[c] : null));
        }
        return types;
    }

    private static CsvShape SingleColumnShape(IReadOnlyList<string> lines, int headCount)
    {
        var body = lines.Skip(headCount > 0 ? 1 : 0).ToArray();
        var colType = ColumnTypeInferrer.AggregateColumn(body);
        return new CsvShape
        {
            Separator   = null,
            Quote       = null,
            HasHeader   = false,
            ColumnTypes = new[] { colType },
            FieldCount  = 1,
            Score       = 0.30f,
            Label       = "Single column",
            Description = "No separator detected",
        };
    }

    private static CsvShape? FixedWidth(IReadOnlyList<string> lines, int headCount)
    {
        if (lines.Count < 2) return null;
        int width = lines.Max(l => l.Length);
        if (width < 4) return null;

        // A column boundary is a character position where every row has a space.
        var allSpace = new bool[width];
        for (int p = 0; p < width; p++) allSpace[p] = true;
        foreach (var l in lines)
            for (int p = 0; p < System.Math.Min(width, l.Length); p++)
                if (l[p] != ' ') allSpace[p] = false;

        var boundaries = new List<int>();
        boundaries.Add(0);
        bool inSpace = false;
        for (int p = 0; p < width; p++)
        {
            if (allSpace[p] && !inSpace) inSpace = true;
            else if (!allSpace[p] && inSpace) { boundaries.Add(p); inSpace = false; }
        }
        if (boundaries.Count < 2) return null;

        // Slice each line.
        int columns = boundaries.Count;
        var rows = new List<string[]>();
        foreach (var l in lines)
        {
            var arr = new string[columns];
            for (int c = 0; c < columns; c++)
            {
                int start = boundaries[c];
                int end   = c + 1 < columns ? boundaries[c + 1] : width;
                if (start >= l.Length) { arr[c] = string.Empty; continue; }
                if (end > l.Length) end = l.Length;
                arr[c] = l[start..end].Trim();
            }
            rows.Add(arr);
        }

        var body = rows.Skip(headCount > 0 ? 1 : 0).ToArray();
        var types = new CsvDataType[columns];
        for (int c = 0; c < columns; c++)
            types[c] = ColumnTypeInferrer.AggregateColumn(body.Select(r => (string?)r[c]));

        return new CsvShape
        {
            Separator            = null,
            Quote                = null,
            HasHeader            = false, // header detection is unreliable for fixed-width — leave off
            ColumnTypes          = types,
            FieldCount           = columns,
            Score                = 0.45f, // surfaced only when no separator scored well
            Label                = $"Fixed-width ({columns} columns)",
            Description          = $"Boundaries at {string.Join(",", boundaries)}",
            FixedWidthBoundaries = boundaries,
        };
    }

    private static string NameOfSep(char sep) => sep switch
    {
        ',' => "Comma",
        '\t' => "Tab",
        '|' => "Pipe",
        ';' => "Semicolon",
        _ => $"'{sep}'"
    };
}
