using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.ViewModels;

/// <summary>How the Erase tool marks what to erase.</summary>
public enum EraseMarkWay
{
    /// <summary>A thing picked as the cut-out tool picks it: clicks on it and off it, a box round it, or a phrase.</summary>
    Pick,

    /// <summary>Brushed over: left to mark, right to take marking back.</summary>
    Brush,
}

/// <summary>
/// What the Erase tool has marked so far, before it is erased: the brush strokes, how the place will be filled, and —
/// for the healing brush — where texture is copied from. What is picked is the cut-out tool's (<see cref="PictureCutter"/>),
/// so a thing picked and its shadow brushed over are erased in one step. Strokes are in the photo's pixels as it stands.
/// </summary>
public sealed partial class EraseMarking : ObservableObject
{
    /// <summary>The smallest and largest brush, in the photo's pixels.</summary>
    public const double LeastBrush = 4, MostBrush = 400;

    private readonly List<RecipeEraseStroke> _strokes = [];
    private List<MarkupPoint>? _stroke;
    private bool _strokeMarks;
    private MarkupPoint? _healAnchor;

    /// <param name="aiOffered">Whether picture erasing runs here: what a new photo's fill starts as.</param>
    public EraseMarking(bool aiOffered) => _fill = aiOffered ? EraseFill.Ai : EraseFill.ContentAware;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPicking), nameof(IsBrushing))]
    private EraseMarkWay _way = EraseMarkWay.Pick;

    public bool IsPicking => Way == EraseMarkWay.Pick;

    public bool IsBrushing => Way == EraseMarkWay.Brush;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHealing), nameof(NeedsHealSource))]
    private EraseFill _fill;

    public bool IsHealing => Fill == EraseFill.Heal;

    /// <summary>The healing brush has nowhere to copy from yet.</summary>
    public bool NeedsHealSource => IsHealing && HealSource is null;

    /// <summary>The brush's width, in the photo's pixels.</summary>
    [ObservableProperty] private double _brushSize = 40;

    partial void OnBrushSizeChanged(double value)
    {
        if (value < LeastBrush || value > MostBrush) BrushSize = Math.Clamp(value, LeastBrush, MostBrush);
    }

    /// <summary>Where the healing brush copies texture from, as the first mark after it is set is to it; null until set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsHealSource))]
    private MarkupPoint? _healSource;

    /// <summary>The strokes so far, the one being drawn last.</summary>
    public IReadOnlyList<RecipeEraseStroke> Strokes
        => _stroke is null ? _strokes : [.. _strokes, new RecipeEraseStroke { Points = [.. _stroke], Width = BrushSize, Marks = _strokeMarks }];

    /// <summary>How far each healed pixel copies from, once the source and the first mark after it are set.</summary>
    public MarkupPoint? HealOffset => HealSource is { } source && _healAnchor is { } anchor ? new MarkupPoint(source.X - anchor.X, source.Y - anchor.Y) : null;

    /// <summary>A stroke begins at <paramref name="at"/>: one that marks, or one that takes marking back.</summary>
    public void BeginStroke(MarkupPoint at, bool marks)
    {
        if (marks && HealSource is not null && _healAnchor is null) _healAnchor = at;
        _stroke = [at];
        _strokeMarks = marks;
        OnPropertyChanged(nameof(Strokes));
    }

    /// <summary>The stroke reaches <paramref name="at"/>; a point nearer than <paramref name="step"/> to the last is not kept.</summary>
    public void ContinueStroke(MarkupPoint at, double step)
    {
        if (_stroke is null) return;
        var last = _stroke[^1];
        if (Math.Abs(at.X - last.X) < step && Math.Abs(at.Y - last.Y) < step) return;
        _stroke.Add(at);
        OnPropertyChanged(nameof(Strokes));
    }

    public void EndStroke()
    {
        if (_stroke is null) return;
        _strokes.Add(new RecipeEraseStroke { Points = [.. _stroke], Width = BrushSize, Marks = _strokeMarks });
        _stroke = null;
        OnPropertyChanged(nameof(Strokes));
    }

    /// <summary>The healing brush copies from <paramref name="at"/>, as the next mark is to it.</summary>
    public void SetHealSource(MarkupPoint at)
    {
        HealSource = at;
        _healAnchor = null;
        OnPropertyChanged(nameof(HealOffset));
    }

    /// <summary>
    /// The step that erases what is marked — <paramref name="pick"/>, what the cut-out tool has picked, and the strokes —
    /// filled as chosen; null where nothing is marked, or the healing brush has nowhere to copy from. A pick with no
    /// stroke after the source anchors the healing brush at its first click on the thing, or its box's centre.
    /// </summary>
    public RecipeErase? Step(RecipeCutOut? pick)
    {
        var step = new RecipeErase { Fill = Fill, Pick = pick, Strokes = [.. _strokes] };
        if (!step.MarksAnything) return null;
        if (Fill != EraseFill.Heal) return step;
        if (HealSource is not { } source) return null;
        var anchor = _healAnchor
                     ?? (pick?.Clicks.FirstOrDefault(c => c.Include) is { } click ? new MarkupPoint(click.X, click.Y)
                         : pick?.Box is { } box ? new MarkupPoint((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2)
                         : (MarkupPoint?)null);
        return anchor is { } a ? step with { HealFrom = new MarkupPoint(source.X - a.X, source.Y - a.Y) } : null;
    }

    /// <summary>Every stroke goes, and where the healing brush copies from.</summary>
    public void Clear()
    {
        _strokes.Clear();
        _stroke = null;
        _healAnchor = null;
        HealSource = null;
        OnPropertyChanged(nameof(Strokes));
        OnPropertyChanged(nameof(HealOffset));
    }
}
