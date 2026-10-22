using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Shell;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A file's or a folder's icon in a list row, as Explorer draws it. The row's glyph shows first; the shell's icon replaces
/// it at once when that kind of file has been drawn before, otherwise as soon as the shared shell worker reaches it — which
/// waits for a scroll to settle and draws the rows on screen first. Only a row that is drawn asks, so a virtualised list
/// asks for what is in view; a recycled row asks again under its new path and is never handed its old path's icon.
/// </summary>
public sealed class FileIcon : FrameworkElement
{
    public static readonly DependencyProperty PathProperty = RegisterAsked(nameof(Path), typeof(string), string.Empty);
    public static readonly DependencyProperty IsFolderProperty = RegisterAsked(nameof(IsFolder), typeof(bool), false);
    public static readonly DependencyProperty NameOnlyProperty = RegisterAsked(nameof(NameOnly), typeof(bool), false);
    public static readonly DependencyProperty StampProperty = RegisterAsked(nameof(Stamp), typeof(DateTime), DateTime.MinValue);

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
        nameof(IconSize), typeof(double), typeof(FileIcon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Reask));

    public static readonly DependencyProperty FallbackProperty = DependencyProperty.Register(
        nameof(Fallback), typeof(string), typeof(FileIcon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(FileIcon), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(FileIcon), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The row's path. For a <see cref="NameOnly"/> row, anything ending in its name.</summary>
    public string Path { get => (string)GetValue(PathProperty); set => SetValue(PathProperty, value); }

    public bool IsFolder { get => (bool)GetValue(IsFolderProperty); set => SetValue(IsFolderProperty, value); }

    /// <summary>The row is not on disk — an entry inside an archive or a disk image — so it wears its kind's icon, read from its name.</summary>
    public bool NameOnly { get => (bool)GetValue(NameOnlyProperty); set => SetValue(NameOnlyProperty, value); }

    /// <summary>The row's last-write time: a file that changed (a program rebuilt) is asked about again.</summary>
    public DateTime Stamp { get => (DateTime)GetValue(StampProperty); set => SetValue(StampProperty, value); }

    /// <summary>The icon's edge in device-independent units.</summary>
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

    /// <summary>The glyph shown until the shell's icon arrives, and if it never does. Unset, a folder's or a document's.</summary>
    public string? Fallback { get => (string?)GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    private ImageSource? _icon;
    private IDisposable? _pending;
    private bool _stale = true;

    public FileIcon()
    {
        // A large icon drawn small on a monitor finer than the system's.
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        // Out of the tree, nothing waits on its behalf; back in, it asks again.
        Unloaded += (_, _) => { if (_pending is not null) Forget(); };
        Loaded   += (_, _) => { if (_stale) InvalidateVisual(); };
    }

    private static DependencyProperty RegisterAsked(string name, Type type, object initial) => DependencyProperty.Register(
        name, type, typeof(FileIcon), new FrameworkPropertyMetadata(initial, FrameworkPropertyMetadataOptions.AffectsRender, Reask));

    private static void Reask(DependencyObject owner, DependencyPropertyChangedEventArgs e) => ((FileIcon)owner).Forget();

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        Forget();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(IconSize, IconSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Asked here rather than on each property change: a recycled row changes several at once and asks only once,
        // and a row that is never drawn never asks.
        if (_stale) Ask();

        double size = IconSize;
        if (_icon is not null)
        {
            drawingContext.DrawImage(_icon, new Rect((RenderSize.Width - size) / 2, (RenderSize.Height - size) / 2, size, size));
            return;
        }

        var glyph = new FormattedText(Fallback ?? (IsFolder ? "📁" : "📄"), CultureInfo.CurrentUICulture, FlowDirection,
                                      new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                                      size * 0.8, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(glyph, new Point((RenderSize.Width - glyph.Width) / 2, (RenderSize.Height - glyph.Height) / 2));
    }

    private void Ask()
    {
        _stale = false;
        _icon  = null;
        if (string.IsNullOrEmpty(Path)) return;

        var size   = ShellFileIcons.SizeFor(IconSize * VisualTreeHelper.GetDpi(this).DpiScaleX);
        var lookup = ShellFileIcons.Shared.Request(new FileIconQuery(Path, IsFolder, NameOnly, Stamp.Ticks, size), Dispatcher, Arrived);
        _icon    = lookup.Icon;
        _pending = lookup.Pending;
    }

    private void Arrived(ImageSource icon)
    {
        _pending = null;
        _icon    = icon;
        InvalidateVisual();
    }

    /// <summary>Stops waiting for the icon asked for, and asks afresh on the next drawing.</summary>
    private void Forget()
    {
        _pending?.Dispose();
        _pending = null;
        _stale   = true;
    }
}
