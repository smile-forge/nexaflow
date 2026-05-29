using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Features.Tabular.Streaming;

/// <summary>
/// Streaming reader for the windowed-display path. Always scans from the file start: the
/// previous anchor-based fast-forward was unreliable because <see cref="StreamReader"/>
/// buffers ahead, so <c>BaseStream.Position</c> at any point is somewhere past where
/// <c>ReadLine</c> has logically consumed, by whatever bytes remain in the buffer. The
/// anchors it wrote were therefore mostly wrong and caused subsequent windows to label
/// rows incorrectly (which then cascaded into the EOF-clamp shrinking <c>KnownRowCount</c>
/// and the viewport going blank). Full-file scans cost O(rows) per scroll change but
/// always return the right rows.
/// </summary>
public sealed class RowWindowReader : IRowSource
{
    private readonly string    _path;
    private readonly Encoding  _encoding;
    private readonly int       _bomBytes;
    private readonly DataShape _shape;
    private          int?      _knownRowCount;
    private readonly object    _knownLock = new();

    public int? KnownRowCount
    {
        get { lock (_knownLock) return _knownRowCount; }
    }

    /// <summary>Called by <see cref="RowCounter"/> when the full count finishes.</summary>
    public void SetKnownRowCount(int count)
    {
        lock (_knownLock) _knownRowCount = count;
    }

    public RowWindowReader(string filePath, Encoding encoding, int bomBytes, DataShape shape)
    {
        _path     = filePath;
        _encoding = encoding;
        _bomBytes = bomBytes;
        _shape    = shape;
    }

    public Task<List<HydratedRow>> GetVisibleAsync(
        int fromRow, int maxVisible, RowFilter filter, CancellationToken ct)
    {
        return Task.Run(() => ScanForVisible(fromRow, maxVisible, filter, ct), CancellationToken.None);
    }

    /// <summary>
    /// Scans from the start of the data section forward, collecting up to
    /// <paramref name="maxVisible"/> filter-passing rows whose absolute index is
    /// ≥ <paramref name="fromRow"/>. Cancellation is cooperative — when triggered the
    /// scan returns whatever it has so far and the caller checks the token before
    /// committing the result.
    /// </summary>
    private List<HydratedRow> ScanForVisible(int fromRow, int maxVisible, RowFilter filter, CancellationToken ct)
    {
        var result = new List<HydratedRow>(maxVisible);
        try
        {
            using var fs = File.OpenRead(_path);
            fs.Position  = _bomBytes;
            using var sr = new StreamReader(fs, _encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 16384);

            for (int i = 0; i < _shape.LeadingCommentCount; i++)
                if (sr.ReadLine() is null) return result;
            if (_shape.RawShape.HasHeader)
                if (sr.ReadLine() is null) return result;

            int rowIdx = 0;
            string? line;
            int sinceCancelCheck = 0;
            while ((line = sr.ReadLine()) is not null)
            {
                if (++sinceCancelCheck >= 32)
                {
                    if (ct.IsCancellationRequested) return result;
                    sinceCancelCheck = 0;
                }
                if (rowIdx >= fromRow)
                {
                    var raw      = SmallFileLoader.TokenizeRow(line, _shape.RawShape);
                    var hydrated = _shape.HydrateRow(raw);
                    if (filter(hydrated))
                    {
                        result.Add(new HydratedRow(rowIdx, hydrated));
                        if (result.Count >= maxVisible) break;
                    }
                }
                rowIdx++;
            }
        }
        catch (IOException) { /* file disappeared mid-scan — return partial */ }
        return result;
    }

    public void Dispose() { /* no resources held between calls */ }
}
