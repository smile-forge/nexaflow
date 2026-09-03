using System;

namespace Nexaflow.Visuals.Text.Markdown.Matrix;

/// <summary>
/// Reed–Solomon parity over a Galois field of the caller's choosing.
///
/// <para>
/// Every matrix symbology protects its codewords this way and every one of them picks a different
/// field: QR and Aztec's largest symbols work in GF(256) under 0x11D, Data Matrix in GF(256) under
/// 0x12D, PDF417 in GF(929) — a prime field, not a binary one — and Aztec's smaller symbols in GF(16),
/// GF(64) and GF(1024). The arithmetic is identical; only the modulus and the size differ, so the field
/// is a parameter and the codec is written once.
/// </para>
/// <para>
/// Both binary and prime fields are handled by the same two tables. Multiplication is a table lookup
/// either way; what differs is how the tables are built and whether "add" is XOR or modular addition,
/// and <see cref="GaloisField"/> knows which it is.
/// </para>
/// </summary>
public sealed class GaloisField
{
    private readonly int[] _exp;
    private readonly int[] _log;

    /// <summary>GF(256) under x⁸+x⁴+x³+x²+1 — QR, and Aztec above 8 layers.</summary>
    public static readonly GaloisField Qr = Binary(0x11D, 256);

    /// <summary>GF(256) under x⁸+x⁵+x³+x²+1 — Data Matrix ECC 200.</summary>
    public static readonly GaloisField DataMatrix = Binary(0x12D, 256);

    /// <summary>The prime field GF(929), generator 3 — PDF417.</summary>
    public static readonly GaloisField Pdf417 = Prime(929, 3);

    private GaloisField(int size, int[] exp, int[] log, bool binary)
    {
        Size     = size;
        _exp     = exp;
        _log     = log;
        IsBinary = binary;
    }

    /// <summary>How many elements the field has; codewords range over 0 to Size − 1.</summary>
    public int Size { get; }

    /// <summary>True for a GF(2ⁿ) field, where addition is XOR; false for a prime field, where it is modular.</summary>
    public bool IsBinary { get; }

    /// <summary>A GF(2ⁿ) field under <paramref name="primitive"/>, with 2 as the generator.</summary>
    public static GaloisField Binary(int primitive, int size)
    {
        var exp = new int[size * 2];
        var log = new int[size];

        int x = 1;
        for (int i = 0; i < size - 1; i++)
        {
            exp[i] = x;
            log[x] = i;
            x <<= 1;
            if (x >= size) x ^= primitive;
        }
        for (int i = size - 1; i < exp.Length; i++) exp[i] = exp[i - (size - 1)];

        return new GaloisField(size, exp, log, binary: true);
    }

    /// <summary>The prime field GF(<paramref name="prime"/>) with the given generator.</summary>
    public static GaloisField Prime(int prime, int generator)
    {
        var exp = new int[prime * 2];
        var log = new int[prime];

        int x = 1;
        for (int i = 0; i < prime - 1; i++)
        {
            exp[i] = x;
            log[x] = i;
            x = x * generator % prime;
        }
        for (int i = prime - 1; i < exp.Length; i++) exp[i] = exp[i - (prime - 1)];

        return new GaloisField(prime, exp, log, binary: false);
    }

    public int Add(int a, int b) => IsBinary ? a ^ b : (a + b) % Size;

    public int Subtract(int a, int b) => IsBinary ? a ^ b : (a - b + Size) % Size;

    public int Multiply(int a, int b) =>
        a == 0 || b == 0 ? 0 : _exp[_log[a] + _log[b]];

    /// <summary>The generator raised to <paramref name="power"/>.</summary>
    public int Exp(int power) => _exp[power % (Size - 1)];

    public int Log(int value) => _log[value];

    /// <summary>The multiplicative inverse.</summary>
    public int Inverse(int a) => _exp[Size - 1 - _log[a]];
}

/// <summary>
/// The encoding half of Reed–Solomon: the parity codewords that let a reader repair a damaged symbol.
/// </summary>
public static class ReedSolomon
{
    /// <summary>
    /// The generator polynomial's coefficients, highest degree first — the product of
    /// (x − gᶦ) for i from <paramref name="firstRoot"/> below <paramref name="firstRoot"/> + <paramref name="degree"/>.
    /// </summary>
    /// <param name="firstRoot">
    /// QR and Aztec start at g⁰; Data Matrix and PDF417 start at g¹. It is the one thing the standards
    /// disagree about, and getting it wrong produces parity that is perfectly consistent and reads back
    /// as garbage.
    /// </param>
    public static int[] Generator(GaloisField field, int degree, int firstRoot = 0)
    {
        // Built up root by root as the product of (x − gⁱ), highest power first, with the monic
        // leading term dropped at the end because the division never needs it.
        //
        // The subtraction is the whole point. Written the way every GF(2ⁿ) implementation writes it —
        // multiply and XOR — this produces ∏(x + gⁱ), which is the same polynomial only because −1 ≡ 1
        // in a binary field. Over GF(929) it is a different polynomial, and the parity it makes is
        // self-consistent, verifies against itself, and is rejected by every real scanner.
        var coefficients = new int[] { 1 };

        for (int i = 0; i < degree; i++)
        {
            int root = field.Exp(firstRoot + i);
            var next = new int[coefficients.Length + 1];

            for (int j = 0; j < coefficients.Length; j++)
            {
                next[j]     = field.Add(next[j], coefficients[j]);
                next[j + 1] = field.Subtract(next[j + 1], field.Multiply(coefficients[j], root));
            }

            coefficients = next;
        }

        return coefficients[1..];
    }

    /// <summary>The remainder of <paramref name="data"/> over <paramref name="generator"/> — the parity codewords.</summary>
    public static int[] Parity(GaloisField field, ReadOnlySpan<int> data, int[] generator)
    {
        var result = new int[generator.Length];
        foreach (int d in data)
        {
            int factor = field.Add(d, result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            // Subtract, not add: this is polynomial division. The two are the same operation in a
            // binary field, which is why every GF(2^n) implementation writes it as XOR and why the
            // difference only shows up over a prime field.
            for (int i = 0; i < result.Length; i++)
                result[i] = field.Subtract(result[i], field.Multiply(generator[i], factor));
        }
        return result;
    }

    /// <summary>
    /// The parity for a prime field where the standard defines it by subtraction — PDF417's
    /// codewords are the negated remainder, so the caller asks for that form by name.
    /// </summary>
    public static int[] NegatedParity(GaloisField field, ReadOnlySpan<int> data, int[] generator)
    {
        var parity = Parity(field, data, generator);
        for (int i = 0; i < parity.Length; i++) parity[i] = field.Subtract(0, parity[i]);
        return parity;
    }
}
