using System;
using System.Collections.Generic;
using System.Text;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// A PDF417 reader without the camera: finds each row's cluster, looks its characters back up in the
/// table, checks every Reed–Solomon syndrome and decodes the compaction.
///
/// <para>
/// Where it can it takes a different route to the same answer as the encoder. Parity is checked by
/// evaluating the received polynomial at each root rather than by re-dividing — two copies of one
/// mistake agree with each other — and the row indicators are read back and compared against the shape
/// they claim, which is the check a real scanner makes before it trusts a strip of bars.
/// </para>
/// </summary>
internal static class Pdf417TestDecoder
{
    internal sealed record Decoded(string Text, int[] Codewords, int Rows, int Columns, int ErrorLevel);

    /// <summary>Pattern → codeword, per cluster — the table read the other way.</summary>
    private static readonly Dictionary<int, int>[] Reverse = BuildReverse();

    private static Dictionary<int, int>[] BuildReverse()
    {
        var maps = new Dictionary<int, int>[3];
        for (int i = 0; i < 3; i++)
        {
            maps[i] = new Dictionary<int, int>(Pdf417Codewords.Count);
            for (int cw = 0; cw < Pdf417Codewords.Count; cw++)
                maps[i][Pdf417Codewords.Pattern(Pdf417Codewords.Clusters[i], cw)] = cw;
        }
        return maps;
    }

    internal static Decoded Decode(IModuleMatrix m)
    {
        int rows = m.Height;
        var words = new List<int>();
        int columns = -1, level = -1, rowsDiv3 = -1, rowsMod3 = -1;

        for (int r = 0; r < rows; r++)
        {
            int clusterIndex = r % 3;
            int cluster = Pdf417Codewords.Clusters[clusterIndex];

            if (Read(m, r, 0, 17) != Pdf417Codewords.StartPattern)
                throw new InvalidOperationException($"row {r} does not begin with the start pattern");

            // How many 17-module characters sit between the start pattern and the stop pattern.
            int chars = (m.Width - 17 - 18) / 17;
            var cws = new int[chars];
            for (int i = 0; i < chars; i++)
            {
                int pattern = Read(m, r, 17 + i * 17, 17);
                if (!Reverse[clusterIndex].TryGetValue(pattern, out cws[i]))
                    throw new InvalidOperationException($"row {r}, character {i}: 0x{pattern:X5} is not in cluster {cluster}");
            }

            int k = r / 3;
            int left = cws[0] - 30 * k, right = cws[^1] - 30 * k;

            switch (cluster)
            {
                case 0:
                    // Only the whole threes of the row count; the remainder is cluster 3's to carry.
                    Agree(ref rowsDiv3, left, r, "row count");
                    Agree(ref columns, right + 1, r, "column count");
                    break;
                case 3:
                    Agree(ref level, left / 3, r, "error level");
                    Agree(ref rowsMod3, left % 3, r, "row count remainder");
                    Agree(ref rowsDiv3, right, r, "row count");
                    break;
                default:
                    Agree(ref columns, left + 1, r, "column count");
                    Agree(ref level, right / 3, r, "error level");
                    break;
            }

            for (int i = 1; i < chars - 1; i++) words.Add(cws[i]);
        }

        int claimedRows = rowsDiv3 * 3 + Math.Max(rowsMod3, 0) + 1;
        if (claimedRows != rows)
            throw new InvalidOperationException($"the indicators claim {claimedRows} rows; the symbol has {rows}");

        CheckParity(words, 1 << (level + 1));

        int total = words[0];
        return new Decoded(ReadText(words.GetRange(1, total - 1)), words.ToArray(), rows, columns, level);
    }

    private static void Agree(ref int slot, int value, int row, string what)
    {
        if (slot >= 0 && slot != value)
            throw new InvalidOperationException($"row {row}: indicators disagree about the {what} ({slot} then {value})");
        slot = value;
    }

    private static int Read(IModuleMatrix m, int row, int at, int count)
    {
        int value = 0;
        for (int i = 0; i < count; i++) value = (value << 1) | (m[at + i, row] ? 1 : 0);
        return value;
    }

