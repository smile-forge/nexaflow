using XamlMath.Fonts;
using XamlMath.Utils;

namespace XamlMath.Atoms;

// Atom representing character that does not depend on text style.
internal sealed record FixedCharAtom : CharSymbol
{
    public FixedCharAtom(CharFont charFont)
    {
        this.CharFont = charFont;
    }

    public CharFont CharFont { get; }

    protected override Result<CharInfo> GetCharInfo(ITeXFont font, TexStyle style) =>
        font.GetCharInfo(this.CharFont, style);

    public override Result<CharFont> GetCharFont(ITeXFont texFont) =>
        Result.Ok(this.CharFont);
}
