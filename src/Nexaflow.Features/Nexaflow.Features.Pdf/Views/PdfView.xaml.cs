using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Dependencies;
using Nexaflow.Features.Pdf.Dependencies;
using Nexaflow.Features.Pdf.Models;
using Nexaflow.Features.Pdf.ViewModels;
using Nexaflow.Visuals.Web;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Features.Pdf.Views;

/// <summary>
/// The PDF reader tab: the shared browser surface renders the document (Edge reads PDFs natively, and its
/// own toolbar is left intact — it already carries page selection, search, zoom and print), with Nexaflow's
/// document panel beside it.
/// </summary>
public partial class PdfView : UserControl, IPageView, IAirspaceContent, IDisposable
{
    private readonly IShellServices _shell;

    /// <summary>How long to wait for a navigation to settle before deciding the renderer ignored it.</summary>
    private static readonly TimeSpan NavigationSettle = TimeSpan.FromSeconds(1.5);

    public PdfViewModel ViewModel { get; }

    public PdfView(PdfViewModel viewModel, IShellServices shell)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        _shell      = shell;
        DataContext = viewModel;

        ViewModel.NavigateToPageAsync = GoToPageAsync;
        ViewModel.CapturePageAsync    = ct => Surface.CapturePngAsync(1600, ct);

        Surface.Unavailable += (_, e) => HandleSurfaceUnavailable(e);