    /// <summary>Every syndrome vanishes across data and parity together when the parity is right.</summary>
    private static void CheckParity(List<int> words, int parity)
    {
        var field = GaloisField.Pdf417;

        for (int root = 1; root <= parity; root++)
        {
            int x = field.Exp(root), y = 0;
            foreach (int c in words) y = field.Add(field.Multiply(y, x), c);

            if (y != 0) throw new InvalidOperationException($"syndrome at root g^{root} is {y}, not 0");
        }
    }

    // ── Compaction ─────────────────────────────────────────────────────────

    private const string MixedSet = "0123456789&\r\t,:#-.$/+%*=^";
    private const string PunctSet = ";<>@[\\]_`~!\r\t,:\n-.$/\"|*()?{}'";

    private static string ReadText(List<int> words)
    {
        var text = new StringBuilder();
        int at = 0;

        while (at < words.Count)
        {
            int word = words[at];

            if (word == 901 || word == 924) { at = ReadBytes(words, at + 1, word == 924, text); continue; }
            if (word == 902) { at = ReadNumeric(words, at + 1, text); continue; }
            if (word == 900) { at = ReadTextRun(words, at + 1, text); continue; }

            at = ReadTextRun(words, at, text);
        }

        return text.ToString();
    }

    private static int ReadTextRun(List<int> words, int at, StringBuilder text)
    {
        int mode = 0, latched = 0;   // 0 upper, 1 lower, 2 mixed, 3 punct

        for (; at < words.Count; at++)
        {
            if (words[at] >= 900) return at;

            foreach (int v in new[] { words[at] / 30, words[at] % 30 })
            {
                switch (mode)
                {
                    case 0:
                        if (v < 26) text.Append((char)('A' + v));
                        else if (v == 26) text.Append(' ');
                        else if (v == 27) mode = latched = 1;
                        else if (v == 28) mode = latched = 2;
                        else { mode = 3; continue; }
                        break;
                    case 1:
                        if (v < 26) text.Append((char)('a' + v));
                        else if (v == 26) text.Append(' ');
                        else if (v == 27) { mode = 0; continue; }      // shift for one character
                        else if (v == 28) mode = latched = 2;
                        else { mode = 3; continue; }
                        break;
                    case 2:
                        if (v < 25) text.Append(MixedSet[v]);
                        else if (v == 25) { mode = 3; continue; }
                        else if (v == 26) text.Append(' ');
                        else if (v == 27) mode = latched = 1;
                        else if (v == 28) mode = latched = 0;
                        else { mode = 3; continue; }
                        break;
                    default:
                        if (v < 29) text.Append(PunctSet[v]);
                        else { mode = latched = 0; continue; }
                        break;
                }

                // A shift lasts one character; a latch changed `latched` above and this restores nothing.
                if (mode == 3 && latched != 3) mode = latched;
                else if (mode == 0 && latched == 1) mode = latched;
            }
        }

        return at;
    }

    private static int ReadBytes(List<int> words, int at, bool wholeGroups, StringBuilder text)
    {
        var bytes = new List<byte>();

        while (at < words.Count && words[at] < 900)
        {
            int left = 0;
            while (at + left < words.Count && words[at + left] < 900) left++;

            if (left >= 5 && (wholeGroups || left % 5 == 0))
            {
                long chunk = 0;
                for (int i = 0; i < 5; i++) chunk = chunk * 900 + words[at + i];
                for (int i = 5; i >= 0; i--) bytes.Insert(bytes.Count - (5 - i), 0);
                bytes.RemoveRange(bytes.Count - 6, 6);
                var six = new byte[6];
                for (int i = 5; i >= 0; i--) { six[i] = (byte)(chunk & 0xFF); chunk >>= 8; }
                bytes.AddRange(six);
                at += 5;
            }
            else
            {
                bytes.Add((byte)words[at]);
                at++;
            }
        }

        text.Append(Encoding.UTF8.GetString(bytes.ToArray()));
        return at;
    }

    private static int ReadNumeric(List<int> words, int at, StringBuilder text)
    {
        var group = new List<int>();

        while (at < words.Count && words[at] < 900)
        {
            group.Add(words[at]);
            at++;

            if (group.Count == 15 || at >= words.Count || words[at] >= 900)
            {
                var value = System.Numerics.BigInteger.Zero;
                foreach (int w in group) value = value * 900 + w;
                text.Append(value.ToString()[1..]);        // the leading 1 the encoder put on
                group.Clear();
            }
        }

        return at;
    }
}
