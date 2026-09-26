using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Common.Theming;
using Nexaflow.Visuals.Icons;

namespace Nexaflow.Core.ViewModels;

/// <summary>One item on the editor's preview strip: a draft copy of a ribbon item, and the id a journey clicks it by.</summary>
public sealed class RibbonEditorCard(RibbonItem item, int number)
{
    public RibbonItem Item { get; } = item;

    public bool IsSeparator => Item.Kind == RibbonItemKind.Separator;

    /// <summary>By position when the editor opened (new cards continue the count), so it survives renaming.</summary>
    public string AutomationId { get; } = item.Kind == RibbonItemKind.Separator
        ? $"RibbonEditor_Separator{number}"
        : $"RibbonEditor_Card{number}";
}

/// <summary>A shape in the gallery, drawn on a copy of the selected button so each tile shows that button.</summary>
public sealed record RibbonShapeOption(RibbonButtonShape Shape, string Name, RibbonItem Preview)
{
    public string AutomationId => $"RibbonEditor_Shape{Shape}";
}

/// <summary>A choice with its display name: a colour slot, an outline weight, an editor section.</summary>
public sealed record RibbonEditorChoice<T>(T Value, string Name, string AutomationId);

public enum RibbonEditorSection { Icon, Colours, Shape }

/// <summary>
/// The ribbon editor. It works on a deep copy of the live items, so nothing the user does reaches the ribbon until
/// <see cref="DoneCommand"/>, and <see cref="CancelCommand"/> leaves it exactly as it was. Created when the editor
/// opens and dropped when it closes: the ribbon is used constantly and edited rarely, so none of this — nor the
/// pickers the view builds for it — costs anything the rest of the time.
/// </summary>
public sealed partial class RibbonEditorViewModel : ObservableObject
{
    private readonly Func<IEnumerable<RibbonItem>> _loadDefaults;
    private readonly Action<IReadOnlyList<RibbonItem>> _commit;
    private readonly Action _close;
    private readonly Func<object, object?> _findResource;
    private int _nextNumber;

    public RibbonEditorViewModel(
        IEnumerable<RibbonItem> live,
        IEnumerable<RibbonCatalogEntry> pages,
        Func<IEnumerable<RibbonItem>> loadDefaults,
        Action<IReadOnlyList<RibbonItem>> commit,
        Action close,
        Func<object, object?> findResource)
    {
        _loadDefaults = loadDefaults;
        _commit       = commit;
        _close        = close;
        _findResource = findResource;

        foreach (var item in live) Cards.Add(NewCard(item.Clone()));
        foreach (var page in pages) AvailablePages.Add(page);
        _pageToAdd = AvailablePages.FirstOrDefault();

        Slots =
        [
            new(RibbonColourSlot.Foreground, Str.Get("Shell.RibbonEditor.SlotForeground"), "RibbonEditor_SlotForeground"),
            new(RibbonColourSlot.Background, Str.Get("Shell.RibbonEditor.SlotBackground"), "RibbonEditor_SlotBackground"),
            new(RibbonColourSlot.Border,     Str.Get("Shell.RibbonEditor.SlotBorder"),     "RibbonEditor_SlotBorder"),
        ];
        _selectedSlot = Slots[0];

        BorderWeights =
        [
            new(RibbonBorderWeight.None,   Str.Get("Shell.RibbonEditor.WeightNone"),   "RibbonEditor_WeightNone"),
            new(RibbonBorderWeight.Thin,   Str.Get("Shell.RibbonEditor.WeightThin"),   "RibbonEditor_WeightThin"),
            new(RibbonBorderWeight.Medium, Str.Get("Shell.RibbonEditor.WeightMedium"), "RibbonEditor_WeightMedium"),
            new(RibbonBorderWeight.Thick,  Str.Get("Shell.RibbonEditor.WeightThick"),  "RibbonEditor_WeightThick"),
        ];

        Sections =
        [
            new(RibbonEditorSection.Icon,    Str.Get("Shell.RibbonEditor.SectionIcon"),    "RibbonEditor_SectionIcon"),
            new(RibbonEditorSection.Colours, Str.Get("Shell.RibbonEditor.SectionColours"), "RibbonEditor_SectionColours"),
            new(RibbonEditorSection.Shape,   Str.Get("Shell.RibbonEditor.SectionShape"),   "RibbonEditor_SectionShape"),
        ];
        _selectedSection = Sections[0];

        ShapeOptions = RibbonShapeNames.All()
            .Select(s => new RibbonShapeOption(s.Shape, s.Name, new RibbonItem { Shape = s.Shape }))
            .ToList();
    }

    // ── The draft ────────────────────────────────────────────────────────────

    public ObservableCollection<RibbonEditorCard> Cards { get; } = [];

