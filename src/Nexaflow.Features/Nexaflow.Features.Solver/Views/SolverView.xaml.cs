using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Nexaflow.Features.Common;
using Nexaflow.Features.Solver.Palette;
using Nexaflow.Features.Solver.ViewModels;
using Nexaflow.Visuals.Common.Controls;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Features.Solver.Views;

/// <summary>
/// The Solver tab. Owns the two things a ViewModel cannot: where the caret is when a palette key is
/// pressed, and the navigator's tiles.
/// </summary>
public partial class SolverView : UserControl, IPageView
{
    private readonly SolverViewModel _vm;

    /// <summary>Guards the rail⇄ViewModel round trip while one side is driving the other.</summary>
    private bool _syncingMode;

    /// <summary>Builds the view over its ViewModel.</summary>
    public SolverView(SolverViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        DataContext = vm;

        vm.InsertRequested += OnInsertRequested;
        vm.DefinitionReplaced += OnDefinitionReplaced;
        vm.PropertyChanged += OnViewModelPropertyChanged;

        // Latex typesets as it is typed, so there is no source state to switch to and nothing to do
        // when focus leaves — and the caret is the editor's own business, since it is what "focused
        // and editable" looks like there. Nothing to arrange here at all.

        SymbolNavigator.NodeClicked += (_, id) =>
        {
            _vm.SelectLatexTileCommand.Execute(id);
            RefreshNavigator();
        };
        SymbolNavigator.CentreClicked += (_, _) =>
        {
            _vm.LatexNavigateUpCommand.Execute(null);
            RefreshNavigator();
        };
        RefreshNavigator();

        // The ViewModel is the source of truth for the mode. The rail is re-synced from it on every
        // load, because a TabControl re-selects its first item when it is re-attached to the visual
        // tree — which is exactly what switching to another shell tab and back does. Left two-way
        // bound, that reselection pushes Calc into the ViewModel, which swaps the editor's text
        // buffer out from under whatever was being worked on: the tab appears to reset itself.
        Loaded += (_, _) => { SyncRailFromViewModel(); FocusDefinition(); };
        SyncRailFromViewModel();

        // Deliberately NOT unsubscribed on Unloaded: a tab switch unloads and reloads this view, so
        // dropping the handlers there would quietly break the palette and "use as definition" for
        // the rest of the tab's life. The page's Closed event disposes the ViewModel, and that is
        // what actually ends this view.
    }

    /// <inheritdoc/>
    public IPageViewModel ViewModel => _vm;

    /// <summary>The editor the current tab shows, or null on Calc - which has a plain field instead.</summary>
    private InlineMarkdownEditor? Editor =>
        _vm.IsLatexMode ? LatexInput : _vm.IsTextMode ? ProseInput : null;

