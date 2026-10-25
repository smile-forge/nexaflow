using Nexaflow.Markdown.Latex;
using Nexaflow.Markdown.Typesetting.Tex;
using Nexaflow.Markdown.Typesetting.Tex.Exceptions;
using Nexaflow.Markdown.Typesetting.Tex.Fonts;
using Nexaflow.Markdown.Typesetting.Tex.Utils;
using TexEnvironment = Nexaflow.Markdown.Typesetting.Tex.TexEnvironment;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Nexaflow.Markdown.Layout.Maths;

/// <summary>
/// What maths is typeset with in a medium: the maths faces as TeX's tables describe them — read once, since nothing in them
/// depends on size — and the face <c>\text{…}</c> is set in. Made once for a medium by whatever hosts it, as a style is.
/// </summary>
public sealed class PortableTexFonts
{
    private PortableTexFonts(ILayoutMedium medium, DefaultTexFontData maths, LayoutFace text)
    {
        Medium = medium;
        Maths = maths;
        Text = text;
    }

    internal ILayoutMedium Medium { get; }

    internal DefaultTexFontData Maths { get; }

    /// <summary>The face <c>\text{…}</c> is set in.</summary>
    public LayoutFace Text { get; }

    /// <summary>
    /// The maths faces <paramref name="medium"/> has, with <paramref name="textFamily"/> for <c>\text{…}</c> — read the first time anything
    /// asks and kept with the medium, since the tables do not change.
    /// </summary>
    public static PortableTexFonts Of(ILayoutMedium medium, string textFamily) =>
        Kept.GetOrCreateValue(medium).GetOrAdd(textFamily, family => new(medium, DefaultTexFontData.Read(new MediumFontProvider(medium)), medium.Face(family)));

    /// <summary>What each medium was found to have, kept as long as the medium is.</summary>
    private static readonly ConditionalWeakTable<ILayoutMedium, ConcurrentDictionary<string, PortableTexFonts>> Kept = new();

    /// <summary>What one formula is typeset in: its style, the maths faces at its size, and the text face at the same.</summary>
    internal TexEnvironment Environment(TexStyle style, double scale) =>
        new(style, new DefaultTexFont(Maths, scale), new MediumSystemFont(Medium, Text, scale));
}

/// <summary>A face a medium opened, as the typesetter's tables hold it.</summary>
internal sealed record MediumTypeface(LayoutFace Face) : IFontTypeface;

/// <summary>The maths faces the tables name, opened by a medium.</summary>
internal sealed class MediumFontProvider(ILayoutMedium medium) : IFontProvider
{
    public IFontTypeface ReadFontFile(string fontFileName) => new MediumTypeface(medium.Font(fontFileName));
}

/// <summary>
/// The face <c>\text{…}</c> is set in, measured a letter at a time by the medium: a face with no TeX metrics of its own, so
/// everything a maths font answers it cannot.
/// </summary>
internal sealed class MediumSystemFont(ILayoutMedium medium, LayoutFace face, double size) : ITeXFont
{
    public bool SupportsMetrics => false;

    public double Size { get; } = size;

    public Result<CharInfo> GetCharInfo(char character, string textStyle, TexStyle style)
    {
        var ink = medium.Letter(face, character);

        return Result.Ok(new CharInfo(character, new MediumTypeface(face), 1.0, TexFontUtilities.NoFontId,
                                      new TeXFontMetrics(ink.Width, ink.Height, ink.Depth, 0, 1.0)));
    }

    public CharFont? GetLigature(CharFont leftChar, CharFont rightChar) => null;

    public double GetKern(CharFont leftChar, CharFont rightChar, TexStyle style) => 0.0;

    public Result<CharInfo> GetBoldCharInfo(CharInfo charInfo, TexStyle style) =>
        Result.Error<CharInfo>(Unsupported(nameof(GetBoldCharInfo)));

    public Result<CharInfo> GetDefaultCharInfo(char character, TexStyle style) =>
        Result.Error<CharInfo>(Unsupported(nameof(GetDefaultCharInfo)));

    public Result<CharInfo> GetCharInfo(CharFont charFont, TexStyle style) => Result.Error<CharInfo>(Unsupported(nameof(GetCharInfo)));

    public Result<CharInfo> GetCharInfo(string name, TexStyle style) => Result.Error<CharInfo>(Unsupported(nameof(GetCharInfo)));

    public ExtensionChar GetExtension(CharInfo charInfo, TexStyle style) => throw Unsupported(nameof(GetExtension));

    public CharInfo GetNextLargerCharInfo(CharInfo charInfo, TexStyle style) => throw Unsupported(nameof(GetNextLargerCharInfo));

    public double GetQuad(int fontId, TexStyle style) => throw Unsupported(nameof(GetQuad));

    public double GetSkew(CharFont charFont, TexStyle style) => throw Unsupported(nameof(GetSkew));

    public bool HasSpace(int fontId) => throw Unsupported(nameof(HasSpace));

    public bool HasNextLarger(CharInfo charInfo) => throw Unsupported(nameof(HasNextLarger));

    public bool IsExtensionChar(CharInfo charInfo) => throw Unsupported(nameof(IsExtensionChar));

    public int GetMuFontId() => throw Unsupported(nameof(GetMuFontId));

    public double GetXHeight(TexStyle style, int fontId) => throw Unsupported(nameof(GetXHeight));

    public double GetSpace(TexStyle style) => throw Unsupported(nameof(GetSpace));

    public double GetAxisHeight(TexStyle style) => throw Unsupported(nameof(GetAxisHeight));

    public double GetBigOpSpacing1(TexStyle style) => throw Unsupported(nameof(GetBigOpSpacing1));

    public double GetBigOpSpacing2(TexStyle style) => throw Unsupported(nameof(GetBigOpSpacing2));

    public double GetBigOpSpacing3(TexStyle style) => throw Unsupported(nameof(GetBigOpSpacing3));

    public double GetBigOpSpacing4(TexStyle style) => throw Unsupported(nameof(GetBigOpSpacing4));

    public double GetBigOpSpacing5(TexStyle style) => throw Unsupported(nameof(GetBigOpSpacing5));

    public double GetSub1(TexStyle style) => throw Unsupported(nameof(GetSub1));

    public double GetSub2(TexStyle style) => throw Unsupported(nameof(GetSub2));

    public double GetSubDrop(TexStyle style) => throw Unsupported(nameof(GetSubDrop));

    public double GetSup1(TexStyle style) => throw Unsupported(nameof(GetSup1));

    public double GetSup2(TexStyle style) => throw Unsupported(nameof(GetSup2));

    public double GetSup3(TexStyle style) => throw Unsupported(nameof(GetSup3));

    public double GetSupDrop(TexStyle style) => throw Unsupported(nameof(GetSupDrop));

    public double GetNum1(TexStyle style) => throw Unsupported(nameof(GetNum1));

    public double GetNum2(TexStyle style) => throw Unsupported(nameof(GetNum2));

    public double GetNum3(TexStyle style) => throw Unsupported(nameof(GetNum3));

    public double GetDenom1(TexStyle style) => throw Unsupported(nameof(GetDenom1));

    public double GetDenom2(TexStyle style) => throw Unsupported(nameof(GetDenom2));

    public double GetDefaultLineThickness(TexStyle style) => throw Unsupported(nameof(GetDefaultLineThickness));

    private static TexNotSupportedException Unsupported(string method) =>
        new($"Call of method {method} on {nameof(MediumSystemFont)} is not supported");
}
