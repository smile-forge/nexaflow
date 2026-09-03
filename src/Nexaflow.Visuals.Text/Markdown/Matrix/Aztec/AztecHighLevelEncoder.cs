using System;
using System.Collections.Generic;
using System.Globalization;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Turns a message into Aztec's bit stream: the codes, latches, shifts and byte runs that spell it out
/// in the fewest bits.
///
/// <para>
/// Choosing them is a shortest-path problem, not a scan. Every character can usually be reached several
/// ways — latch to the set it lives in, shift into it for one character, or drop the whole run into a
/// byte shift — and the cheapest choice for one character depends on what follows it. A greedy encoder
/// gets this wrong in a way nobody notices, because the symbol it produces still scans; it is simply
/// bigger than it needed to be, sometimes by a whole layer. So the encoder is a small dynamic program
/// over (position, set in force), and the answer it finds is optimal rather than plausible.
/// </para>
/// <para>
/// The one deliberate approximation is byte runs: a run of length <c>k</c> is offered as a single move
/// for every <c>k</c>, which is quadratic in the message length. Aztec's own ceiling is under two
/// thousand bytes, so the worst case is small and the alternative — folding the run length into the
/// state — costs more than it saves.
/// </para>
/// </summary>
internal static class AztecHighLevelEncoder
{
    /// <summary>A message begins in Upper; the standard fixes that so a reader has somewhere to start.</summary>
    private const AztecCharacterSet Initial = AztecCharacterSet.Upper;

    private const int Unreachable = int.MaxValue / 4;

    /// <summary>
    /// The bit stream for <paramref name="bytes"/>, preceded by whatever <paramref name="options"/>
    /// says the message is flagged as.
    /// </summary>
    internal static List<bool> Encode(ReadOnlySpan<byte> bytes, AztecOptions options)
    {
        var bits = new List<bool>();
        WritePrefix(bits, options);

        var best = Search(bytes);
        Write(bits, best, bytes);
        return bits;
    }

    // ── FLG(n): the GS1 and ECI flags ──────────────────────────────────────

    /// <summary>
    /// FLG(n) is Punct code zero followed by three bits. FLG(0) is FNC1 — the flag that says the
    /// message is a GS1 element string. FLG(1) to FLG(6) introduce an ECI number, given as that many
    /// digits in the Digit set, which says what character set the bytes are in.
    ///
    /// <para>
    /// Both are written from Upper through a punctuation shift, so the set in force afterwards is still
    /// Upper and the search below can start where it always starts.
    /// </para>
    /// </summary>
    private static void WritePrefix(List<bool> bits, AztecOptions options)
    {
        if (!options.Gs1 && options.Eci is null) return;

        var shift = AztecCharacterSets.Shift(Initial, AztecCharacterSet.Punct)!.Value;
        Append(bits, shift.Value, shift.Width);
        Append(bits, AztecCharacterSets.Flg, AztecCharacterSets.Width(AztecCharacterSet.Punct));

        if (options.Eci is not int eci)
        {
            Append(bits, 0, 3);                 // FLG(0) — FNC1
            return;
        }

        string digits = eci.ToString(CultureInfo.InvariantCulture);
        Append(bits, digits.Length, 3);
        foreach (char digit in digits)
            Append(bits, AztecCharacterSets.Single(AztecCharacterSet.Digit, digit)!.Value,
                   AztecCharacterSets.Width(AztecCharacterSet.Digit));
    }

    // ── The search ─────────────────────────────────────────────────────────

    /// <summary>
    /// One thing to write. A chain of these, newest first, is a candidate encoding — a linked list
    /// rather than an array so that the states sharing a prefix share its storage too.
    /// </summary>
    private abstract class Emission(Emission? previous)
    {
        internal Emission? Previous { get; } = previous;
    }

    /// <summary>A code in a known width — a character, a latch, or a shift.</summary>
    private sealed class CodeEmission(Emission? previous, int value, int width) : Emission(previous)
    {
        internal int Value { get; } = value;
        internal int Width { get; } = width;
    }

    /// <summary>A byte shift and the run of raw bytes it carries.</summary>
    private sealed class ByteRun(Emission? previous, int start, int count) : Emission(previous)
    {
        internal int Start { get; } = start;
        internal int Count { get; } = count;
    }

    /// <summary>
    /// The cheapest encoding of the whole message, as a chain of emissions ending at its last one.
    /// </summary>
    private static Emission? Search(ReadOnlySpan<byte> bytes)
    {
        int n     = bytes.Length;
        int sets  = AztecCharacterSets.All.Length;
        var cost  = new int[n + 1, sets];
        var chain = new Emission?[n + 1, sets];

        for (int at = 0; at <= n; at++)
            for (int set = 0; set < sets; set++) cost[at, set] = Unreachable;

        cost[0, (int)Initial] = 0;

        for (int at = 0; at <= n; at++)
        {
            Latch(cost, chain, at);
            if (at == n) break;

            foreach (var set in AztecCharacterSets.All)
            {
                int from = cost[at, (int)set];
                if (from >= Unreachable) continue;

                Character(bytes, cost, chain, at, set, from);
                Shifted(bytes, cost, chain, at, set, from);
                Bytes(cost, chain, at, set, from, n);
            }
        }

        int cheapest = Unreachable;
        Emission? answer = null;
        foreach (var set in AztecCharacterSets.All)
            if (cost[n, (int)set] < cheapest)
            {
                cheapest = cost[n, (int)set];
                answer   = chain[n, (int)set];
            }

        return answer;
    }

