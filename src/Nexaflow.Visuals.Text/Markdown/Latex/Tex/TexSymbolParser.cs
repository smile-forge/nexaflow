using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Nexaflow.Visuals.Text.Markdown.Latex;
using XamlMath.Data;
using XamlMath.Utils;

namespace XamlMath;

// Parse definitions of symbols from XML files.
internal sealed class TexSymbolParser
{
    private static readonly string resourceName = TexUtilities.ResourcesDataDirectory + "TexSymbols.xml";

    private static readonly IReadOnlyDictionary<string, TexAtomType> typeMappings;

    static TexSymbolParser()
    {
        typeMappings = new Dictionary<string, TexAtomType>
        {
            ["ord"] = TexAtomType.Ordinary,
            ["op"] = TexAtomType.BigOperator,
            ["bin"] = TexAtomType.BinaryOperator,
            ["rel"] = TexAtomType.Relation,
            ["open"] = TexAtomType.Opening,
            ["close"] = TexAtomType.Closing,
            ["punct"] = TexAtomType.Punctuation,
            ["acc"] = TexAtomType.Accent,
        };
    }

    private readonly XElement rootElement;

    public TexSymbolParser()
    {
        this.rootElement = typeof(XamlMathResourceMarker).Assembly.ReadResourceRoot(resourceName);
    }

    public IReadOnlyDictionary<string, Glyph> GetSymbols()
    {
        var result = new Dictionary<string, Glyph>();

        foreach (var symbolElement in rootElement.Elements("Symbol"))
        {
            var symbolName = symbolElement.AttributeValue("name");

            result.Add(symbolName, new Glyph
            {
                SymbolName = symbolName,
                Type = typeMappings[symbolElement.AttributeValue("type")],
                IsDelimiter = symbolElement.AttributeBooleanValue("del", false),
            });
        }

        return result;
    }
}