    /// <inheritdoc/>
    public void Reinitialize(Dictionary<string, string>? pageParams)
    {
        // The Solver carries no parameters: a re-open is a fresh surface, which is also what makes
        // pinning the tab pin the tab and not the working.
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SolverViewModel.LatexTiles)) { RefreshNavigator(); return; }
        if (e.PropertyName != nameof(SolverViewModel.Mode)) return;
        SyncRailFromViewModel();

        // Picking a tab means "I am going to type here", so the caret belongs in the editor — otherwise
        // the rail keeps the keyboard and the formula shows no caret until the first key finds its way
        // in.
        FocusDefinition();
    }

    /// <summary>
    /// Puts the caret in whichever editor the current tab shows.
    /// <para>
    /// At <see cref="DispatcherPriority.Loaded"/>, which is after layout — the editor's visibility
    /// follows a binding, so anything sooner asks a control that is still collapsed and gives up. That
    /// is why the caret only appeared once something had been typed: the first keystroke was doing the
    /// focusing the tab switch had failed to.
    /// </para>
    /// </summary>
    private void FocusDefinition() => Dispatcher.BeginInvoke(() =>
    {
        // Whichever field the tab shows takes the keyboard, and the caret follows from that — a
        // formula draws its own the moment the editor is focused, so there is nothing further to ask.
        if (_vm.IsCalcMode) CalcInput.Focus();
        else if (Editor is { IsVisible: true } editor) editor.Focus();

        // Background, which runs after everything else queued — including whatever the shell does with
        // focus while it is putting a new tab up. At Loaded this ran first and was overruled, which is
        // why a freshly opened Calc had no caret while switching to it later did.
    }, DispatcherPriority.Background);

    private void SyncRailFromViewModel()
    {
        if (_syncingMode) return;
        _syncingMode = true;
        try
        {
            foreach (var item in ModeRail.Items)
            {
                if (item is not VerticalTabItem tab || !Equals(tab.Tag, _vm.ModeName)) continue;
                ModeRail.SelectedItem = tab;
                break;
            }
        }
        finally { _syncingMode = false; }
    }

    private void OnModeRailSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A TabControl's SelectionChanged bubbles from inner selectors too; only the rail's own
        // selection means the user picked a mode.
        if (_syncingMode || !ReferenceEquals(e.OriginalSource, ModeRail)) return;
        if (ModeRail.SelectedItem is not VerticalTabItem { Tag: string tag }) return;

        _syncingMode = true;
        try { _vm.ModeName = tag; }
        finally { _syncingMode = false; }
    }

    /// <summary>
    /// Applies a palette key to whichever editor is showing, then puts focus back — pressing a key
    /// must never cost you your place in the expression.
    /// </summary>
    private void OnInsertRequested(PaletteKey key)
    {
        if (key.Insert.Length == 0) return;

        if (_vm.IsCalcMode) { ApplyToTextBox(CalcInput, key); return; }
        if (Editor is not { } editor) return;

        // A rendered formula has a caret of its own, so a symbol goes where you are looking — inside
        // the exponent you were editing — rather than becoming a new line under the expression.
        if (key.InsertKind == KeyInsert.Wrapping)
        {
            if (!editor.WrapLatexAtCaret(key.Insert, key.Close))
                // The braces are all it takes: an argument left empty becomes a hole when it is parsed,
                // and the hole draws itself. Nothing is written that the reader did not ask for.
                editor.InsertMarkdownAtCaret(key.Insert + key.Close);
        }
        else if (!editor.InsertLatexAtCaret(key.Insert, key.CaretBack))
        {
            editor.InsertMarkdownAtCaret(key.Insert);
        }

        editor.Focus();
    }

    /// <summary>
    /// The whole of what makes a keypad feel right: a function brackets what you have selected
    /// rather than deleting it, a postfix operator follows a number rather than replacing it, and
    /// with nothing selected a bracketing key leaves a selected placeholder so the next keystroke
    /// simply types the argument.
    /// </summary>
    private static void ApplyToTextBox(TextBox box, PaletteKey key)
    {
        var text = box.Text;
        var at = box.SelectionStart;
        var len = box.SelectionLength;

        switch (key.InsertKind)
        {
            case KeyInsert.Wrapping:
            {
                var inner = len > 0 ? text.Substring(at, len) : PaletteText.Placeholder;
                var built = key.Insert + inner + key.Close;
                box.Text = text.Remove(at, len).Insert(at, built);

                if (len > 0) box.CaretIndex = at + built.Length;
                else box.Select(at + key.Insert.Length, inner.Length);   // placeholder, ready to type over
                break;
            }

            case KeyInsert.Postfix:
            {
                // Nothing to apply it to — at the start, or straight after an operator or a space.
                if (!FollowsAnOperand(text, at + len)) return;
                var pos = at + len;
                box.Text = text.Insert(pos, key.Insert);
                box.CaretIndex = pos + key.Insert.Length;
                break;
            }

            default:
            {
                box.Text = text.Remove(at, len).Insert(at, key.Insert);
                box.CaretIndex = Math.Max(0, at + key.Insert.Length - key.CaretBack);
                break;
            }
        }

        box.Focus();
    }

    /// <summary>Whether the character before <paramref name="index"/> can be squared, factorialised or similar.</summary>
    private static bool FollowsAnOperand(string text, int index)
    {
        if (index <= 0 || index > text.Length) return false;
        var c = text[index - 1];
        return !char.IsWhiteSpace(c) && c is not ('+' or '-' or '*' or '/' or '^' or '(' or ',' or '%');
    }

    /// <summary>
    /// Parks the caret at the end after a wholesale replacement — clear, backspace, "use as
    /// definition", or the AI setting it. The text itself already flows through the bindings.
    /// </summary>
    private void OnDefinitionReplaced(string text)
    {
        if (_vm.IsCalcMode) { CalcInput.CaretIndex = CalcInput.Text.Length; return; }

        // Handed over directly rather than left to the binding: a wholesale replacement is exactly the
        // case a two-way binding cannot express, since the editor is usually the thing that last wrote
        // the value and the property has not moved as far as the binding is concerned. The caret is
        // left where the replacement put it — taking it back would fight the user for it.
        if (Editor is { } editor && editor.Markdown != text) editor.Markdown = text;
    }

    /// <summary>
    /// Rebuilds the navigator's tiles for wherever the ViewModel has navigated to.
    /// <para>
    /// Colours come from the shell's categorical <c>Swatch.*</c> bank rather than being chosen here,
    /// so the palette re-tints with the theme along with everything else. A tile keeps the same
    /// swatch at the same position on every level, which is what lets the ring be read by position
    /// once you know it.
    /// </para>
    /// </summary>
    private void RefreshNavigator()
    {
        string[] swatches =
        [
            "Swatch.Purple", "Swatch.Blue", "Swatch.Green", "Swatch.Cyan",
            "Swatch.Amber", "Swatch.Orange", "Swatch.Teal", "Swatch.Pink",
        ];

        var positions = _vm.LatexTiles;
        var tiles = new List<OctagonNode?>(OctagonNavigator.MaxNodes);
        for (var i = 0; i < positions.Count && i < OctagonNavigator.MaxNodes; i++)
            tiles.Add(positions[i].IsGap
                ? null
                : new OctagonNode(positions[i].Id, positions[i].Label, positions[i].Tooltip,
                                  Swatch(swatches[i % swatches.Length]), positions[i].Opens));

        SymbolNavigator.Nodes = tiles;
        SymbolNavigator.CentreLabel = _vm.LatexCentreLabel;
        SymbolNavigator.CanGoUp = _vm.CanGoUpLatex;
    }

    private static Brush Swatch(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
}
