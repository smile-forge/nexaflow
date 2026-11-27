using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Documents;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// An icon glyph drawn in one colour, as the WPF shell draws its emoji and Fluent icons: WPF cannot render colour fonts, so an emoji
/// comes out as its outline filled with the foreground, and the shell's look depends on that. Avalonia renders colour glyphs, so the
/// glyph's outline is taken from the font and filled here instead. Sized as the WPF icon is: at least <see cref="FontSize"/> square.
/// </summary>
public sealed class MonoGlyph : Control
{
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<MonoGlyph, string?>(nameof(Text));

    public static readonly StyledProperty<double> FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner<MonoGlyph>();

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner<MonoGlyph>();

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner<MonoGlyph>();

    /// <summary>The emoji font: what WPF's fallback reaches for an emoji in Segoe UI. A glyph in it is drawn from its plain outline
    /// (<see cref="EmojiOutlines"/>), not its colour layers.</summary>
    public static readonly FontFamily Emoji = new("Segoe UI Emoji");

    /// <summary>Where a character the emoji font lacks is drawn from, as WPF falls back from Segoe UI.</summary>
    private static readonly FontFamily TextFonts = new("Segoe UI, Segoe UI Symbol");

    private Geometry? _shape;
    private Size _extent;

    static MonoGlyph()
    {
        AffectsMeasure<MonoGlyph>(TextProperty, FontSizeProperty, FontFamilyProperty);
        AffectsRender<MonoGlyph>(ForegroundProperty);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == FontFamilyProperty) _shape = null;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Shape();
        return new Size(Math.Max(_extent.Width, FontSize), Math.Max(_extent.Height, FontSize));
    }

    public override void Render(DrawingContext context)
    {
        if (Shape() is not { } shape || Foreground is not { } brush) return;
        var offset = new Vector((Bounds.Width - _extent.Width) / 2, (Bounds.Height - _extent.Height) / 2);
        using (context.PushTransform(Matrix.CreateTranslation(offset)))
            context.DrawGeometry(brush, null, shape);
    }

    private Geometry? Shape()
    {
        if (_shape is not null || string.IsNullOrEmpty(Text)) return _shape;
        if (FontFamily == Emoji)
        {
        // A character the emoji font lacks (⬡, say) is drawn as WPF's fallback draws it: from the text fonts.
        if (EmojiOutlines.Of(Text, FontSize) is { } outline)
        {
            (_shape, _extent) = outline;
            return _shape;
        }
        }
        var family = FontFamily == Emoji ? TextFonts : FontFamily;
        var text = new FormattedText(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(family), FontSize, Brushes.Black);
        _shape  = text.BuildGeometry(default);
        _extent = new Size(text.WidthIncludingTrailingWhitespace, text.Height);
        return _shape;
    }
}
