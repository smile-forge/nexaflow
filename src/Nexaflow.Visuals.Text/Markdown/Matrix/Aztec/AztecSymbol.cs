namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// A finished Aztec symbol: its modules, and what the encoder settled on to fit the message into them.
///
/// <para>
/// It is an <see cref="IModuleMatrix"/> like every other 2D symbol, so the shared renderer draws it
/// without knowing what a bullseye is. The rest is what the block reports about itself — the family and
/// layer count that name the size, and how much of the capacity went to the message rather than to
/// error correction, which is the number that tells an author whether the symbol is comfortable.
/// </para>
/// </summary>
public sealed class AztecSymbol : IModuleMatrix
{
    private readonly bool[,] _modules;

    internal AztecSymbol(bool[,] modules, bool compact, int layers, int codewordBits,
                         int dataCodewords, int totalCodewords, int messageBits)
    {
        _modules       = modules;
        Compact        = compact;
        Layers         = layers;
        CodewordBits   = codewordBits;
        DataCodewords  = dataCodewords;
        TotalCodewords = totalCodewords;
        MessageBits    = messageBits;
    }

    public int Width => _modules.GetLength(1);

    public int Height => _modules.GetLength(0);

    public bool this[int x, int y] => _modules[y, x];

    /// <summary>Modules on a side. Aztec symbols are always square.</summary>
    public int Size => Width;

    /// <summary>True for a compact symbol, false for one in the full range.</summary>
    public bool Compact { get; }

    /// <summary>Data layers around the core — one to four compact, one to thirty-two full.</summary>
    public int Layers { get; }

    /// <summary>Bits per codeword: six, eight, ten or twelve, decided by the layer count.</summary>
    public int CodewordBits { get; }

    /// <summary>Codewords the message occupies, after bit stuffing.</summary>
    public int DataCodewords { get; }

    /// <summary>Codewords the symbol holds altogether; the rest are error correction.</summary>
    public int TotalCodewords { get; }

    /// <summary>Codewords given over to error correction.</summary>
    public int CheckCodewords => TotalCodewords - DataCodewords;

    /// <summary>What share of the symbol is error correction, as a percentage.</summary>
    public int ErrorCorrectionPercent => CheckCodewords * 100 / TotalCodewords;

    /// <summary>Bits the high-level encoding of the message came to, before stuffing and padding.</summary>
    public int MessageBits { get; }

    /// <summary>The family and size, as a symbol is usually named — "compact 2" or "full 11".</summary>
    public string Designation => $"{(Compact ? "compact" : "full")} {Layers}";
}
