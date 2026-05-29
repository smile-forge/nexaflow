using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.Tabular.Detection;
using Nexaflow.Features.Tabular.ViewModels;

namespace Nexaflow.Features.Tabular.Views;

public partial class TabularView : UserControl, IPageView
{
    public IPageViewModel? ViewModel { get; }
    public TabularViewModel VM => (TabularViewModel)ViewModel!;

    /// <summary>Separator characters offered in the Split-by submenu. Only those that
    /// appear in the column's sample data are shown at right-click time.</summary>
    private static readonly (string Label, char Char)[] SplitCandidates =
    {
        ("Tab",       '\t'),
        ("Comma",     ','),
        ("Semicolon", ';'),
        ("Pipe",      '|'),
        ("Space",     ' '),
        ("Dash",      '-'),
        ("Slash",     '/'),
        ("Colon",     ':'),
        ("Underscore",'_'),
        ("Period",    '.'),
    };

    public TabularView(TabularViewModel vm)
    {
        InitializeComponent();
        ViewModel   = vm;
        DataContext = vm;

        GridControl.HeaderContextMenuOpening += PopulateHeaderContextMenu;
        GridControl.FocalRowChanged    += focal =>
        {
            if (focal == vm.FocalRow) return;
            vm.FocalRow = focal;
            _ = vm.RefreshWindowAsync();
        };
        GridControl.RowClicked += (row, mods) =>
        {
            bool ctrl  = (mods & ModifierKeys.Control) != 0;
            bool shift = (mods & ModifierKeys.Shift)   != 0;
            vm.HandleRowClick(row.AbsoluteIndex, ctrl, shift);
        };

        vm.LayoutChanged += BuildFilterPanel;
        vm.PropertyChanged += OnVmPropertyChanged;
        vm.Columns.CollectionChanged += (_, _) => SubscribeColumnSelections();
        SubscribeColumnSelections();
        BuildFilterPanel();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabularViewModel.IsFilterPanelOpen))
            BuildFilterPanel();
    }

    private void SubscribeColumnSelections()
    {
        foreach (var c in VM.Columns)
        {
            c.PropertyChanged -= OnColumnSelectionChanged;
            c.PropertyChanged += OnColumnSelectionChanged;
        }
    }

    private void OnColumnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabularColumnViewModel.IsSelected) ||
            e.PropertyName == nameof(TabularColumnViewModel.DisplayType))
            BuildFilterPanel();
    }

    // ── Header context menu ──────────────────────────────────────────────

    private void PopulateHeaderContextMenu(TabularColumnViewModel col, ContextMenu menu)
    {
        // ── Rename column… ──
        var rename = new MenuItem { Header = "Rename column…" };
        rename.Click += (_, _) => RenameColumnPrompt(col);
        menu.Items.Add(rename);

        // ── Merge with previous (only when not the first column) ──
        var merge = new MenuItem
        {
            Header   = "Merge with previous",
            IsEnabled = col.Index > 0,
            ToolTip  = "Treat the separator between this column and the previous as data, not a delimiter.",
        };
        merge.Click += async (_, _) => await VM.MergeWithPreviousAsync(col.Index);
        menu.Items.Add(merge);

        menu.Items.Add(new Separator());

        // ── Split by ▶  ───────────────────────────────────────────────────
        // Always populate the full submenu. Items whose character isn't actually present
        // in the column's sample data are still shown but disabled — the user can see at
        // a glance which separator candidates would (and wouldn't) split the values.
        var split = new MenuItem { Header = "Split by" };
        foreach (var (label, ch) in SplitCandidates)
        {
            bool present = ColumnSampleContains(col, ch);
            var mi = new MenuItem
            {
                Header    = present ? $"{label}  ({Visible(ch)})" : $"{label}  ({Visible(ch)})  — not in sample",
                IsEnabled = present,
            };
            if (present)
            {
                var captured = ch; var idx = col.Index;
                mi.Click += async (_, _) => await VM.SplitColumnByAsync(idx, captured);
            }
            split.Items.Add(mi);
        }
        menu.Items.Add(split);

        // ── Evaluate as ▶ ────────────────────────────────────────────────
        // Always show every type; types whose parser fails on the column's sample data
        // are disabled, except String which is always enabled (any column can be treated
        // as text — useful for switching numeric sort to alphabetical or for filtering
        // a numeric column by prefix).
        var eval = new MenuItem { Header = "Evaluate as" };
        foreach (CsvDataType t in Enum.GetValues(typeof(CsvDataType)))
        {
            bool ok      = t == CsvDataType.String || ColumnParsesAs(col, t);
            bool current = col.DisplayType == t;
            var mi = new MenuItem
            {
                Header      = $"{CsvDataTypeIcons.Glyph(t)}  {t}{(current ? "  ✓" : string.Empty)}",
                IsEnabled   = ok && !current,
                IsCheckable = false,
            };
            if (ok && !current)
            {
                var captured = t; var idx = col.Index;
                mi.Click += async (_, _) => await VM.EvaluateColumnAsAsync(idx, captured);
            }
            eval.Items.Add(mi);
        }
        menu.Items.Add(eval);
        // ContextMenu opens itself — populated synchronously from the Opened callback.
    }

    private void RenameColumnPrompt(TabularColumnViewModel col)
    {
        // IShellServices.ShowPrompt is reached via the same shell that owns this tab —
        // grab it through the VM's constructor injection (private field-style usage isn't
        // exposed, so we use the page services indirectly through OpenBreadcrumb which
        // also takes the shell). Simpler: extend the VM with a rename helper that the
        // shell call funnels through.
        VM.RequestRenameColumn(col.Index);
    }

    private static string Visible(char c) => c switch
    {
        '\t' => "\\t",
        ' '  => "·",
        _    => c.ToString(),
    };

    private static bool ColumnSampleContains(TabularColumnViewModel col, char sep)
    {
        foreach (var s in col.SampleValues)
            if (s.IndexOf(sep) >= 0) return true;
        return false;
    }

    private static bool ColumnParsesAs(TabularColumnViewModel col, CsvDataType target)
    {
        if (col.SampleValues.Count == 0) return target == CsvDataType.String;
        // String is always available; for everything else, every non-empty sample must parse.
        if (target == CsvDataType.String) return true;
        foreach (var s in col.SampleValues)
        {
            var t = ColumnTypeInferrer.ClassifyCell(s);
            if (!Compatible(t, target)) return false;
        }
        return true;
    }

    private static bool Compatible(CsvDataType actual, CsvDataType target) =>
        actual == target ||
        (target == CsvDataType.Decimal  && actual is CsvDataType.Integer or CsvDataType.Currency) ||
        (target == CsvDataType.DateTime && actual is CsvDataType.Date or CsvDataType.Time) ||
        (target == CsvDataType.Currency && actual is CsvDataType.Integer or CsvDataType.Decimal);

    // ── Filter side panel ────────────────────────────────────────────────

    private void BuildFilterPanel()
    {
        if (FiltersHost is null) return;
        FiltersHost.Items.Clear();
        if (VM.Columns is null) return;

        foreach (var col in VM.Columns.Where(c => c.IsSelected))
            FiltersHost.Items.Add(BuildFilterRow(col));

        if (FiltersHost.Items.Count == 0)
            FiltersHost.Items.Add(new TextBlock
            {
                Text       = "Select a column header to filter it.",
                Foreground = (Brush)FindResource("TextDimBrush"),
                FontSize   = 11,
            });
    }

    private FrameworkElement BuildFilterRow(TabularColumnViewModel col)
    {
        var label = new TextBlock
        {
            Text       = $"{CsvDataTypeIcons.Glyph(col.DisplayType)}  {col.Header}",
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize   = 12,
            Margin     = new Thickness(0, 0, 0, 4),
        };
        var body = new StackPanel();
        body.Children.Add(label);
        body.Children.Add(BuildFilterEditor(col));
        body.Children.Add(new Separator
        {
            Background = (Brush)FindResource("BorderBrush"),
            Margin     = new Thickness(0, 8, 0, 8),
        });
        return body;
    }

    private FrameworkElement BuildFilterEditor(TabularColumnViewModel col)
    {
        switch (col.Filter)
        {
            case StringColumnFilter s:
            {
                var tb = new TextBox { Padding = new Thickness(6, 4, 6, 4) };
                tb.SetBinding(TextBox.TextProperty,
                    new Binding(nameof(StringColumnFilter.Text)) { Source = s, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                tb.TextChanged += (_, _) => col.NotifyFilterChanged();
                return tb;
            }
            case NumericColumnFilter n:
            {
                // Labeled Min / Max boxes so the user knows which is which.
                var outer = new StackPanel();
                var labels = new Grid();
                labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                labels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var minLabel = new TextBlock { Text = "Min", FontSize = 10, Foreground = (Brush)FindResource("TextDimBrush"), Margin = new Thickness(2, 0, 0, 2) };
                var maxLabel = new TextBlock { Text = "Max", FontSize = 10, Foreground = (Brush)FindResource("TextDimBrush"), Margin = new Thickness(6, 0, 0, 2) };
                Grid.SetColumn(minLabel, 0); Grid.SetColumn(maxLabel, 1);
                labels.Children.Add(minLabel); labels.Children.Add(maxLabel);

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var min = new TextBox { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 4, 0) };
                var max = new TextBox { Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(4, 0, 0, 0) };
                Grid.SetColumn(min, 0); Grid.SetColumn(max, 1);
                min.LostFocus += (_, _) => { n.Min = ParseDec(min.Text); col.NotifyFilterChanged(); };
                max.LostFocus += (_, _) => { n.Max = ParseDec(max.Text); col.NotifyFilterChanged(); };
                grid.Children.Add(min); grid.Children.Add(max);

                outer.Children.Add(labels);
                outer.Children.Add(grid);
                return outer;
            }
            case DateColumnFilter d:
            {
                // From and To rows each have a date picker + an HH:mm:ss text box so the
                // user can constrain to a sub-day time range (matters for DateTime columns).
                FrameworkElement BuildRangeRow(string label, DateTime? initial, System.Action<DateTime?> apply)
                {
                    var lbl = new TextBlock { Text = label, FontSize = 10, Foreground = (Brush)FindResource("TextDimBrush"), Margin = new Thickness(2, 4, 0, 2) };
                    var dp  = new DatePicker { SelectedDate = initial?.Date };
                    var tb  = new TextBox
                    {
                        Text    = initial?.TimeOfDay.ToString(@"hh\:mm\:ss") ?? string.Empty,
                        Padding = new Thickness(6, 4, 6, 4),
                        Margin  = new Thickness(0, 4, 0, 0),
                        ToolTip = "Optional time of day in HH:mm:ss",
                    };
                    void Refresh()
                    {
                        if (dp.SelectedDate is null) { apply(null); return; }
                        var date = dp.SelectedDate.Value.Date;
                        if (System.TimeSpan.TryParse(tb.Text, System.Globalization.CultureInfo.InvariantCulture, out var ts))
                            apply(date + ts);
                        else
                            apply(date);
                        col.NotifyFilterChanged();
                    }
                    dp.SelectedDateChanged += (_, _) => Refresh();
                    tb.LostFocus           += (_, _) => Refresh();

                    var row = new StackPanel();
                    row.Children.Add(lbl);
                    row.Children.Add(dp);
                    row.Children.Add(tb);
                    return row;
                }

                var stk = new StackPanel();
                stk.Children.Add(BuildRangeRow("From", d.From, v => d.From = v));
                stk.Children.Add(BuildRangeRow("To",   d.To,   v => d.To   = v));
                return stk;
            }
            case BooleanColumnFilter b:
            {
                var stk = new StackPanel { Orientation = Orientation.Horizontal };
                var t = new CheckBox { Content = "true",  IsChecked = b.ShowTrue,  Margin = new Thickness(0, 0, 8, 0),
                                       Foreground = (Brush)FindResource("TextBrush") };
                var f = new CheckBox { Content = "false", IsChecked = b.ShowFalse,
                                       Foreground = (Brush)FindResource("TextBrush") };
                t.Checked   += (_, _) => { b.ShowTrue  = true;  col.NotifyFilterChanged(); };
                t.Unchecked += (_, _) => { b.ShowTrue  = false; col.NotifyFilterChanged(); };
                f.Checked   += (_, _) => { b.ShowFalse = true;  col.NotifyFilterChanged(); };
                f.Unchecked += (_, _) => { b.ShowFalse = false; col.NotifyFilterChanged(); };
                stk.Children.Add(t); stk.Children.Add(f);
                return stk;
            }
        }
        return new TextBlock { Text = "(no filter)", Foreground = (Brush)FindResource("TextDimBrush") };
    }

    private static decimal? ParseDec(string s) =>
        decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        // File path is fixed at tab creation; opening a different file creates a new tab.
    }
}
