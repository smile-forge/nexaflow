namespace Nexaflow.Markdown.Chemistry;

/// <summary>
/// The periodic table, as far as reading and drawing a structure needs it: which symbols are elements, which bare
/// symbols SMILES lets you write without brackets, and how many bonds an atom may make.
///
/// <para>
/// <b>Valence follows RDKit</b>, because RDKit is the reader the corpus was checked against and the one most
/// SMILES was written for: nitrogen makes three bonds and never five, phosphorus three, five or seven, sulfur two,
/// four or six. A charge moves an atom along its row — N⁺ bonds like carbon, O⁻ like fluorine, C⁻ like nitrogen —
/// which is what lets <c>[NH4+]</c> and <c>[O-]</c> be read without a table of charged forms.
/// </para>
/// <para>
/// Anything off the main groups — the transition metals, the lanthanides — has no valence here, and is never said
/// to have too many bonds. A coordination compound is written with whatever bonds its author meant.
/// </para>
/// </summary>
public static class Elements
{
    private static readonly string[] Symbols =
    [
        "*",
        "H", "He",
        "Li", "Be", "B", "C", "N", "O", "F", "Ne",
        "Na", "Mg", "Al", "Si", "P", "S", "Cl", "Ar",
        "K", "Ca", "Sc", "Ti", "V", "Cr", "Mn", "Fe", "Co", "Ni", "Cu", "Zn", "Ga", "Ge", "As", "Se", "Br", "Kr",
        "Rb", "Sr", "Y", "Zr", "Nb", "Mo", "Tc", "Ru", "Rh", "Pd", "Ag", "Cd", "In", "Sn", "Sb", "Te", "I", "Xe",
        "Cs", "Ba", "La", "Ce", "Pr", "Nd", "Pm", "Sm", "Eu", "Gd", "Tb", "Dy", "Ho", "Er", "Tm", "Yb", "Lu",
        "Hf", "Ta", "W", "Re", "Os", "Ir", "Pt", "Au", "Hg", "Tl", "Pb", "Bi", "Po", "At", "Rn",
        "Fr", "Ra", "Ac", "Th", "Pa", "U", "Np", "Pu", "Am", "Cm", "Bk", "Cf", "Es", "Fm", "Md", "No", "Lr",
        "Rf", "Db", "Sg", "Bh", "Hs", "Mt", "Ds", "Rg", "Cn", "Nh", "Fl", "Mc", "Lv", "Ts", "Og",
    ];

    private static readonly Dictionary<string, int> Numbers =
        Symbols.Select((symbol, number) => (symbol, number)).ToDictionary(p => p.symbol, p => p.number, StringComparer.Ordinal);

    /// <summary>
    /// The bonds each main-group element may make, lowest first — RDKit's table. An element that is not here may
    /// make any number.
    /// </summary>
    private static readonly Dictionary<int, int[]> Valences = new()
    {
        [1] = [1], [2] = [0],
        [3] = [1], [4] = [2], [5] = [3], [6] = [4], [7] = [3], [8] = [2], [9] = [1], [10] = [0],
        [11] = [1], [12] = [2], [13] = [3], [14] = [4], [15] = [3, 5, 7], [16] = [2, 4, 6], [17] = [1], [18] = [0],
        [19] = [1], [20] = [2], [31] = [3], [32] = [4], [33] = [3, 5, 7], [34] = [2, 4, 6], [35] = [1], [36] = [0],
        [37] = [1], [38] = [2], [49] = [3], [50] = [2, 4], [51] = [3, 5, 7], [52] = [2, 4, 6], [53] = [1, 3, 5], [54] = [0],
        [55] = [1], [56] = [2], [81] = [1, 3], [82] = [2, 4], [83] = [3, 5, 7], [84] = [2, 4, 6], [85] = [1], [86] = [0],
    };

