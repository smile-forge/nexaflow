using Nexaflow.IO.Protocol.Values;

namespace Nexaflow.IO.Protocol.Converters;

/// <summary>
/// A variable-length prefix code, driven entirely by a table the document supplies.
///
/// <para>
/// The split is the same one <c>crc16</c> makes and for the same reason: the <b>algorithm</b> is a notion —
/// symbols become bit runs, bit runs become octets, and no octet boundary lines up with anything — while
/// the <b>table</b> is a specification's own content, hundreds of rows of it, and belongs in the document
/// that cites the specification. A converter carrying one family's table would be that family's mechanism
/// living in the engine under a general name.
/// </para>
///
/// <para>
/// The table is a list of <c>[symbol, code, bits]</c> rows rather than an array indexed by symbol, so a
/// code over four symbols is four rows. That keeps a small illustrative table writable, and it is also how
/// a specification prints one.
/// </para>
///
/// <para>
/// <b>The end-of-stream symbol is required</b>, and not because anything emits it. A run of symbols almost
/// never ends on an octet boundary, so the last octet has to be filled with something, and what a
/// specification says to fill it with is the leading bits of the end-of-stream code. That makes the padding
/// checkable rather than ignorable: too much of it, or the wrong bits, is a malformed value and not a
/// detail. Without the rule an encoder could pad with anything, two encodings of one value would both
/// decode, and value → octets would stop being injective.
/// </para>
/// </summary>
internal static class PrefixCode
{
    /// <summary>The symbol that never appears in a value and always ends one.</summary>
    private const long EndOfStream = 256;

    /// <summary>Widest code this will consider, which bounds the walk over a run of ones.</summary>
    private const int MaxBits = 32;

    private readonly record struct Code(long Bits, int Width);

    /// <summary>Symbols to octets, most significant bit first, padded with the end-of-stream prefix.</summary>
    public static byte[] Pack(byte[] input, ProtoValue table)
    {
        var (bySymbol, _, eos) = Read(table);

        List<byte> output = [];
        long accumulator = 0;
        int held = 0;

        foreach (var symbol in input)
        {
            if (!bySymbol.TryGetValue(symbol, out var code))
                throw new ProtoTypeException(
                    $"the code table has no entry for octet {symbol}, so this value cannot be written. A "
                  + "table that does not cover every octet a field may hold is incomplete for that field.");

            accumulator = (accumulator << code.Width) | code.Bits;
            held += code.Width;

            while (held >= 8)
            {
                held -= 8;
                output.Add((byte)(accumulator >> held));
            }
        }

        // What is left of the last octet, filled from the top of the end-of-stream code. Nothing is being
        // said here — the bits exist because octets do.
        if (held > 0)
        {
            int short_ = 8 - held;
            output.Add((byte)((accumulator << short_) | (eos.Bits >> (eos.Width - short_))));
        }

        return [.. output];
    }

    /// <summary>
    /// Octets back to symbols, refusing anything a writer of this table would not have produced.
    /// </summary>
    /// <remarks>
    /// Three refusals, all of them the same law wearing different clothes: one value, one encoding. Padding
    /// wide enough to have held another symbol means the writer stopped early; padding that is not the
    /// end-of-stream prefix means it padded with something of its own; and the end-of-stream symbol itself
    /// inside a value means the run was terminated by hand. Each of the three would give a second set of
    /// octets that decodes to the same value.
    /// </remarks>
    public static byte[] Unpack(byte[] input, ProtoValue table)
    {
        var (_, byCode, eos) = Read(table);

        List<byte> output = [];
        long current = 0;
        int width = 0;

        foreach (var octet in input)
            for (int bit = 7; bit >= 0; bit--)
            {
                current = (current << 1) | (uint)((octet >> bit) & 1);

                if (++width > MaxBits)
                    throw new ProtoTypeException(
                        $"{MaxBits} bits went by without matching any code in the table");

                if (!byCode.TryGetValue(new Code(current, width), out var symbol)) continue;

                if (symbol == EndOfStream)
                    throw new ProtoTypeException(
                        "the end-of-stream symbol appears inside the value. It ends a run and is never part "
                      + "of one, so a value containing it has been terminated by hand and would encode to "
                      + "different octets.");

                output.Add((byte)symbol);
                current = 0;
                width = 0;
            }

        if (width >= 8)
            throw new ProtoTypeException(
                $"{width} bits are left over, which is enough to have carried another symbol. Padding fills "
              + "out an octet; more than that means the run stopped early.");

        if (width > 0 && current != eos.Bits >> (eos.Width - width))
            throw new ProtoTypeException(
                $"the last {width} bit(s) are not the leading bits of the end-of-stream code, so this was "
              + "padded with something other than what the table says to pad with. Two paddings of one "
              + "value would both decode, and only one of them can be what gets written back.");

        return [.. output];
    }