    public ObservableCollection<RibbonCatalogEntry> AvailablePages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsButtonSelected), nameof(IsSeparatorSelected), nameof(SelectedItem))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(ToggleSizeCommand), nameof(MoveLeftCommand),
        nameof(MoveRightCommand), nameof(ResetLookCommand))]
    private RibbonEditorCard? _selected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddPageCommand))]
    private RibbonCatalogEntry? _pageToAdd;

    public RibbonItem? SelectedItem => Selected?.Item;
    public bool HasSelection => Selected is not null;
    public bool IsButtonSelected => Selected is { IsSeparator: false };
    public bool IsSeparatorSelected => Selected is { IsSeparator: true };

    private RibbonEditorCard NewCard(RibbonItem item) => new(item, _nextNumber++);

    partial void OnSelectedChanged(RibbonEditorCard? oldValue, RibbonEditorCard? newValue)
    {
        if (oldValue is not null) oldValue.Item.PropertyChanged -= OnSelectedItemChanged;
        if (newValue is not null) newValue.Item.PropertyChanged += OnSelectedItemChanged;
        RefreshLook();
    }

    private void OnSelectedItemChanged(object? sender, PropertyChangedEventArgs e) => RefreshLook();

    // ── Look: colours, shape, contrast ───────────────────────────────────────

    public IReadOnlyList<RibbonEditorChoice<RibbonColourSlot>> Slots { get; }
    public IReadOnlyList<RibbonEditorChoice<RibbonBorderWeight>> BorderWeights { get; }
    public IReadOnlyList<RibbonEditorChoice<RibbonEditorSection>> Sections { get; }
    public IReadOnlyList<RibbonShapeOption> ShapeOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIconSection), nameof(IsColoursSection), nameof(IsShapeSection))]
    private RibbonEditorChoice<RibbonEditorSection> _selectedSection;

    public bool IsIconSection => SelectedSection.Value == RibbonEditorSection.Icon;
    public bool IsColoursSection => SelectedSection.Value == RibbonEditorSection.Colours;
    public bool IsShapeSection => SelectedSection.Value == RibbonEditorSection.Shape;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedColour), nameof(SlotDefaultColor), nameof(IsBorderSlot), nameof(ColourBaseline))]
    private RibbonEditorChoice<RibbonColourSlot> _selectedSlot;

    public bool IsBorderSlot => SelectedSlot.Value == RibbonColourSlot.Border;

    /// <summary>What "theme default" looks like in the slot being edited.</summary>
    public Color SlotDefaultColor => RibbonColourSlots.ThemeDefault(SelectedSlot.Value, _findResource);

    /// <summary>Changes whenever the picker starts on a different colour, so its before/after preview restarts.</summary>
    public object ColourBaseline => (Selected, SelectedSlot.Value);

    /// <summary>The selected button's colour in the chosen slot — what the colour picker edits.</summary>
    public ColorSpec SelectedColour
    {
        get => SelectedItem is { } item ? SlotValue(item, SelectedSlot.Value) : ColorSpec.Default;
        set
        {
            if (SelectedItem is not { } item || SlotValue(item, SelectedSlot.Value) == value) return;
            switch (SelectedSlot.Value)
            {
                case RibbonColourSlot.Background: item.Background = value; break;
                case RibbonColourSlot.Border:
                    item.BorderColor = value;
                    // An outline colour with no outline changes nothing; the first one picked brings a thin outline.
                    if (!value.IsDefault && item.BorderWeight == RibbonBorderWeight.None)
                        item.BorderWeight = RibbonBorderWeight.Thin;
                    break;
                default: item.Foreground = value; break;
            }
        }
    }

    public RibbonEditorChoice<RibbonBorderWeight>? SelectedBorderWeight
    {
        get => SelectedItem is { } item ? BorderWeights.First(w => w.Value == item.BorderWeight) : null;
        set
        {
            if (value is not null && SelectedItem is { } item) item.BorderWeight = value.Value;
        }
    }

    public RibbonShapeOption? SelectedShape
    {
        get => SelectedItem is { } item ? ShapeOptions.First(o => o.Shape == item.Shape) : null;
        set
        {
            if (value is not null && SelectedItem is { } item) item.Shape = value.Shape;
        }
    }

    /// <summary>Every colour already on the draft ribbon, so a look can be repeated in one click.</summary>
    public IReadOnlyList<ColorSpec> UsedColours => Cards
        .SelectMany(c => new[] { c.Item.Foreground, c.Item.Background, c.Item.BorderColor })
        .Where(c => !c.IsDefault)
        .Distinct()
        .ToList();

    /// <summary>The WCAG contrast of the selected button's icon and label against what they sit on.</summary>
    public double? ContrastRatio
    {
        get
        {
            if (SelectedItem is not { } item) return null;
            var fore = RibbonColourSlots.EffectiveForeground(item, _findResource)
                       ?? RibbonColourSlots.ThemeDefault(RibbonColourSlot.Foreground, _findResource);
            var back = item.Background.Resolve() ?? RibbonColourSlots.ThemeDefault(RibbonColourSlot.Background, _findResource);
            return ColorContrast.Ratio(fore, back);
        }
    }

    public bool IsLowContrast => ContrastRatio < ColorContrast.MinimumForLargeText;

    public string ContrastText => ContrastRatio is not { } ratio
        ? string.Empty
        : IsLowContrast
            ? Str.Format("Shell.RibbonEditor.ContrastLow", ratio)
            : Str.Format("Shell.RibbonEditor.Contrast", ratio);

    public string SizeToggleText => SelectedItem is { IsHalf: true }
        ? Str.Get("Shell.RibbonEditor.MakeFull")
        : Str.Get("Shell.RibbonEditor.MakeCompact");

    private static ColorSpec SlotValue(RibbonItem item, RibbonColourSlot slot) => slot switch
    {
        RibbonColourSlot.Background => item.Background,
        RibbonColourSlot.Border     => item.BorderColor,
        _                           => item.Foreground,
    };

    private void RefreshLook()
    {
        if (SelectedItem is { } item)
            foreach (var option in ShapeOptions)
            {
                option.Preview.Label        = item.Label;
                option.Preview.Icon         = item.Icon;
                option.Preview.Foreground   = item.Foreground;
                option.Preview.Background   = item.Background;
                option.Preview.BorderColor  = item.BorderColor;
                option.Preview.BorderWeight = item.BorderWeight;
            }

        OnPropertyChanged(nameof(SelectedColour));
        OnPropertyChanged(nameof(SelectedBorderWeight));
        OnPropertyChanged(nameof(SelectedShape));
        OnPropertyChanged(nameof(UsedColours));
        OnPropertyChanged(nameof(ContrastRatio));
        OnPropertyChanged(nameof(IsLowContrast));
        OnPropertyChanged(nameof(ContrastText));
        OnPropertyChanged(nameof(SizeToggleText));
        OnPropertyChanged(nameof(ColourBaseline));
    }

    [RelayCommand]
    private void UseColour(ColorSpec colour) => SelectedColour = colour;

    [RelayCommand(CanExecute = nameof(IsButtonSelected))]
    private void ResetLook()
    {
        if (SelectedItem is not { } item) return;
        item.Foreground   = ColorSpec.Default;
        item.Background   = ColorSpec.Default;
        item.BorderColor  = ColorSpec.Default;
        item.BorderWeight = RibbonBorderWeight.None;
        item.Shape        = RibbonButtonShape.Standard;
    }

    // ── Arranging ────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanAddPage))]
    private void AddPage()
    {
        if (PageToAdd is not { } entry) return;
        var card = NewCard(new RibbonItem
        {
            Kind       = RibbonItemKind.Button,
            Label      = entry.Title,
            Icon       = IconRef.Parse(entry.Icon),
            PageKind   = entry.PageKind,
            PageParams = entry.PageParams is null ? null : new(entry.PageParams),
        });
        InsertBeforeSelected(card);

        // Drop the just-added page from the dropdown so it can't be added twice in one session.
        AvailablePages.Remove(entry);
        PageToAdd = AvailablePages.FirstOrDefault();
        Selected  = card;
    }

    private bool CanAddPage() => PageToAdd is not null;

    [RelayCommand]
    private void AddSeparator() => InsertBeforeSelected(NewCard(new RibbonItem { Kind = RibbonItemKind.Separator }));

    /// <summary>A new card goes before the selected one, else at the end.</summary>
    private void InsertBeforeSelected(RibbonEditorCard card)
    {
        var index = Selected is null ? -1 : Cards.IndexOf(Selected);
        if (index >= 0) Cards.Insert(index, card);
        else Cards.Add(card);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        if (Selected is not { } card) return;
        var index = Cards.IndexOf(card);
        Cards.Remove(card);
        Selected = Cards.Count == 0 ? null : Cards[Math.Min(index, Cards.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(IsButtonSelected))]
    private void ToggleSize()
    {
        if (SelectedItem is { } item) item.IsHalf = !item.IsHalf;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveLeft() => MoveBy(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MoveRight() => MoveBy(+1);

    private void MoveBy(int delta)
    {
        if (Selected is not { } card) return;
        var from = Cards.IndexOf(card);
        var to   = Math.Clamp(from + delta, 0, Cards.Count - 1);
        if (to != from) Cards.Move(from, to);
    }

    /// <summary>A drag on the strip: <see cref="ReorderRequest.ToIndex"/> counts the list before the move.</summary>
    [RelayCommand]
    private void Reorder(ReorderRequest request)
    {
        if (request.Item is not RibbonEditorCard card) return;
        var from = Cards.IndexOf(card);
        if (from < 0) return;
        var to = request.ToIndex > from ? request.ToIndex - 1 : request.ToIndex;
        to = Math.Clamp(to, 0, Cards.Count - 1);
        if (to != from) Cards.Move(from, to);
        Selected = card;
    }

    /// <summary>Starts the draft again from the shipped defaults; the ribbon itself is untouched until Done.</summary>
    [RelayCommand]
    private void ResetDefaults()
    {
        Selected = null;
        Cards.Clear();
        _nextNumber = 0;
        foreach (var item in _loadDefaults()) Cards.Add(NewCard(item.Clone()));
        OnPropertyChanged(nameof(UsedColours));
    }

    // ── Leaving ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Done()
    {
        Selected = null;
        _commit(Cards.Select(c => c.Item).ToList());
        _close();
    }

    [RelayCommand]
    private void Cancel()
    {
        Selected = null;
        _close();
    }
}
