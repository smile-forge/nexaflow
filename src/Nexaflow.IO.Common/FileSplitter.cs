using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.IO.Common;

public enum SplitMode { BySize, ByLineCount, ByRegex }

public sealed record SplitOptions
{
    public SplitMode Mode { get; init; }

    /// <summary>BySize: target max bytes per part. A single oversized line still goes whole into its own part.</summary>
    public long MaxBytesPerPart { get; init; }

    /// <summary>ByLineCount: lines per part.</summary>
    public int LinesPerPart { get; init; }

    /// <summary>ByRegex: a new part starts at each line matching this pattern.</summary>
    public string? BoundaryPattern { get; init; }

    /// <summary>Defaults to the source file's directory.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Used only to test ByRegex boundaries; the bytes written are always the source's. Defaults to a sniff.</summary>
    public Encoding? Encoding { get; init; }
}

public sealed record SplitResult(IReadOnlyList<string> OutputFiles, long TotalBytes, int TotalLines);

/// <summary>
/// Streams a (possibly huge) text file into smaller sibling files, cutting only at <c>\n</c> boundaries so
/// the concatenation of the parts is byte-identical to the source. Line bytes are written raw; for ByRegex
/// a line is decoded only to test the boundary. Intended for UTF-8/ASCII text and logs.
/// </summary>
public static class FileSplitter
{
    public static async Task<SplitResult> SplitAsync(string sourcePath, SplitOptions options, CancellationToken ct = default)
    {
        Validate(options);

        var encoding = options.Encoding ?? SniffEncoding(sourcePath);
        var outDir   = options.OutputDirectory ?? Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        Directory.CreateDirectory(outDir);
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var ext      = Path.GetExtension(sourcePath);

        var boundary = options.Mode == SplitMode.ByRegex
            ? new Regex(options.BoundaryPattern!, RegexOptions.Compiled)
            : null;

        var outputs = new List<string>();
        long totalBytes = 0;
        var totalLines  = 0;

        FileStream? part = null;
        var partIndex   = 0;
        long partBytes  = 0;
        var partLines   = 0;

        try
        {
            await using var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
            var readBuf = new byte[1 << 16];
            using var line = new MemoryStream(256); // accumulates the current line's bytes, including its \n
            int read;

            // Local function captures the part/counter locals by reference.
            async Task FlushLineAsync()
            {
                var len = (int)line.Length;
                if (len == 0) return;
                var buf = line.GetBuffer();

                var startNew = part is null;
                if (boundary is not null)
                {
                    if (partLines > 0 && boundary.IsMatch(encoding.GetString(buf, 0, len))) startNew = true;
                }
                else if (options.Mode == SplitMode.BySize)
                {
                    if (partLines > 0 && partBytes + len > options.MaxBytesPerPart) startNew = true;
                }
                else // ByLineCount
                {
                    if (partLines >= options.LinesPerPart) startNew = true;
                }

                if (startNew)
                {
                    part?.Dispose();
                    partIndex++;
                    var full = Path.Combine(outDir, $"{baseName}.part{partIndex:D3}{ext}");
                    part = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                    outputs.Add(full);
                    partBytes = 0;
                    partLines = 0;
                }

                await part!.WriteAsync(buf.AsMemory(0, len), ct);
                partBytes += len; partLines++;
                totalBytes += len; totalLines++;
                line.SetLength(0);
            }

            while ((read = await src.ReadAsync(readBuf.AsMemory(0, readBuf.Length), ct)) > 0)
            {
                // Index-based (not Span) because a Span can't live across the await in FlushLineAsync.
                var pos = 0;
                while (pos < read)
                {
                    var nl = Array.IndexOf(readBuf, (byte)'\n', pos, read - pos);
                    if (nl < 0)
                    {
                        line.Write(readBuf, pos, read - pos);
                        break;
                    }
                    line.Write(readBuf, pos, nl - pos + 1);
                    await FlushLineAsync();
                    pos = nl + 1;
                }
            }
            await FlushLineAsync(); // trailing line with no final newline
        }
        finally
        {
            part?.Dispose();
        }

        return new SplitResult(outputs, totalBytes, totalLines);
    }

    private static void Validate(SplitOptions o)
    {
        switch (o.Mode)
        {
            case SplitMode.BySize when o.MaxBytesPerPart <= 0:
                throw new ArgumentException("MaxBytesPerPart must be positive.", nameof(o));
            case SplitMode.ByLineCount when o.LinesPerPart <= 0:
                throw new ArgumentException("LinesPerPart must be positive.", nameof(o));
            case SplitMode.ByRegex when string.IsNullOrEmpty(o.BoundaryPattern):
                throw new ArgumentException("BoundaryPattern is required.", nameof(o));
        }
    }

    private static Encoding SniffEncoding(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return EncodingDetector.Detect(fs).Encoding;
    }
}
