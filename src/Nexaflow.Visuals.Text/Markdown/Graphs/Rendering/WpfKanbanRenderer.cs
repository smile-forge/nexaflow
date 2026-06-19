using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="KanbanBoard"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Columns lay out left-to-right (horizontally scrollable); each is
/// a header (title + card count) over a vertical stack of cards.  A card shows its text, any
/// metadata chips (ticket, assignee), and a left stripe + label coloured by priority.  Each column
/// takes a colour from the categorical <see cref="MarkdownPalette.Series"/> bank.  Unlike the graph
/// renderers this uses native WPF layout panels (not a measured canvas), so multi-line card text
/// and chip rows wrap for free.
/// </summary>
public static class WpfKanbanRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private static readonly FontFamily MonoFont = new("Cascadia Code, Consolas, Courier New");

    private const double ColumnWidth = 230;

    public static FrameworkElement Render(KanbanBoard board, MarkdownPalette palette)
    {
        if (board.Columns.Count == 0)
            return new TextBlock { Text = "(empty kanban)", Foreground = palette.TextMuted, FontSize = 12 };

        var columns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        for (int i = 0; i < board.Columns.Count; i++)
            columns.Children.Add(BuildColumn(board.Columns[i], palette.Series[i % palette.Series.Count], palette));

        var content = new StackPanel();
        if (!string.IsNullOrWhiteSpace(board.Title))
            content.Children.Add(new TextBlock
            {
                Text = board.Title, Foreground = palette.Heading, FontFamily = BodyFont,
                FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(13, 10, 13, 0),
            });
        content.Children.Add(columns);

        return new Border
        {
            Background = palette.CodeBg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12),
            Child = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                MaxHeight = 600,
            },
        };
    }

    // ── Columns ──────────────────────────────────────────────────────────────

    private static FrameworkElement BuildColumn(KanbanColumn column, Brush accent, MarkdownPalette palette)
    {
        Color ac = (accent as SolidColorBrush)?.Color ?? Colors.SteelBlue;
        var stack = new StackPanel { Width = ColumnWidth, Margin = new Thickness(5, 0, 5, 0) };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = column.Title, Foreground = palette.Text, FontFamily = BodyFont, FontSize = 13,
            FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var count = new TextBlock
        {
            Text = column.Items.Count.ToString(), Foreground = palette.TextMuted, FontFamily = BodyFont,
            FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);

        stack.Children.Add(new Border
        {
            Background = Tint(ac, 0x30), CornerRadius = new CornerRadius(5),
            BorderBrush = accent, BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(9, 6, 9, 6), Margin = new Thickness(0, 0, 0, 6),
            Child = header,
        });

        foreach (var item in column.Items)
            stack.Children.Add(BuildCard(item, accent, palette));

        return stack;
    }

    // ── Cards ────────────────────────────────────────────────────────────────

    private static FrameworkElement BuildCard(KanbanItem item, Brush accent, MarkdownPalette palette)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = item.Text, Foreground = palette.Text, FontFamily = BodyFont, FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
        });

        bool hasMeta = item.Ticket is not null || item.Assigned is not null || item.Priority != KanbanPriority.None;
        if (hasMeta)
        {
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (item.Ticket is not null)
                chips.Children.Add(Chip(item.Ticket, palette.Accent, palette, mono: true));
            if (item.Priority != KanbanPriority.None)
                chips.Children.Add(Chip(PriorityLabel(item.Priority), PriorityBrush(item.Priority, palette), palette, mono: false));
            if (item.Assigned is not null)
                chips.Children.Add(Chip(item.Assigned, palette.TextMuted, palette, mono: false));
            inner.Children.Add(chips);
        }

        Brush stripe = item.Priority != KanbanPriority.None ? PriorityBrush(item.Priority, palette) : accent;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new Border { Background = stripe, CornerRadius = new CornerRadius(4, 0, 0, 4) });
        var body = new Border { Padding = new Thickness(8, 6, 8, 6), Child = inner };
        Grid.SetColumn(body, 1);
        grid.Children.Add(body);

        return new Border
        {
            Background = palette.TableHeaderBg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 0, 6), ClipToBounds = true,
            Child = grid,
        };
    }

    private static FrameworkElement Chip(string text, Brush color, MarkdownPalette palette, bool mono)
    {
        Color c = (color as SolidColorBrush)?.Color ?? Colors.Gray;
        return new Border
        {
            Background = Tint(c, 0x2E), BorderBrush = color, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(0, 2, 4, 0),
            Child = new TextBlock
            {
                Text = text, Foreground = palette.Text, FontSize = 10.5,
                FontFamily = mono ? MonoFont : BodyFont,
            },
        };
    }

    // ── Priority mapping ───────────────────────────────────────────────────────

    private static string PriorityLabel(KanbanPriority p) => p switch
    {
        KanbanPriority.VeryHigh => "Very High",
        KanbanPriority.High     => "High",
        KanbanPriority.Low      => "Low",
        KanbanPriority.VeryLow  => "Very Low",
        _                       => string.Empty,
    };

    private static Brush PriorityBrush(KanbanPriority p, MarkdownPalette palette) => p switch
    {
        KanbanPriority.VeryHigh => palette.Danger,
        KanbanPriority.High     => palette.Warning,
        KanbanPriority.Low      => palette.Accent,
        _                       => palette.TextMuted,   // VeryLow / None
    };

    // ── Colour helper ──────────────────────────────────────────────────────────

    private static Brush Tint(Color c, byte a)
    {
        var b = new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }
}
