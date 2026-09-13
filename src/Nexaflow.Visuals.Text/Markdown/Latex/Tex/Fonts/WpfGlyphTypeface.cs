using System.Windows.Media;
using Nexaflow.Visuals.Text.Markdown.Latex.Tex.Fonts;

namespace Nexaflow.Visuals.Text.Markdown.Latex.Tex.Fonts;

internal record WpfGlyphTypeface(GlyphTypeface Typeface) : IFontTypeface;