        // Collapsing the panel Border alone would leave its 300px column behind as a blank gutter, so the
        // grid columns have to collapse with it.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PdfViewModel.IsPanelOpen)) ApplyPanelWidth();
        };

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again on a re-parent (tab torn off to another window); the surface leaves a live
        // browser alone, so the user's scroll position survives the move.
        if (Surface.IsReady || !ViewModel.SurfaceAvailable) return;

        // Pre-flight: when the shell already knows the runtime is absent, show the fallback now rather than
        // starting a browser that cannot start. Only a definite Missing counts — Unknown means the probe
        // could not decide (or has not run yet), and the right response to that is still to try.
        if (_shell.GetDependencyStatus(WebView2Dependency.DependencyId).State == ExternalDependencyState.Missing)
        {
            ShowRuntimeMissing();
            _pendingPage = null;
            await ViewModel.LoadAsync();
            return;
        }

        if (await Surface.EnsureReadyAsync())
            Surface.NavigateTo(InitialUrl(_pendingPage));

        _pendingPage = null;

        // The panel parse is independent of the renderer and must never gate it — start it after the
        // navigation so the page appears while PdfPig is still working.
        await ViewModel.LoadAsync();
    }

    /// <summary>A page asked for before the surface was ready (an initial "page" param), applied on load.</summary>
    private int? _pendingPage;

    /// <summary>The panel width to restore: whatever the user last dragged the splitter to.</summary>
    private GridLength _panelWidth = new(300);

    /// <summary>
    /// Gives the panel its column back, or takes the column away entirely.
    /// <para>
    /// Hiding the Border is not enough on its own: a collapsed child does not release its grid column, so the
    /// reader would keep a 300px blank gutter where the panel used to be. MinWidth has to go too, or the
    /// column floors at 220 however narrow the width says it is.
    /// </para>
    /// </summary>
    private void ApplyPanelWidth()
    {
        if (ViewModel.IsPanelOpen)
        {
            PanelColumn.MinWidth   = PanelMinWidth;
            PanelColumn.Width      = _panelWidth;
            SplitterColumn.Width   = new GridLength(SplitterWidth);
        }
        else
        {
            _panelWidth            = PanelColumn.Width;   // remember where they had dragged it to
            PanelColumn.MinWidth   = 0;
            PanelColumn.Width      = new GridLength(0);
            SplitterColumn.Width   = new GridLength(0);
        }
    }

    private const double PanelMinWidth = 220;
    private const double SplitterWidth = 4;

    /// <summary>Sets the page the document should open at. Only meaningful before the first navigation.</summary>
    public void OpenAtPage(int page) => _pendingPage = page > 0 ? page : null;
    /// <summary>The URL the document is first loaded from, optionally opening at a page.</summary>
    private string InitialUrl(int? page)
        => page is int p && p > 0
            ? ViewModel.FileUri.AbsoluteUri + PageFragment(p, null)
            : ViewModel.FileUri.AbsoluteUri;


    /// <summary>The fragment alone, without the document URL in front of it.</summary>
    private static string PageFragment(int page, double? offsetFromTop)
    {
        var fragment = $"#page={page}";
        if (offsetFromTop is double y)
            fragment += $"&view=FitH,{y.ToString("0.##", CultureInfo.InvariantCulture)}";
        return fragment;
    }

    /// <summary>
    /// Moves the rendered view to a page, escalating only when it has to.
    /// <para>
    /// The document is almost always already loaded, so the first attempt changes only the fragment from
    /// inside the page — a same-document navigation the renderer handles without re-fetching anything. Only
    /// if the page won't take that do we fall back to a real navigation, which always lands but re-fetches
    /// the document; on a large PDF that is a visible re-render.
    /// </para>
    /// <para>
    /// Returning false — rather than assuming it worked — is what lets the caller degrade honestly when the
    /// renderer ignores page fragments altogether.
    /// </para>
    /// </summary>
    private async Task<bool> GoToPageAsync(int page, double? offsetFromTop, CancellationToken ct)
    {
        if (!Surface.IsReady) return false;

        var fragment = PageFragment(page, offsetFromTop);

        // Only meaningful while our document is the one loaded; otherwise there is nothing to move within.
        if (Surface.CurrentUrl.StartsWith(ViewModel.FileUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
            && await Surface.TrySetFragmentAsync(fragment, ct))
            return true;

        var url = ViewModel.FileUri.AbsoluteUri + fragment;
        if (await Surface.NavigateAndWaitAsync(url, NavigationSettle, ct))
            return true;

        // Nothing settled: the renderer treated the fragment as a no-op even as a navigation.
        if (!await Surface.NavigateAndWaitAsync("about:blank", NavigationSettle, ct))
            return false;

        return await Surface.NavigateAndWaitAsync(url, NavigationSettle, ct);
    }

    /// <summary>
    /// Jumps to the clicked row's destination.
    /// <para>
    /// Wired to the ListBoxItem's button-DOWN via an EventSetter, and deliberately left unhandled: a
    /// ListBoxItem captures the mouse on button-down, so a handler on a child never sees the matching
    /// button-up, and marking down handled would stop the list selecting the row at all.
    /// </para>
    /// <para>
    /// Calls the view-model directly rather than through JumpToCommand. That command is an
    /// <c>AsyncRelayCommand</c>, which refuses to start while a previous run is still going — and a jump runs
    /// for as long as the renderer takes to settle, so a second click during that window was being dropped
    /// rather than queued or superseded.
    /// </para>
    /// <para>
    /// Not driven off selection changing either: the reader may scroll away and want to click the same row
    /// again to come back, which a selection-driven jump would ignore.
    /// </para>
    /// </summary>
    private async void OutlineRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PdfOutlineItem item }) return;

        // A row with no destination is a grouping heading. It still selects and copies; it just has nowhere
        // to go.
        if (item.PageNumber is not int page || !item.CanJump) return;

        await ViewModel.GoToPageAsync(page, CancellationToken.None, item.OffsetFromTop);
    }

    // ── Browser-unavailable fallback ──────────────────────────────────────

    private void HandleSurfaceUnavailable(WebSurfaceUnavailableEventArgs e)
    {
        if (e.RuntimeMissing) { ShowRuntimeMissing(); return; }

        ViewModel.SurfaceAvailable = false;
        ViewModel.RuntimeMissing   = false;
        ViewModel.FailureMessage   =
            "The embedded viewer couldn't be started on this PC. The document's properties and contents "
            + "are still listed beside this panel.";
    }

    /// <summary>
    /// The runtime-missing fallback. Reached two ways — the pre-flight check on load, and a failed browser
    /// start — so the wording lives in one place and both routes offer the same install link.
    /// </summary>
    private void ShowRuntimeMissing()
    {
        ViewModel.SurfaceAvailable = false;
        ViewModel.RuntimeMissing   = true;
        ViewModel.FailureMessage   =
            "The Microsoft Edge WebView2 runtime isn't installed on this PC, so the document can't be "
            + "shown here. Its properties and contents are still listed beside this panel.";
    }

    private void OpenExternal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ViewModel.FileUri.LocalPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _shell.ShowError($"Couldn't open the document externally: {ex.Message}");
        }
    }

    private void InstallRuntime_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { _shell.ShowError($"Couldn't open the download page: {ex.Message}"); }
    }

    // ── IAirspaceContent ──────────────────────────────────────────────────

    /// <summary>
    /// Collapses the renderer while a shell overlay covers the page — its native HWND draws above all WPF
    /// content, so without this the overlay would be hidden behind the document.
    /// </summary>
    void IAirspaceContent.SetCoveredByOverlay(bool covered) => ViewModel.IsCovered = covered;

    // ── IPageView ─────────────────────────────────────────────────────────

    IPageViewModel? IPageView.ViewModel => ViewModel;

    /// <summary>Re-activating the tab with a "page" param jumps to it rather than opening a second tab.</summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (!pageParams.TryGetValue("page", out var raw) || !int.TryParse(raw, out var page) || page < 1)
            return;

        if (Surface.IsReady) _ = ViewModel.GoToPageAsync(page, CancellationToken.None);
        else                 OpenAtPage(page);
    }

    public void Dispose()
    {
        Surface.Dispose();
        ViewModel.Dispose();
    }
}