    /// <summary>
    /// Latching costs bits and consumes no character, so it is applied across the states at one
    /// position rather than as a move between positions. The routes are already shortest paths, so a
    /// single pass computed out of place — never in place, which would let two latches compound — is
    /// the complete closure.
    /// </summary>
    private static void Latch(int[,] cost, Emission?[,] chain, int at)
    {
        var reached = new int[AztecCharacterSets.All.Length];
        var through = new Emission?[AztecCharacterSets.All.Length];

        foreach (var to in AztecCharacterSets.All)
        {
            reached[(int)to] = cost[at, (int)to];
            through[(int)to] = chain[at, (int)to];

            foreach (var from in AztecCharacterSets.All)
            {
                if (from == to || cost[at, (int)from] >= Unreachable) continue;

                int total = cost[at, (int)from] + AztecCharacterSets.LatchCost(from, to);
                if (total >= reached[(int)to]) continue;

                reached[(int)to] = total;
                through[(int)to] = Chain(chain[at, (int)from], AztecCharacterSets.Latch(from, to));
            }
        }

        foreach (var set in AztecCharacterSets.All)
        {
            cost[at, (int)set]  = reached[(int)set];
            chain[at, (int)set] = through[(int)set];
        }
    }

    /// <summary>The character at <paramref name="at"/> written in the set already in force.</summary>
    private static void Character(ReadOnlySpan<byte> bytes, int[,] cost, Emission?[,] chain,
                                  int at, AztecCharacterSet set, int from)
    {
        int width = AztecCharacterSets.Width(set);

        if (AztecCharacterSets.Single(set, (char)bytes[at]) is int code)
            Relax(cost, chain, at + 1, set, from + width, new CodeEmission(chain[at, (int)set], code, width));

        if (set == AztecCharacterSet.Punct && at + 1 < bytes.Length
            && AztecCharacterSets.Pair((char)bytes[at], (char)bytes[at + 1]) is int pair)
            Relax(cost, chain, at + 2, set, from + width, new CodeEmission(chain[at, (int)set], pair, width));
    }

    /// <summary>The character borrowed from another set for one code, leaving the set in force unchanged.</summary>
    private static void Shifted(ReadOnlySpan<byte> bytes, int[,] cost, Emission?[,] chain,
                                int at, AztecCharacterSet set, int from)
    {
        foreach (var target in (AztecCharacterSet[])[AztecCharacterSet.Punct, AztecCharacterSet.Upper])
        {
            if (AztecCharacterSets.Shift(set, target) is not { } shift) continue;

            int width = AztecCharacterSets.Width(target);
            var after = new CodeEmission(chain[at, (int)set], shift.Value, shift.Width);

            if (AztecCharacterSets.Single(target, (char)bytes[at]) is int code)
                Relax(cost, chain, at + 1, set, from + shift.Width + width,
                      new CodeEmission(after, code, width));

            if (target == AztecCharacterSet.Punct && at + 1 < bytes.Length
                && AztecCharacterSets.Pair((char)bytes[at], (char)bytes[at + 1]) is int pair)
                Relax(cost, chain, at + 2, set, from + shift.Width + width,
                      new CodeEmission(after, pair, width));
        }
    }

    /// <summary>
    /// A run of raw bytes. Every length is offered, because the header is paid once per run and the
    /// break-even against spelling the characters out depends on how far the run goes.
    /// </summary>
    private static void Bytes(int[,] cost, Emission?[,] chain, int at, AztecCharacterSet set, int from, int n)
    {
        if (!AztecCharacterSets.CanByteShift(set)) return;

        int longest = Math.Min(n - at, AztecCharacterSets.MaxByteRun);
        for (int count = 1; count <= longest; count++)
            Relax(cost, chain, at + count, set, from + RunCost(count),
                  new ByteRun(chain[at, (int)set], at, count));
    }

    /// <summary>The byte shift, its length field, and the bytes themselves.</summary>
    private static int RunCost(int count) =>
        AztecCharacterSets.Width(AztecCharacterSet.Upper) + (count <= ShortRun ? 5 : 5 + 11) + count * 8;

    /// <summary>The longest run the five-bit length field can state on its own.</summary>
    private const int ShortRun = 31;

    private static void Relax(int[,] cost, Emission?[,] chain, int at, AztecCharacterSet set,
                              int total, Emission emission)
    {
        if (total >= cost[at, (int)set]) return;
        cost[at, (int)set]  = total;
        chain[at, (int)set] = emission;
    }

    private static Emission? Chain(Emission? tail, AztecCharacterSets.Code[] codes)
    {
        foreach (var code in codes) tail = new CodeEmission(tail, code.Value, code.Width);
        return tail;
    }

    // ── Writing it out ─────────────────────────────────────────────────────

    private static void Write(List<bool> bits, Emission? last, ReadOnlySpan<byte> bytes)
    {
        var order = new List<Emission>();
        for (var link = last; link is not null; link = link.Previous) order.Add(link);

        for (int i = order.Count - 1; i >= 0; i--)
            switch (order[i])
            {
                case CodeEmission code:
                    Append(bits, code.Value, code.Width);
                    break;

                case ByteRun run:
                    Append(bits, AztecCharacterSets.ByteShift,
                           AztecCharacterSets.Width(AztecCharacterSet.Upper));
                    if (run.Count <= ShortRun)
                    {
                        Append(bits, run.Count, 5);
                    }
                    else
                    {
                        // A zero length field says the real one follows in eleven bits, biased by the
                        // longest run the short field could have stated.
                        Append(bits, 0, 5);
                        Append(bits, run.Count - ShortRun, 11);
                    }
                    for (int b = 0; b < run.Count; b++) Append(bits, bytes[run.Start + b], 8);
                    break;
            }
    }

    private static void Append(List<bool> bits, int value, int width)
    {
        for (int bit = width - 1; bit >= 0; bit--) bits.Add((value >> bit & 1) != 0);
    }
}