    /// <summary>The symbols SMILES lets you write without brackets, the aromatic ones included.</summary>
    private static readonly HashSet<string> Organic = new(StringComparer.Ordinal)
    {
        "B", "C", "N", "O", "P", "S", "F", "Cl", "Br", "I", "b", "c", "n", "o", "p", "s", "*",
    };

    /// <summary>The symbols that may be written aromatic inside brackets.</summary>
    private static readonly HashSet<string> AromaticInBrackets = new(StringComparer.Ordinal)
    {
        "b", "c", "n", "o", "p", "s", "se", "as", "te",
    };

    /// <summary>The atomic number of <paramref name="symbol"/> — written either case — or null where it names no element. Zero for <c>*</c>.</summary>
    public static int? Number(string symbol)
    {
        if (symbol.Length == 0) return null;
        var proper = char.ToUpperInvariant(symbol[0]) + symbol[1..];
        return Numbers.TryGetValue(proper, out var number) ? number : null;
    }

    /// <summary>The symbol for atomic number <paramref name="number"/>, properly cased.</summary>
    public static string Symbol(int number) => number >= 0 && number < Symbols.Length ? Symbols[number] : "*";

    /// <summary>Whether <paramref name="symbol"/> is an element name at all, written properly cased.</summary>
    public static bool IsElement(string symbol) => Numbers.ContainsKey(symbol);

    /// <summary>Whether <paramref name="symbol"/> may stand without brackets.</summary>
    public static bool IsOrganic(string symbol) => Organic.Contains(symbol);

    /// <summary>Whether <paramref name="symbol"/> may be written aromatic inside brackets.</summary>
    public static bool IsAromaticSymbol(string symbol) => AromaticInBrackets.Contains(symbol);

    /// <summary>
    /// The bonds an atom of <paramref name="number"/> with <paramref name="charge"/> may make, lowest first — or null
    /// where there is no limit.
    ///
    /// <para>
    /// A charge moves the atom along its row to the element it has as many electrons as, which is only meaningful
    /// while that element is a main-group one too. A charged transition metal has no limit, and nor does anything
    /// charged past the end of the table.
    /// </para>
    /// </summary>
    public static IReadOnlyList<int>? Allowed(int number, int charge)
    {
        if (!Valences.ContainsKey(number)) return null;

        var like = number - charge;
        if (charge != 0 && (Row(like) != Row(number) || !Valences.ContainsKey(like))) return charge > 0 && IsEarly(number) ? null : Valences.GetValueOrDefault(number);

        return Valences[like];
    }

    /// <summary>
    /// The fewest bonds from <paramref name="allowed"/> that is at least <paramref name="used"/>, or null where even
    /// the most is too few.
    /// </summary>
    public static int? Fitting(IReadOnlyList<int> allowed, int used)
    {
        foreach (var valence in allowed)
            if (valence >= used) return valence;
        return null;
    }

    /// <summary>Which row of the table an element is in; the noble gas closes it.</summary>
    private static int Row(int number) => number switch
    {
        <= 2 => 1,
        <= 10 => 2,
        <= 18 => 3,
        <= 36 => 4,
        <= 54 => 5,
        <= 86 => 6,
        _ => 7,
    };

    /// <summary>The alkali and alkaline-earth metals, whose positive ions have given their bonding electrons away.</summary>
    private static bool IsEarly(int number) => number is 3 or 4 or 11 or 12 or 19 or 20 or 37 or 38 or 55 or 56;

    /// <summary>What an element is called, for a reason set beneath a structure.</summary>
    public static string Name(int number) => number switch
    {
        0 => "a wildcard atom",
        1 => "hydrogen",
        5 => "boron",
        6 => "carbon",
        7 => "nitrogen",
        8 => "oxygen",
        9 => "fluorine",
        14 => "silicon",
        15 => "phosphorus",
        16 => "sulfur",
        17 => "chlorine",
        35 => "bromine",
        53 => "iodine",
        _ => Symbol(number),
    };
}
