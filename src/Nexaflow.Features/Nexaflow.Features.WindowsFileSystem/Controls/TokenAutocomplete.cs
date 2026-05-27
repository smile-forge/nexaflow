using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.WindowsFileSystem.Controls;

/// <summary>
/// Attached behaviour for a <see cref="TextBox"/> that pops up a filter-as-you-type
/// suggestion list when the user types <c>#</c> (file tokens) or <c>%</c>
/// (environment variable names). Tab/Enter completes the selection, Esc cancels,
/// Up/Down navigates.
/// </summary>
public static class TokenAutocomplete
{
    private static readonly IReadOnlyList<string> FileTokens =
        ["file", "filenoext", "filepath", "pathonly"];

    // Cached env-var list (process-scope is fine).
    private static readonly Lazy<IReadOnlyList<string>> EnvVars = new(() =>
    {
        var dict = Environment.GetEnvironmentVariables();
        var names = new List<string>(dict.Count);
        foreach (DictionaryEntry de in dict)
            if (de.Key is string s) names.Add(s);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    });

    // ── Attached property ────────────────────────────────────────────────────

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool),
            typeof(TokenAutocomplete),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;
        if ((bool)e.NewValue) Attach(tb);
        else                  Detach(tb);
    }

    // ── State per textbox ────────────────────────────────────────────────────

    private sealed class State
    {
        public Popup    Popup    = new();
        public ListBox  List     = new();
        public char     Trigger;     // '#' or '%'
        public int      Anchor;      // caret position immediately after trigger
        public bool     Open;
    }

    private static readonly Dictionary<TextBox, State> _states = new();

    // ── Wiring ───────────────────────────────────────────────────────────────

    private static void Attach(TextBox tb)
    {
        if (_states.ContainsKey(tb)) return;

        var st = new State();
        st.List.MinWidth = 180;
        st.List.MaxHeight = 220;
        st.List.Background = SystemColors.ControlBrush;
        st.List.BorderThickness = new Thickness(1);
        st.List.BorderBrush = SystemColors.ActiveBorderBrush;

        st.Popup.PlacementTarget = tb;
        st.Popup.Placement       = PlacementMode.Bottom;
        st.Popup.StaysOpen       = false;
        st.Popup.AllowsTransparency = true;
        st.Popup.Child = st.List;

        _states[tb] = st;

        tb.TextChanged    += OnTextChanged;
        tb.PreviewKeyDown += OnPreviewKeyDown;
        tb.LostFocus      += (_, _) => Close(tb);
        st.List.PreviewMouseLeftButtonUp += (_, _) => Commit(tb);
    }

    private static void Detach(TextBox tb)
    {
        if (!_states.TryGetValue(tb, out var st)) return;
        tb.TextChanged    -= OnTextChanged;
        tb.PreviewKeyDown -= OnPreviewKeyDown;
        st.Popup.IsOpen = false;
        _states.Remove(tb);
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (!_states.TryGetValue(tb, out var st)) return;

        int caret = tb.CaretIndex;
        string text = tb.Text;

        // Detect a fresh trigger: the char immediately before the caret is # or %
        // and we are not already tracking from a different anchor.
        if (!st.Open && caret > 0)
        {
            char c = text[caret - 1];
            if (c == '#' || c == '%')
            {
                Open(tb, st, c, caret);
                return;
            }
        }

        if (st.Open)
        {
            // If caret moved before the anchor (e.g. user pressed Home), close.
            if (caret < st.Anchor) { Close(tb); return; }

            // Re-filter based on the prefix from anchor → caret.
            var prefix = text.Substring(st.Anchor, caret - st.Anchor);

            // A non-alphanumeric/non-bracket character closes the popup.
            if (prefix.Length > 0 && !IsTokenChar(prefix[^1], st.Trigger))
            {
                Close(tb);
                return;
            }

            UpdateFilter(st, prefix);
            if (st.List.Items.Count == 0) Close(tb);
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb) return;
        if (!_states.TryGetValue(tb, out var st) || !st.Open) return;

        switch (e.Key)
        {
            case Key.Down:
                if (st.List.Items.Count > 0)
                    st.List.SelectedIndex = Math.Min(st.List.SelectedIndex + 1, st.List.Items.Count - 1);
                e.Handled = true;
                break;
            case Key.Up:
                if (st.List.Items.Count > 0)
                    st.List.SelectedIndex = Math.Max(st.List.SelectedIndex - 1, 0);
                e.Handled = true;
                break;
            case Key.Tab:
            case Key.Enter:
                Commit(tb);
                e.Handled = true;
                break;
            case Key.Escape:
                Close(tb);
                e.Handled = true;
                break;
        }
    }

    // ── Popup lifecycle ──────────────────────────────────────────────────────

    private static void Open(TextBox tb, State st, char trigger, int anchor)
    {
        st.Trigger = trigger;
        st.Anchor  = anchor;
        st.Open    = true;
        UpdateFilter(st, string.Empty);
        if (st.List.Items.Count == 0) { st.Open = false; return; }

        // Anchor the popup horizontally near the caret.
        var rect = tb.GetRectFromCharacterIndex(anchor);
        st.Popup.HorizontalOffset = rect.Left;
        st.Popup.IsOpen = true;
    }

    private static void Close(TextBox tb)
    {
        if (!_states.TryGetValue(tb, out var st)) return;
        st.Open = false;
        st.Popup.IsOpen = false;
    }

    private static void UpdateFilter(State st, string prefix)
    {
        IEnumerable<string> source = st.Trigger == '#' ? FileTokens : EnvVars.Value;
        var matched = string.IsNullOrEmpty(prefix)
            ? source
            : source.Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        st.List.ItemsSource = matched.ToList();
        if (st.List.Items.Count > 0) st.List.SelectedIndex = 0;
    }

    private static void Commit(TextBox tb)
    {
        if (!_states.TryGetValue(tb, out var st) || !st.Open) return;
        if (st.List.SelectedItem is not string chosen) { Close(tb); return; }

        // Replace [anchor … caret) with the chosen completion.
        int caret  = tb.CaretIndex;
        int from   = st.Anchor;
        int length = caret - from;
        if (length < 0) { Close(tb); return; }

        // Decide what trailing chars to append: # tokens optionally take [N] — keep cursor between brackets.
        string insertion = chosen;
        int    caretOffset = insertion.Length;

        tb.Text = tb.Text.Remove(from, length).Insert(from, insertion);
        tb.CaretIndex = from + caretOffset;
        Close(tb);
        tb.Focus();
    }

    private static bool IsTokenChar(char c, char trigger)
    {
        if (char.IsLetterOrDigit(c)) return true;
        if (trigger == '#' && (c == '[' || c == ']')) return true;
        if (trigger == '%' && (c == '_' || c == '(' || c == ')')) return true;
        return false;
    }
}
