using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Shell;

namespace Nexaflow.Visuals.Icons;

/// <summary>
/// Draws an icon that may be a picture in a Windows file's resources: <see cref="Resource"/> when it names one the shell
/// can draw, otherwise <see cref="Icon"/>'s glyph. The glyph shows at once and until the picture arrives — at once when
/// that picture has been drawn before, else when the shared shell worker reaches it — so nothing waits on the disk, and
/// an element that is never drawn never asks.
/// <para>
/// Sized and tinted like <see cref="IconGlyph"/>, by <c>FontSize</c> and <c>Foreground</c>: the picture is
/// <c>FontSize</c> wide, and the element takes the glyph's room whichever it shows, so a picture arriving never moves
/// the layout.
/// </para>
/// </summary>
public sealed class ResourceIcon : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconRef), typeof(ResourceIcon),
        new FrameworkPropertyMetadata(default(IconRef), FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ResourceProperty = DependencyProperty.Register(
        nameof(Resource), typeof(ShellIconRef?), typeof(ResourceIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Reask));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(ResourceIcon), new FrameworkPropertyMetadata(SystemFonts.MessageFontSize,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Reask));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(ResourceIcon), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(ResourceIcon), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The glyph shown until the picture arrives, if it never does, and when there is no picture to draw.</summary>
    public IconRef Icon { get => (IconRef)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>The picture to draw instead of the glyph, or null for the glyph alone.</summary>
    public ShellIconRef? Resource { get => (ShellIconRef?)GetValue(ResourceProperty); set => SetValue(ResourceProperty, value); }

    /// <summary>The glyph's size, and the picture's edge, in device-independent units.</summary>
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    /// <summary>The family an emoji glyph is drawn in; a Fluent glyph is always drawn in the bundled font.</summary>
    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    private readonly ShellIconRequest _request;

    public ResourceIcon()
    {
        _request = new ShellIconRequest(this);
        // A picture drawn for one DPI shown on a monitor finer than it.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    private static void Reask(DependencyObject owner, DependencyPropertyChangedEventArgs e) => ((ResourceIcon)owner)._request.Forget();

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _request.Forget();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var glyph = Glyph();
        return new Size(Math.Max(glyph.WidthIncludingTrailingWhitespace, FontSize), Math.Max(glyph.Height, FontSize));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Asked here rather than on each property change: a recycled element changes several at once and asks only once.
        if (_request.IsStale) Ask();

        if (_request.Icon is { } picture)
        {
            double size = FontSize;
            drawingContext.DrawImage(picture, new Rect((RenderSize.Width - size) / 2, (RenderSize.Height - size) / 2, size, size));
            return;
        }

        var glyph = Glyph();
        drawingContext.DrawText(glyph, new Point((RenderSize.Width - glyph.WidthIncludingTrailingWhitespace) / 2, (RenderSize.Height - glyph.Height) / 2));
    }

    private void Ask()
    {
        if (Resource is not { } resource)
        {
            _request.Ask(null);
            return;
        }

        int pixels = (int)Math.Ceiling(FontSize * VisualTreeHelper.GetDpi(this).DpiScaleX);
        _request.Ask(arrived => ShellResourceIcons.Shared.Request(resource, pixels, Dispatcher, arrived));
    }

    private FormattedText Glyph() => new(
        IconCatalog.GlyphFor(Icon), CultureInfo.CurrentUICulture, FlowDirection,
        new Typeface(IconCatalog.FontFor(Icon) ?? FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
        FontSize, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
