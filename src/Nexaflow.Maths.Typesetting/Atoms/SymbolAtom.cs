using System;
using System.Collections;
using System.Collections.Generic;
using XamlMath.Fonts;
using XamlMath.Utils;
using System.Diagnostics.CodeAnalysis;

namespace XamlMath.Atoms;

// Atom representing symbol (non-alphanumeric character).
internal sealed record SymbolAtom : CharSymbol
{
    /// <summary>
    /// Special name of empty delimiter symbol that shouldn't be rendered.
    /// </summary>
    internal const string EmptyDelimiterName = "_emptyDelimiter";

    // Dictionary of definitions of all symbols, keyed by name.
    private static readonly IReadOnlyDictionary<string, Func<SymbolAtom>> symbols;

    // Set of all valid symbol types.
    private static readonly BitArray validSymbolTypes;

    static SymbolAtom()
    {
        var symbolParser = new TexSymbolParser();
        symbols = symbolParser.GetSymbols();

        validSymbolTypes = new BitArray(16);
        validSymbolTypes.Set((int)TexAtomType.Ordinary, true);
        validSymbolTypes.Set((int)TexAtomType.BigOperator, true);
        validSymbolTypes.Set((int)TexAtomType.BinaryOperator, true);
        validSymbolTypes.Set((int)TexAtomType.Relation, true);
        validSymbolTypes.Set((int)TexAtomType.Opening, true);
        validSymbolTypes.Set((int)TexAtomType.Closing, true);
        validSymbolTypes.Set((int)TexAtomType.Punctuation, true);
        validSymbolTypes.Set((int)TexAtomType.Accent, true);
    }

    public static bool TryGetAtom(string name, [NotNullWhen(true)] out SymbolAtom? atom)
    {
        if (!symbols.TryGetValue(name, out var factory))
        {
            atom = null;
            return false;
        }

        var symbol = factory();
        atom = new SymbolAtom(symbol, symbol.Type);
        return true;
    }

    public static SymbolAtom GetAtom(string name) =>
        TryGetAtom(name, out var atom) ? atom : throw new SymbolNotFoundException(name);

    public SymbolAtom(SymbolAtom symbolAtom, TexAtomType type)
        : base(type)
    {
        if (!validSymbolTypes[(int)type])
            throw new ArgumentException("The specified type is not a valid symbol type.", nameof(type));
        this.Name = symbolAtom.Name;
        this.IsDelimeter = symbolAtom.IsDelimeter;
    }

    public SymbolAtom(string name, TexAtomType type, bool isDelimeter)
        : base(type)
    {
        this.Name = name;
        this.IsDelimeter = isDelimeter;
    }

    public bool IsDelimeter { get; }

    public string Name { get; }

    protected override Result<CharInfo> GetCharInfo(ITeXFont font, TexStyle style) =>
        font.GetCharInfo(this.Name, style);

    public override Result<CharFont> GetCharFont(ITeXFont texFont) =>
        // Style is irrelevant here.
        texFont.GetCharInfo(this.Name, TexStyle.Display).Map(ci => ci.GetCharacterFont());
}
