using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nexaflow.Features.Code.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Text.Editor;

namespace Nexaflow.Features.Code.Views;

/// <summary>
/// "As Code" page: the editor on the left, a markdown code-intelligence panel on the right, a draggable splitter
/// between, and a collapse/reopen toggle. The panel regenerates (debounced) as the buffer changes, and its links
/// drive navigation — same-file member links scroll the editor; other-file links open that file As Code.
/// </summary>
public partial class CodeView : UserControl, IPageView
{
    private readonly CodeViewModel _vm;
    private readonly DispatcherTimer _genTimer;
    private double _lastPanelWidth = 380;

    IPageViewModel? IPageView.ViewModel => _vm;

    public CodeView(CodeViewModel vm)
    {
        InitializeComponent();

        _vm = vm;

        // Set the link hook before the markdown binding first rebuilds (LinkNavigate is read at build time).
        Md.LinkNavigate  = OnLink;
        Md.BaseDirectory = Path.GetDirectoryName(vm.FilePath);

        DataContext = vm;

        // Host the shared editor directly. It subscribes to the VM's ScrollToLineRequested, so member-link
        // navigation works without this view touching the AvalonEdit control.
        EditorHost.Content = new FileTextEditorView(vm);

        _genTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _genTimer.Tick += OnGenTick;

        vm.Document.TextChanged += OnDocumentTextChanged;
        vm.PropertyChanged      += OnVmPropertyChanged;

        ApplyPanelState();

        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) => RestartGenTimer();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _genTimer.Stop();
        _genTimer.Tick           -= OnGenTick;
        _vm.Document.TextChanged -= OnDocumentTextChanged;
        _vm.PropertyChanged      -= OnVmPropertyChanged;
        // The inner FileTextEditorView owns disposal of the shared view-model (one tab = one VM).
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e) => RestartGenTimer();

    private void RestartGenTimer()
    {
        _genTimer.Stop();
        _genTimer.Start();
    }

    private void OnGenTick(object? sender, EventArgs e)
    {
        _genTimer.Stop();
        _vm.RegenerateIntelligence();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CodeViewModel.PanelVisible)
                           or nameof(CodeViewModel.HasIntelligence)
                           or nameof(CodeViewModel.IsPanelOpen))
            ApplyPanelState();
    }

    private void ApplyPanelState()
    {
        // Remember the user's dragged width before collapsing.
        if (PanelHost.Visibility == Visibility.Visible && PanelColumn.Width.IsAbsolute && PanelColumn.Width.Value > 1)
            _lastPanelWidth = PanelColumn.Width.Value;

        if (_vm.PanelVisible)
        {
            PanelHost.Visibility    = Visibility.Visible;
            Splitter.Visibility     = Visibility.Visible;
            ReopenButton.Visibility = Visibility.Collapsed;
            PanelColumn.Width       = new GridLength(_lastPanelWidth);
        }
        else if (_vm.HasIntelligence)   // collapsed, but a map is available — show the reopen tab
        {
            PanelHost.Visibility    = Visibility.Collapsed;
            Splitter.Visibility     = Visibility.Collapsed;
            ReopenButton.Visibility = Visibility.Visible;
            PanelColumn.Width       = GridLength.Auto;
        }
        else                            // nothing to show — editor fills the width
        {
            PanelHost.Visibility    = Visibility.Collapsed;
            Splitter.Visibility     = Visibility.Collapsed;
            ReopenButton.Visibility = Visibility.Collapsed;
            PanelColumn.Width       = new GridLength(0);
        }
    }

    /// <summary>Handles a clicked intelligence link (a <c>file:</c> URL, optionally with an <c>#ast=</c> member
    /// fragment). Returns true to mark it handled so the renderer doesn't fall back to the OS browser.</summary>
    private bool OnLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsFile) return false;

        string? ast = null;
        const string key = "#ast=";
        if (uri.Fragment.StartsWith(key, StringComparison.Ordinal))
            ast = Uri.UnescapeDataString(uri.Fragment[key.Length..]);

        var target = uri.LocalPath;
        if (ast is not null && PathsEqual(target, _vm.FilePath))
        {
            _vm.NavigateToAst(ast);
            return true;
        }

        _vm.OpenCode(target, ast);
        return true;
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
}
