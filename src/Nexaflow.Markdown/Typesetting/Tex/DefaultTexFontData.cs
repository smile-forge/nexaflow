using System;
using System.Collections.Generic;
using Nexaflow.Markdown.Typesetting.Tex.Fonts;

namespace Nexaflow.Markdown.Typesetting.Tex;

/// <summary>
/// What <c>DefaultTexFont.xml</c> says about the maths fonts, and the faces it names: read once and shared by every
/// <see cref="DefaultTexFont"/>, whatever size each one sets them at.
///
/// <para>
/// Reading it parses the XML and has the host's <see cref="IFontProvider"/> open every face it lists, which costs
/// milliseconds and over a megabyte each time. None of it depends on size, and none of it changes once read: the
/// parser is the only writer of a <see cref="TexFontInfo"/>, and everything here is handed out read-only. So one reading
/// serves every formula, on every thread that lays one out.
/// </para>
/// </summary>
internal sealed class DefaultTexFontData
{
    public IReadOnlyDictionary<string, double> Parameters { get; }
    public IReadOnlyDictionary<string, object> GeneralSettings { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<CharFont>> TextStyleMappings { get; }
    public IReadOnlyList<string> DefaultTextStyleMappings { get; }
    public IReadOnlyDictionary<string, CharFont> SymbolMappings { get; }
    public IReadOnlyList<TexFontInfo> FontInfoList { get; }

    private DefaultTexFontData(DefaultTexFontParser parser)
    {
        Parameters = parser.GetParameters();
        GeneralSettings = parser.GetGeneralSettings();
        TextStyleMappings = parser.GetTextStyleMappings();
        DefaultTextStyleMappings = parser.GetDefaultTextStyleMappings();
        SymbolMappings = parser.GetSymbolMappings();
        FontInfoList = parser.GetFontDescriptions();

        // Check that Mu font exists.
        var muFontId = (int)GeneralSettings["mufontid"];
        if (muFontId < 0 || muFontId >= FontInfoList.Count || FontInfoList[muFontId] == null)
            throw new InvalidOperationException("ID of Mu font is invalid.");
    }

    /// <summary>Reads the font description, opening each face it names through <paramref name="fontProvider"/>.</summary>
    public static DefaultTexFontData Read(IFontProvider fontProvider) => new(new DefaultTexFontParser(fontProvider));
}
