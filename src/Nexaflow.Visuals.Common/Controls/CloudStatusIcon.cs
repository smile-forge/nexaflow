using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Nexaflow.IO.Common.Cloud;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Shell;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A cloud file's sync status in a list row, as Explorer's Status column draws it: a mark for where the file stands (only
/// online, here, kept here, syncing, in error…) and a second when it is shared, named in full by its tooltip. What the
/// folder's listing said shows at once; the sync provider's own word — which alone knows of errors, transfers and sharing —
/// replaces it once read, for the rows on screen only. A recycled row asks afresh and is never handed its old row's answer.
/// </summary>
public sealed class CloudStatusIcon : FrameworkElement
{
    public static readonly DependencyProperty PathProperty = RegisterAsked(nameof(Path), typeof(string), string.Empty);
    public static readonly DependencyProperty ListedProperty = RegisterAsked(nameof(Listed), typeof(CloudFileState), CloudFileState.None);
    public static readonly DependencyProperty RevisionProperty = RegisterAsked(nameof(Revision), typeof(long), 0L);

    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(
        nameof(GlyphSize), typeof(double), typeof(CloudStatusIcon),
        new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(CloudStatusIcon), new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The row's path on disk.</summary>
    public string Path { get => (string)GetValue(PathProperty); set => SetValue(PathProperty, value); }

    /// <summary>What the folder's listing said of the row — shown until, and unless, its provider says more.</summary>
    public CloudFileState Listed { get => (CloudFileState)GetValue(ListedProperty); set => SetValue(ListedProperty, value); }

    /// <summary>Moved on by the row's list when the shell says the file changed, so its provider is asked again.</summary>
    public long Revision { get => (long)GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    /// <summary>Each mark's edge, in device-independent units.</summary>
    public double GlyphSize { get => (double)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }

    /// <summary>The colour of a mark whose theme token is missing.</summary>
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>The marks are Segoe's icon glyphs — Fluent on Windows 11, MDL2 (the same code points) before it.</summary>
    private static readonly Typeface MarkFace = new(new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                                                    FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    /// <summary>Each state's mark, the theme token it is drawn in, and the string that names it.</summary>
    private static readonly Dictionary<CloudFileState, Mark> StateMarks = new()
    {
        [CloudFileState.OnlineOnly]      = new("", "AccentBrush",    "Common.CloudStatus.OnlineOnly"),
        [CloudFileState.Available]       = new("", "SuccessBrush",   "Common.CloudStatus.Available"),
        [CloudFileState.AlwaysAvailable] = new("", "SuccessBrush",   "Common.CloudStatus.AlwaysAvailable"),
        [CloudFileState.SyncPending]     = new("", "AccentBrush",    "Common.CloudStatus.SyncPending"),
        [CloudFileState.PendingUpload]   = new("", "AccentBrush",    "Common.CloudStatus.PendingUpload"),
        [CloudFileState.PendingDownload] = new("", "AccentBrush",    "Common.CloudStatus.PendingDownload"),
        [CloudFileState.Transferring]    = new("", "AccentBrush",    "Common.CloudStatus.Transferring"),
        [CloudFileState.Error]           = new("", "DangerBrush",    "Common.CloudStatus.Error"),
        [CloudFileState.Warning]         = new("", "WarningBrush",   "Common.CloudStatus.Warning"),
        [CloudFileState.Excluded]        = new("", "TextMutedBrush", "Common.CloudStatus.Excluded"),
    };

    private static readonly Dictionary<CloudSharing, Mark> SharingMarks = new()
    {
        [CloudSharing.Shared] = new("", "TextMutedBrush", "Common.CloudStatus.Shared"),
        [CloudSharing.Public] = new("", "TextMutedBrush", "Common.CloudStatus.Public"),
    };

    private CloudFileStatus? _status;
    private IDisposable? _pending;
    private bool _stale = true;

    public CloudStatusIcon()
    {
        // Named when it is about to be shown, from what is drawn then; a row with nothing drawn shows no tooltip.
        ToolTip = string.Empty;

        Unloaded += (_, _) => { if (_pending is not null) Forget(); };
        Loaded   += (_, _) => { if (_stale) InvalidateVisual(); };
    }

    private static DependencyProperty RegisterAsked(string name, Type type, object initial) => DependencyProperty.Register(
        name, type, typeof(CloudStatusIcon), new FrameworkPropertyMetadata(initial, FrameworkPropertyMetadataOptions.AffectsRender, Reask));

    private static void Reask(DependencyObject owner, DependencyPropertyChangedEventArgs e) => ((CloudStatusIcon)owner).Forget();

    protected override Size MeasureOverride(Size availableSize) => new(GlyphSize * 2 + 2, GlyphSize);

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Asked here rather than on each property change: a recycled row changes several at once and asks only once,
        // and a row that is never drawn never asks.
        if (_stale) Ask();

        var (state, sharing) = Shown();
        double x = 0;
        if (StateMarks.TryGetValue(state, out var mark)) x += Draw(drawingContext, mark, x);
        if (SharingMarks.TryGetValue(sharing, out var shared)) Draw(drawingContext, shared, x);
    }

    protected override void OnToolTipOpening(ToolTipEventArgs e)
    {
        var (state, sharing) = Shown();
        var named = new List<string>(2);
        if (StateMarks.TryGetValue(state, out var mark)) named.Add(Str.Get(mark.Name));
        if (SharingMarks.TryGetValue(sharing, out var shared)) named.Add(Str.Get(shared.Name));

        if (named.Count == 0) e.Handled = true;
        else ToolTip = string.Join(" · ", named);
        base.OnToolTipOpening(e);
    }

    /// <summary>What is drawn: the provider's word where it has one, the listing's otherwise.</summary>
    private (CloudFileState State, CloudSharing Sharing) Shown() =>
        _status is { State: not CloudFileState.None } status
            ? (status.State, status.Sharing)
            : (Listed, _status?.Sharing ?? CloudSharing.None);

    private double Draw(DrawingContext drawingContext, Mark mark, double x)
    {
        double size = GlyphSize;
        var glyph = new FormattedText(mark.Glyph, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, MarkFace, size,
                                      TryFindResource(mark.Brush) as Brush ?? Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(glyph, new Point(x + (size - glyph.Width) / 2, (RenderSize.Height - glyph.Height) / 2));
        return size + 2;
    }

    private void Ask()
    {
        _stale  = false;
        _status = null;
        if (string.IsNullOrEmpty(Path)) return;

        var lookup = ShellCloudStatuses.Shared.Request(new CloudStatusQuery(Path, Listed, Revision), Dispatcher, Arrived);
        _status  = lookup.Status;
        _pending = lookup.Pending;
    }

    private void Arrived(CloudFileStatus status)
    {
        _pending = null;
        _status  = status;
        InvalidateVisual();
    }

    /// <summary>Stops waiting for the answer asked for, and asks afresh on the next drawing.</summary>
    private void Forget()
    {
        _pending?.Dispose();
        _pending = null;
        _stale   = true;
    }

    private sealed record Mark(string Glyph, string Brush, string Name);
}