    /// <summary>
    /// The table, both ways round, with the end-of-stream row pulled out.
    /// </summary>
    /// <remarks>
    /// Built per call rather than cached. A table is a few hundred rows and a message is a few hundred
    /// octets, so the walk over it is the same order as the work it enables — and a cache keyed on a value
    /// that arrives as an argument is a lifetime question the engine has no reason to take on.
    /// </remarks>
    private static (Dictionary<long, Code> BySymbol, Dictionary<Code, long> ByCode, Code Eos) Read(
        ProtoValue table)
    {
        Dictionary<long, Code> bySymbol = [];
        Dictionary<Code, long> byCode = [];
        Code? eos = null;

        foreach (var row in table.AsList())
        {
            var parts = row is ProtoValue.List l && l.Items.Count == 3
                ? l.Items
                : throw new ProtoTypeException(
                    "each row of a code table is [symbol, code, bits] — three values, and this row is "
                  + $"{row}");

            long symbol = parts[0].AsInt();
            var code = new Code(parts[1].AsInt(), (int)parts[2].AsInt());

            if (code.Width is < 1 or > MaxBits)
                throw new ProtoTypeException(
                    $"symbol {symbol} is given a code {code.Width} bit(s) wide, and a code is 1..{MaxBits}");

            if (code.Bits < 0 || (code.Width < 63 && code.Bits >= 1L << code.Width))
                throw new ProtoTypeException(
                    $"symbol {symbol}'s code {code.Bits} does not fit in the {code.Width} bit(s) it declares");

            if (!bySymbol.TryAdd(symbol, code))
                throw new ProtoTypeException(
                    $"symbol {symbol} appears twice in the code table, with two different codes. Which one "
                  + "gets written would depend on the order the rows happen to be in.");

            // Prefix-freeness, checked where the table is read rather than trusted. Two codes where one
            // leads the other is a table that decodes some runs into a different value than it wrote —
            // silently, and only for the inputs that happen to contain the pair.
            if (!byCode.TryAdd(code, symbol))
                throw new ProtoTypeException(
                    $"symbols {symbol} and {byCode[code]} are given the same code");

            if (symbol == EndOfStream) eos = code;
        }

        foreach (var (code, symbol) in byCode)
            for (int shorter = 1; shorter < code.Width; shorter++)
                if (byCode.TryGetValue(new Code(code.Bits >> (code.Width - shorter), shorter), out var other))
                    throw new ProtoTypeException(
                        $"symbol {other}'s code leads symbol {symbol}'s, so a reader cannot tell where one "
                      + "ends. A code table has to be prefix-free.");

        if (eos is not { } ending)
            throw new ProtoTypeException(
                $"the code table has no symbol {EndOfStream}, which is what a run is padded out with. "
              + "Without it there is nothing to fill the last octet with and nothing to check it against.");

        if (ending.Width < 7)
            throw new ProtoTypeException(
                $"the end-of-stream code is {ending.Width} bits and padding can need 7, so there would be "
              + "runs this table cannot finish");

        return (bySymbol, byCode, ending);
    }
}
