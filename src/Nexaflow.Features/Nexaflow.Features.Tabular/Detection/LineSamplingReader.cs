using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Nexaflow.Features.Tabular.Detection;

public sealed record FileSample(
    IReadOnlyList<string> HeadLines,
    IReadOnlyList<string> TailLines,
    LineEndingKind LineEnding,
    Encoding Encoding,
    bool HadBom,
    int BomByteCount,
    long FileLength,
    int AverageLineByteSize,
    IReadOnlyList<string> LeadingCommentLines);

/// <summary>
/// Reads the first N and last N lines of a file efficiently via byte-level seek.
/// Used by the orchestrator to drive shape detection without scanning the whole file.
/// </summary>
public static class LineSamplingReader
{
    public static FileSample Read(string filePath, int linesEachEnd = 4)
    {
        using var fs = File.OpenRead(filePath);
        var probe = EncodingDetector.Detect(fs);
        var enc   = probe.Encoding;

        // Read enough head lines to cover leading comments + the desired non-comment head.
        // Comment lines start with '#' (CSV convention). If the tail of the file also
        // starts a line with '#', treat '#' as data — otherwise strip the leading run.
        const int HeadReadCap = 32;
        var headAll = ReadHead(fs, enc, probe.BomByteCount, HeadReadCap, out var lineEnding);
        var tail = fs.Length > probe.BomByteCount + 4
            ? ReadTail(fs, enc, probe.BomByteCount, linesEachEnd)
            : new List<string>();

        List<string> comments = new();
        List<string> head     = headAll;
        bool headStartsHash = headAll.Count > 0 && headAll[0].StartsWith("#");
        bool tailHasHash    = tail.Count > 0 && tail[^1].StartsWith("#");
        if (headStartsHash && !tailHasHash)
        {
            int i = 0;
            while (i < headAll.Count && headAll[i].StartsWith("#"))
            {
                comments.Add(headAll[i]);
                i++;
            }
            head = headAll.Skip(i).Take(linesEachEnd).ToList();
        }
        else
        {
            head = headAll.Take(linesEachEnd).ToList();
        }

        int totalChars = 0;
        foreach (var l in head) totalChars += l.Length + 1;
        foreach (var l in tail) totalChars += l.Length + 1;
        int sampleLines = head.Count + tail.Count;
        int avgChars    = sampleLines > 0 ? totalChars / sampleLines : 64;
        int bytesPerChar = enc is UnicodeEncoding ? 2 : 1;
        int avgBytes     = System.Math.Max(8, avgChars * bytesPerChar);

        return new FileSample(head, tail, lineEnding, enc, probe.HadBom, probe.BomByteCount,
                              fs.Length, avgBytes, comments);
    }

    private static List<string> ReadHead(FileStream fs, Encoding enc, int bomBytes, int wanted, out LineEndingKind lineEnding)
    {
        fs.Position = bomBytes;
        var lines       = new List<string>(wanted);
        var sr          = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        lineEnding      = LineEndingKind.Lf;
        bool sniffed    = false;

        if (!sniffed)
        {
            // Peek one byte forward to detect line ending: scan first few hundred bytes for \r\n or \n or \r
            long save = fs.Position;
            try
            {
                int peekLen = (int)System.Math.Min(2048, fs.Length - fs.Position);
                if (peekLen > 0)
                {
                    var peek = new byte[peekLen];
                    fs.ReadExactly(peek, 0, peekLen);
                    for (int i = 0; i < peekLen; i++)
                    {
                        if (peek[i] == (byte)'\r')
                        {
                            lineEnding = (i + 1 < peekLen && peek[i + 1] == (byte)'\n') ? LineEndingKind.Crlf : LineEndingKind.Cr;
                            sniffed = true; break;
                        }
                        if (peek[i] == (byte)'\n') { lineEnding = LineEndingKind.Lf; sniffed = true; break; }
                    }
                }
            }
            finally
            {
                fs.Position = save;
                sr.DiscardBufferedData();
            }
        }

        while (lines.Count < wanted && !sr.EndOfStream)
        {
            var line = sr.ReadLine();
            if (line is null) break;
            lines.Add(line);
        }
        return lines;
    }

    private static List<string> ReadTail(FileStream fs, Encoding enc, int bomBytes, int wanted)
    {
        // Read progressively larger tail chunks until we have wanted+1 line breaks.
        long fileLen = fs.Length;
        long start   = System.Math.Max(bomBytes, fileLen - 8192);
        int  attempts = 0;

        while (attempts++ < 4)
        {
            int len = (int)(fileLen - start);
            fs.Position = start;
            var buf = new byte[len];
            fs.ReadExactly(buf, 0, len);

            // Count line endings in buf
            int newlineCount = 0;
            for (int i = 0; i < len; i++) if (buf[i] == (byte)'\n') newlineCount++;
            if (newlineCount > wanted || start <= bomBytes)
            {
                // Decode and split — for UTF-16 we may have started mid-codeunit; in that case
                // re-align by trimming one byte.
                int decodeStart = 0;
                if (enc is UnicodeEncoding && (start - bomBytes) % 2 != 0 && len > 0) decodeStart = 1;
                if (enc is UTF8Encoding)
                {
                    // Advance past any UTF-8 continuation bytes at the head of the buffer.
                    while (decodeStart < len && (buf[decodeStart] & 0xC0) == 0x80) decodeStart++;
                }

                var text  = enc.GetString(buf, decodeStart, len - decodeStart);
                var lines = text.Split('\n');
                var clean = new List<string>(lines.Length);
                foreach (var raw in lines)
                {
                    var s = raw.EndsWith('\r') ? raw[..^1] : raw;
                    clean.Add(s);
                }
                // Drop the trailing empty fragment introduced by a final \n BEFORE picking the last N.
                if (clean.Count > 0 && string.IsNullOrEmpty(clean[^1])) clean.RemoveAt(clean.Count - 1);
                // When we seeked into the middle of a line, the first fragment is partial — drop one.
                int dropLeading = (start > bomBytes && clean.Count > wanted) ? 1 : 0;
                int from        = System.Math.Max(dropLeading, clean.Count - wanted);
                var trimmed     = new List<string>();
                for (int i = from; i < clean.Count; i++) trimmed.Add(clean[i]);
                return trimmed;
            }

            // Need more — double the tail span.
            start = System.Math.Max(bomBytes, start - (fileLen - start));
        }

        return new List<string>();
    }
}
