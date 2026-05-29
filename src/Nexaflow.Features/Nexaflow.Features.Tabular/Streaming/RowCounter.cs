using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Tabular.Detection;

namespace Nexaflow.Features.Tabular.Streaming;

/// <summary>
/// Background row counter. Uses <see cref="StreamReader.ReadLine"/> so the count matches
/// the streaming reader's notion of "a row" exactly — same handling of CR/LF/CRLF, same
/// handling of a missing trailing newline. Naive LF-byte counting misses CR-only line
/// endings (Classic Mac / mixed-format files), which silently undercounts on real-world
/// data; the readers and the scrollbar then disagree about where the file ends.
/// </summary>
public static class RowCounter
{
    public static Task<int> CountAsync(
        string path, int bomBytes, CsvShape shape,
        Action<int> progress, CancellationToken ct,
        int leadingCommentLines = 0,
        Encoding? encoding = null)
    {
        return Task.Run(() =>
        {
            encoding ??= Encoding.UTF8;
            using var fs = File.OpenRead(path);
            fs.Position = bomBytes;
            using var sr = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 65536);

            int rows = 0;
            long lastReport = Environment.TickCount64;
            string? line;
            while ((line = sr.ReadLine()) is not null)
            {
                ct.ThrowIfCancellationRequested();
                rows++;
                var now = Environment.TickCount64;
                if (now - lastReport > 250)
                {
                    progress(rows);
                    lastReport = now;
                }
            }

            rows -= leadingCommentLines;
            if (shape.HasHeader && rows > 0) rows--;
            if (rows < 0) rows = 0;
            progress(rows);
            return rows;
        }, ct);
    }
}
