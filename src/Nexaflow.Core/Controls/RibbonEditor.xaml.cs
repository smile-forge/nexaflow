using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Controls;

public partial class RibbonEditor : UserControl
{
    // ── Dependency properties ─────────────────────────────────────────────

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource),
            typeof(ObservableCollection<RibbonItem>), typeof(RibbonEditor),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty CloseCommandProperty =
        DependencyProperty.Register(nameof(CloseCommand), typeof(ICommand), typeof(RibbonEditor));

    public static readonly DependencyProperty ShellViewModelProperty =
        DependencyProperty.Register(nameof(ShellViewModel), typeof(ShellViewModel), typeof(RibbonEditor));

    public ObservableCollection<RibbonItem>? ItemsSource
    {
        get => (ObservableCollection<RibbonItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }
    public ICommand? CloseCommand
    {
        get => (ICommand?)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }
    public ShellViewModel? ShellViewModel
    {
        get => (ShellViewModel?)GetValue(ShellViewModelProperty);
        set => SetValue(ShellViewModelProperty, value);
    }

    // ── Working draft ─────────────────────────────────────────────────────
    // Edits happen entirely on _draft; changes only go back to ItemsSource on Done.
    private List<RibbonItem> _draft = [];
    // Maps each card border to its draft item — single source of truth; avoids
    // ambiguity with paired compact cards where the outer border's Tag is the
    // top item only.
    private Dictionary<Border, RibbonItem> _cardItem = [];
    private RibbonItem? _selected;

    // ── Drag state ────────────────────────────────────────────────────────
    private Border?     _dragSource;
    private RibbonItem? _dragItem;

    // ── Icon catalogue ────────────────────────────────────────────────────
    private static readonly string[] Icons =
    [
        "🖥","📁","📂","💾","🗂","📄","📝","📋","🗒","🗃",
        "🔍","🔎","🔬","🔭","📡","🛰","🌐","🔗","📎","📌",
        "✂","🖊","🖋","✒","🖌","🖍","📐","📏","📌","📍",
        "💬","🗨","🗯","📢","📣","🔔","🔕","📯","🎵","🎶",
        "⌨","🖱","🖨","📠","📟","📺","📻","🎙","🎚","🎛",
        "⚙","🔧","🔨","🪛","🔩","🪝","🧰","🪜","🧲","💡",
        "🔋","🔌","💻","🖥","📱","⌚","🕹","🎮","🗜","💿",
        "📦","🧳","🗺","🗓","📅","📆","🗑","📤","📥","📬",
        "🏠","🏢","🏗","🏭","🏦","🏥","🏛","🎓","🏆","⭐",
        "❤","💙","💚","💛","🟥","🟦","🟩","🟨","⬛","⬜",
        "➕","➖","✖","➗","🟰","✅","❌","⚠","ℹ","🔒",
        "🔓","👤","👥","🤖","🐛","🦋","🌟","💫","⚡","🔥",
    ];

    // ── Colour swatches ───────────────────────────────────────────────────
    private static readonly string[] Swatches =
    [
        "#E8EAF2","#7880A0","#4A5270",
        "#4F8EF7","#7C5CFC","#22D3A5",
        "#F97316","#EF4444","#EC4899",
        "#FACC15","#84CC16","#06B6D4",
    ];

    public RibbonEditor()
    {
        InitializeComponent();
        // Reinitialise the draft every time the overlay becomes visible
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
                OnShown();
        };
    }

    // ── Show / re-init ────────────────────────────────────────────────────

    /// <summary>Called each time the editor becomes visible so it always reflects
    /// the current live ribbon state, not a stale snapshot from a previous session.</summary>
    private void OnShown()
    {
        InitialiseDraft();
        RebuildCards();
        BuildIconGrid();
        BuildColorSwatches();
        RefreshPropsPanel();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // When ItemsSource is first bound we do an immediate init; subsequent
        // re-inits happen in OnShown (triggered by IsVisibleChanged).
        var ed = (RibbonEditor)d;
        if (ed.IsVisible)
        {
            ed.OnShown();
        }
    }

    private void InitialiseDraft()
    {
        _draft    = [];
        _selected = null;

        foreach (var live in ItemsSource ?? Enumerable.Empty<RibbonItem>())
            _draft.Add(CloneItem(live));
    }

    private static RibbonItem CloneItem(RibbonItem src) => new()
    {
        Kind        = src.Kind,
        Label       = src.Label,
        Icon        = src.Icon,
        IsHalf      = src.IsHalf,
        AccentColor = src.AccentColor,
        PageKind    = src.PageKind,
        PageParams  = src.PageParams is null ? null : new Dictionary<string, string>(src.PageParams),
        TabFactory  = src.TabFactory,
        Command     = src.Command
    };

    // ── Card strip ────────────────────────────────────────────────────────

    private void RebuildCards()
    {
        _cardItem = [];
        CardsPanel.Children.Clear();
        if (_draft.Count == 0) return;

        int i = 0;
        while (i < _draft.Count)
        {
            var item = _draft[i];

            if (item.Kind == RibbonItemKind.Separator)
            {
                CardsPanel.Children.Add(BuildSeparatorCard(item));
                i++;
                continue;
            }

            bool isHalf = item.IsHalf;
            bool nextIsHalf = i + 1 < _draft.Count
                && _draft[i + 1].IsHalf
                && _draft[i + 1].Kind != RibbonItemKind.Separator;

            if (isHalf && nextIsHalf)
            {
                CardsPanel.Children.Add(BuildPairedCard(_draft[i], _draft[i + 1]));
                i += 2;
            }
            else if (isHalf)
            {
                CardsPanel.Children.Add(BuildHalfCard(item));
                i++;
            }
            else
            {
                CardsPanel.Children.Add(BuildFullCard(item));
                i++;
            }
        }

        RefreshSelectedHighlight();
    }

    // Wire drag + click selection onto a card.  item is the primary item the
    // card visually represents.  For paired cards each half row carries its own
    // item separately via half-row click handlers.
    private Border WireCard(Border card, RibbonItem item)
    {
        _cardItem[card] = item;

        card.AllowDrop  = true;
        card.Cursor     = Cursors.SizeAll;
        card.MouseMove += Card_MouseMove;
        card.Drop      += Card_Drop;
        card.DragOver  += Card_DragOver;
        card.DragEnter += Card_DragEnter;
        card.DragLeave += Card_DragLeave;

        card.MouseLeftButtonDown += (_, e) =>
        {
            SelectItem(item);
            e.Handled = true;
        };
        return card;
    }

    private Border BuildFullCard(RibbonItem item)
    {
        var fg = ItemFg(item);
        var content = new StackPanel
        {
            Orientation         = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            IsHitTestVisible    = false   // let the Border receive all mouse events
        };
        content.Children.Add(new TextBlock
        {
            Text                = item.Icon,
            FontSize            = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = fg
        });
        content.Children.Add(new TextBlock
        {
            Text                = item.Label,
            FontSize            = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground          = fg,
            Margin              = new Thickness(0, 3, 0, 0),
            TextWrapping        = TextWrapping.NoWrap
        });

        var card = new Border
        {
            Tag             = item,
            MinWidth        = 54,
            CornerRadius    = new CornerRadius(4),
            Background      = (Brush)FindResource("Surface2Brush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(2, 4, 2, 4),
            Padding         = new Thickness(8, 6, 8, 6),
            Child           = content
        };
        return WireCard(card, item);
    }

    private Border BuildHalfCard(RibbonItem item)
    {
        var row = BuildHalfRow(item, isHitTestTarget: false);

        var card = new Border
        {
            Tag             = item,
            MinWidth        = 80,
            CornerRadius    = new CornerRadius(4),
            Background      = (Brush)FindResource("Surface2Brush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(2, 4, 2, 4),
            Padding         = new Thickness(8, 5, 8, 5),
            VerticalAlignment = VerticalAlignment.Stretch,
            Child           = row
        };
        return WireCard(card, item);
    }

    private Border BuildPairedCard(RibbonItem top, RibbonItem bottom)
    {
        var topRow    = BuildHalfRow(top,    isHitTestTarget: true);
        var bottomRow = BuildHalfRow(bottom, isHitTestTarget: true);

        // Each half row catches its own click to select its own item
        topRow.MouseLeftButtonDown    += (_, e) => { SelectItem(top);    e.Handled = true; };
        bottomRow.MouseLeftButtonDown += (_, e) => { SelectItem(bottom); e.Handled = true; };

        var col = new StackPanel { Orientation = Orientation.Vertical, IsHitTestVisible = true };
        col.Children.Add(topRow);
        col.Children.Add(new Separator
        {
            Margin     = new Thickness(0, 1, 0, 1),
            Background = (Brush)FindResource("BorderBrush"),
            Height     = 1
        });
        col.Children.Add(bottomRow);

        var card = new Border
        {
            Tag             = top,
            MinWidth        = 80,
            CornerRadius    = new CornerRadius(4),
            Background      = (Brush)FindResource("Surface2Brush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(2, 4, 2, 4),
            Padding         = new Thickness(6, 3, 6, 3),
            Child           = col
        };

        // Register in _cardItem for both items pointing at the same card
        // (only the drag-source item is used for reordering; both map here
        //  so RefreshSelectedHighlight can find the card for either item)
        _cardItem[card] = top;          // WireCard will also register top
        // wire the card for drag with the top item as primary
        WireCard(card, top);
        // additionally register bottom so selection highlight works
        _cardItem[card] = top;          // keep top as the drag handle

        // Override the per-card click to do nothing — half rows handle it
        return card;
    }

    private FrameworkElement BuildHalfRow(RibbonItem item, bool isHitTestTarget)
    {
        var fg  = ItemFg(item);
        var row = new StackPanel
        {
            Orientation      = Orientation.Horizontal,
            Margin           = new Thickness(0, 3, 0, 3),
            IsHitTestVisible = isHitTestTarget,
            MinWidth         = 68
        };
        row.Children.Add(new TextBlock
        {
            Text              = item.Icon,
            FontSize          = 13,
            Margin            = new Thickness(0, 0, 6, 0),
            Foreground        = fg,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection     = FlowDirection.LeftToRight
        });
        row.Children.Add(new TextBlock
        {
            Text              = item.Label,
            FontSize          = 10,
            Foreground        = fg,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping      = TextWrapping.NoWrap
        });
        return row;
    }

    private Border BuildSeparatorCard(RibbonItem item)
    {
        var inner = new StackPanel
        {
            VerticalAlignment   = VerticalAlignment.Center,
            IsHitTestVisible    = false
        };
        inner.Children.Add(new TextBlock
        {
            Text                = "│",
            FontSize            = 16,
            Foreground          = (Brush)FindResource("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });
        inner.Children.Add(new TextBlock
        {
            Text                = "sep",
            FontSize            = 9,
            Foreground          = (Brush)FindResource("TextDimBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin              = new Thickness(0, 1, 0, 0)
        });

        var card = new Border
        {
            Tag             = item,
            MinWidth        = 28,
            CornerRadius    = new CornerRadius(4),
            Background      = (Brush)FindResource("Surface2Brush"),
            BorderBrush     = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            Margin          = new Thickness(2, 4, 2, 4),
            Padding         = new Thickness(6, 6, 6, 6),
            Child           = inner
        };
        return WireCard(card, item);
    }

    private Brush ItemFg(RibbonItem item)
    {
        if (item.AccentColor is { Length: > 0 } hex)
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { /* fall through */ }
        }
        return (Brush)FindResource("TextMutedBrush");
    }

    // ── Selection ─────────────────────────────────────────────────────────

    private void SelectItem(RibbonItem item)
    {
        _selected = item;
        RefreshSelectedHighlight();
        RefreshPropsPanel();
    }

    private void RefreshSelectedHighlight()
    {
        foreach (var (card, primaryItem) in _cardItem)
        {
            // A paired card has two items; check if either is selected
            bool isSelected = primaryItem == _selected;
            if (!isSelected && card.Tag is RibbonItem tagItem && tagItem == _selected)
                isSelected = true;
            // Also handle bottom of paired card — find paired card whose bottom item matches
            if (!isSelected)
            {
                int idx = _draft.IndexOf(primaryItem);
                if (idx >= 0 && idx + 1 < _draft.Count && _draft[idx + 1] == _selected
                    && primaryItem.IsHalf && _draft[idx + 1].IsHalf)
                    isSelected = true;
            }

            card.BorderBrush = isSelected
                ? (Brush)FindResource("AccentBrush")
                : (Brush)FindResource("BorderBrush");
        }
    }

    // ── Properties panel ──────────────────────────────────────────────────

    private void RefreshPropsPanel()
    {
        if (_selected is null)
        {
            NoSelectionMsg.Visibility = Visibility.Visible;
            SepProps.Visibility       = Visibility.Collapsed;
            BtnProps.Visibility       = Visibility.Collapsed;
            return;
        }

        if (_selected.Kind == RibbonItemKind.Separator)
        {
            NoSelectionMsg.Visibility = Visibility.Collapsed;
            SepProps.Visibility       = Visibility.Visible;
            BtnProps.Visibility       = Visibility.Collapsed;
            return;
        }

        // Button selected
        NoSelectionMsg.Visibility = Visibility.Collapsed;
        SepProps.Visibility       = Visibility.Collapsed;
        BtnProps.Visibility       = Visibility.Visible;

        SizeToggleBtn.Content = new TextBlock
        {
            Text       = _selected.IsHalf ? "⬆  Full height" : "⬇  Compact",
            FontSize   = 12,
            Foreground = (Brush)FindResource("TextMutedBrush")
        };

        HexInput.Tag  = "loading";
        HexInput.Text = _selected.AccentColor ?? string.Empty;
        HexInput.Tag  = null;

        RefreshIconHighlight(_selected.Icon);
        RefreshSwatchHighlight(_selected.AccentColor);
    }

    private void SizeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _selected.IsHalf = !_selected.IsHalf;
        RebuildCards();
        RefreshPropsPanel();
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _draft.Remove(_selected);
        _selected = null;
        RebuildCards();
        RefreshPropsPanel();
    }

    // ── Icon grid ─────────────────────────────────────────────────────────

    private void BuildIconGrid()
    {
        IconGrid.Children.Clear();
        foreach (var icon in Icons)
        {
            var btn = new Button
            {
                Content             = new TextBlock { Text = icon, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false },
                Style               = (Style)FindResource("TopBarAction"),
                Width               = 34,
                Height              = 34,
                Padding             = new Thickness(0),
                ToolTip             = icon,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Tag                 = icon
            };
            btn.Click += IconBtn_Click;
            IconGrid.Children.Add(btn);
        }
    }

    private void IconBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || _selected.Kind == RibbonItemKind.Separator) return;
        if (sender is not Button btn || btn.Tag is not string icon) return;
        _selected.Icon = icon;
        RebuildCards();
        RefreshIconHighlight(icon);
    }

    private void RefreshIconHighlight(string? currentIcon)
    {
        foreach (Button btn in IconGrid.Children.OfType<Button>())
        {
            bool isCurrent = btn.Tag as string == currentIcon;
            btn.Background   = isCurrent ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
            btn.BorderBrush  = isCurrent ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;
            btn.Foreground   = isCurrent ? (Brush)FindResource("TextBrush")   : (Brush)FindResource("TextMutedBrush");
        }
    }

    // ── Colour swatches ───────────────────────────────────────────────────

    private void BuildColorSwatches()
    {
        ColorSwatches.Children.Clear();
        ColorSwatches.Children.Add(BuildSwatch(null));
        foreach (var hex in Swatches)
            ColorSwatches.Children.Add(BuildSwatch(hex));
    }

    private Border BuildSwatch(string? hex)
    {
        Brush fill = hex is null
            ? (Brush)FindResource("TextMutedBrush")
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        var swatch = new Border
        {
            Width        = 22,
            Height       = 22,
            Margin       = new Thickness(2),
            CornerRadius = new CornerRadius(11),
            Background   = fill,
            Cursor       = Cursors.Hand,
            ToolTip      = hex ?? "Default",
            Tag          = hex
        };
        swatch.MouseLeftButtonDown += (_, _) => ApplyColor(hex);
        return swatch;
    }

    private void ApplyColor(string? hex)
    {
        if (_selected is null || _selected.Kind == RibbonItemKind.Separator) return;
        _selected.AccentColor = hex;
        HexInput.Tag  = "loading";
        HexInput.Text = hex ?? string.Empty;
        HexInput.Tag  = null;
        RebuildCards();
        RefreshSwatchHighlight(hex);
    }

    private void RefreshSwatchHighlight(string? current)
    {
        foreach (Border sw in ColorSwatches.Children.OfType<Border>())
        {
            bool isCurrent = sw.Tag as string == current || (sw.Tag is null && current is null);
            sw.BorderThickness = new Thickness(isCurrent ? 2 : 0);
            sw.BorderBrush     = (Brush)FindResource("AccentBrush");
        }
    }

    private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HexInput.Tag as string == "loading") return;
        if (_selected is null || _selected.Kind == RibbonItemKind.Separator) return;
        var text = HexInput.Text.Trim();
        if (text.Length == 0)
        {
            _selected.AccentColor = null;
            RebuildCards();
            RefreshSwatchHighlight(null);
            return;
        }
        if (!text.StartsWith('#')) text = "#" + text;
        try
        {
            ColorConverter.ConvertFromString(text);
            _selected.AccentColor = text;
            RebuildCards();
            RefreshSwatchHighlight(text);
        }
        catch { /* invalid mid-type — ignore */ }
    }

    // ── Drag-reorder ──────────────────────────────────────────────────────

    private void Card_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not Border b) return;
        if (!_cardItem.TryGetValue(b, out var item)) return;

        _dragSource = b;
        _dragItem   = item;
        DragDrop.DoDragDrop(b, item, DragDropEffects.Move);
    }

    private void Card_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is Border b && b != _dragSource)
            b.Background = (Brush)FindResource("BorderBrush");
    }

    private void Card_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border b)
            b.Background = (Brush)FindResource("Surface2Brush");
    }

    private void Card_DragOver(object sender, DragEventArgs e)
        => e.Effects = DragDropEffects.Move;

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border target || target == _dragSource) return;
        if (_dragItem is null) return;
        if (!_cardItem.TryGetValue(target, out var targetItem)) return;

        int fromIdx = _draft.IndexOf(_dragItem);
        int toIdx   = _draft.IndexOf(targetItem);
        if (fromIdx < 0 || toIdx < 0) return;

        _draft.RemoveAt(fromIdx);
        _draft.Insert(toIdx, _dragItem);

        target.Background = (Brush)FindResource("Surface2Brush");
        RebuildCards();
    }

    // ── Footer handlers ───────────────────────────────────────────────────

    private void AddSeparator_Click(object sender, RoutedEventArgs e)
    {
        var sep = new RibbonItem { Kind = RibbonItemKind.Separator };
        if (_selected is not null)
        {
            int idx = _draft.IndexOf(_selected);
            if (idx >= 0)
            {
                _draft.Insert(idx, sep);
                RebuildCards();
                return;
            }
        }
        _draft.Add(sep);
        RebuildCards();
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        // Rebuild the draft from defaults WITHOUT touching the live ribbon
        // or closing the editor. Changes only apply when the user clicks Done.
        _selected = null;
        _draft    = [];

        if (ShellViewModel is not null)
        {
            foreach (var item in ShellViewModel.BuildDefaultItems())
                _draft.Add(CloneItem(item));
        }

        RebuildCards();
        RefreshPropsPanel();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (ItemsSource is null) return;

        // Commit draft → live collection (edit flag stays true so CollectionChanged
        // does not trigger default-rebuild or a spurious save on the empty state).
        ItemsSource.Clear();
        foreach (var d in _draft)
            ItemsSource.Add(d);

        RibbonLayoutService.Save(ItemsSource);
        CloseCommand?.Execute(null);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => CloseCommand?.Execute(null);
}

