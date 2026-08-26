using Microsoft.Web.WebView2.Core;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Visuals.Web.Controls;

/// <summary>
/// A chrome-free WebView2 host: start it, navigate it, capture it, script it — and, unlike the
/// code this was extracted from, tear it down.
/// <para>
/// Deliberately has no toolbar, address bar or fallback panel of its own. Those are the *host's*
/// framing: a browser tab wants back/forward and a URL, a document reader wants neither. The host
/// overlays its own fallback panel (keyed off <see cref="IsAvailable"/> or the
/// <see cref="Unavailable"/> event) and collapses this whole control's
/// <see cref="UIElement.Visibility"/> when a shell overlay needs the airspace — the WebView's native
/// HWND draws above all WPF content, so hiding it is the only way an overlay can show.
/// </para>
/// </summary>
public partial class WebSurface : UserControl, IDisposable
{
    private bool _initStarted;
    private bool _disposed;

    public WebSurface()
    {
        InitializeComponent();

        // Hooked on the WPF control rather than on CoreWebView2, so a host that subscribes before the
        // browser has started still hears the very first navigation.
        View.NavigationStarting  += OnViewNavigationStarting;
        View.NavigationCompleted += OnViewNavigationCompleted;

        // The default user-data folder is created next to the executable; under an installed build
        // that's Program Files (read-only for a standard user) and init throws. Pin it to a per-user
        // writable location — LocalAppData, since a browser cache is large and machine-specific.
        UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Smile", "nexaflow", "WebView2");
    }

    // ── Configuration — set before the first EnsureReadyAsync ─────────────

    /// <summary>Per-user writable data folder for the browser's cache and profile.</summary>
    public string UserDataFolder { get; set; }

    // ── State ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The live browser, or null when there is not one — <b>including after disposal</b>, where reading
    /// <c>View.CoreWebView2</c> throws <see cref="ObjectDisposedException"/> rather than answering.
    /// <para>
    /// Every operation below already treats "no CoreWebView2" as "not ready, say so and return", which is the
    /// contract a host depends on: the Web tab and the PDF reader both call in while the browser is still
    /// starting, or on a machine where it never will. A disposed surface is the same situation — a tab closed
    /// while something was still reading it — so it has to answer the same way instead of throwing from the
    /// guard meant to prevent exactly that.
    /// </para>
    /// </summary>
    private Microsoft.Web.WebView2.Core.CoreWebView2? Core => _disposed ? null : View.CoreWebView2;

    /// <summary>True once the underlying CoreWebView2 exists and can be driven.</summary>
    public bool IsReady => Core is not null;

    /// <summary>False once starting the browser has failed on this machine. Never returns to true.</summary>
    public bool IsAvailable { get; private set; } = true;

    /// <summary>True when the failure was specifically a missing Evergreen WebView2 runtime.</summary>
    public bool RuntimeMissing { get; private set; }

    /// <summary>A user-facing sentence for why the browser is unavailable; empty while it is fine.</summary>
    public string FailureMessage { get; private set; } = string.Empty;

    public bool CanGoBack    => IsReady && View.CanGoBack;
    public bool CanGoForward => IsReady && View.CanGoForward;

    /// <summary>The URL currently loaded, as last reported by navigation. Empty before the first one.</summary>
    public string CurrentUrl { get; private set; } = string.Empty;

    public string DocumentTitle => Core?.DocumentTitle ?? string.Empty;

    // No public CoreWebView2 escape hatch. Exposing it would force every consumer — and every test that
    // touches this control — to reference the WebView2 package directly, which is exactly the coupling this
    // extraction exists to remove. Add a wrapped operation here instead when a host needs one.

    // ── Events ────────────────────────────────────────────────────────────

    public event EventHandler<WebSurfaceNavigationEventArgs>? NavigationStarting;
    public event EventHandler<WebSurfaceNavigationEventArgs>? NavigationCompleted;
    public event EventHandler? DocumentTitleChanged;
    public event EventHandler? HistoryChanged;

    /// <summary>Raised once when the browser can't be created here. <see cref="IsAvailable"/> is already false.</summary>
    public event EventHandler<WebSurfaceUnavailableEventArgs>? Unavailable;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the browser if it isn't running. Idempotent and safe to call again after a re-parent
    /// (Loaded fires again when a tab is torn off to another window) — an already-live browser is
    /// left alone so the user's page state survives the move. Returns false when the browser can't
    /// be created, having already raised <see cref="Unavailable"/>.
    /// </summary>
    public async Task<bool> EnsureReadyAsync()
    {
        if (_disposed || !IsAvailable) return false;
        if (Core is not null) return true;
        if (_initStarted) return false;
        _initStarted = true;

        try
        {
            View.CreationProperties = new Microsoft.Web.WebView2.Wpf.CoreWebView2CreationProperties
            {
                UserDataFolder = UserDataFolder,
            };
            await View.EnsureCoreWebView2Async();

            if (_disposed) return false;

            var core = Core
                ?? throw new InvalidOperationException("WebView2 initialised without a CoreWebView2.");

            core.DocumentTitleChanged += OnCoreDocumentTitleChanged;
            core.HistoryChanged       += OnCoreHistoryChanged;
            return true;
        }
        catch (Exception ex)
        {
            HandleUnavailable(ex);
            return false;
        }
        finally
        {
            _initStarted = false;   // allow a future re-parent to re-init if CoreWebView2 was torn down
        }
    }

    /// <summary>
    /// True when this exception, or anything it wraps, is the "runtime isn't installed" one. The chain has
    /// to be walked: WPF surfaces a failure from the WebView2 element through a TargetInvocationException,
    /// and an awaited init can arrive inside an AggregateException. A flat type test silently downgrades
    /// those to the generic message and drops the install link — the one thing the user actually needs.
    /// </summary>
    internal static bool IsRuntimeMissing(Exception? ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is WebView2RuntimeNotFoundException) return true;
            if (e is AggregateException agg
                && agg.InnerExceptions.Any(IsRuntimeMissing)) return true;
        }
        return false;
    }

    private void HandleUnavailable(Exception ex)
    {
        IsAvailable    = false;
        RuntimeMissing = IsRuntimeMissing(ex);
        FailureMessage = RuntimeMissing
            ? "The Microsoft Edge WebView2 runtime isn't installed on this PC, so the page can't be shown here."
            : "The embedded browser couldn't be started on this PC.";

        Unavailable?.Invoke(this, new WebSurfaceUnavailableEventArgs(ex, RuntimeMissing, FailureMessage));
    }

    /// <summary>
    /// Unhooks every handler and disposes the WebView2, which ends its browser process. The code this
    /// was extracted from never did this, so a closed tab left its whole browser running.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (View.CoreWebView2 is { } core)
            {
                core.DocumentTitleChanged -= OnCoreDocumentTitleChanged;
                core.HistoryChanged       -= OnCoreHistoryChanged;
                try { core.Stop(); } catch { /* already gone */ }
            }

            View.NavigationStarting  -= OnViewNavigationStarting;
            View.NavigationCompleted -= OnViewNavigationCompleted;
            View.Dispose();
        }
        catch { /* teardown is best-effort; a half-dead browser must not take the tab down with it */ }

        GC.SuppressFinalize(this);
    }

    // ── Navigation ────────────────────────────────────────────────────────

    /// <summary>Navigates to <paramref name="url"/>. False when the browser isn't ready.</summary>
    public bool NavigateTo(string url)
    {
        if (Core is not { } core) return false;
        core.Navigate(url);
        return true;
    }

    /// <summary>
    /// Navigates and completes when the navigation settles, or false on timeout.
    /// <para>
    /// Waits on <b>both</b> completion signals, because a URL that differs only by its fragment
    /// (<c>…#page=3</c> on the document already loaded) is a <i>same-document</i> navigation: the renderer
    /// scrolls without reloading and <see cref="CoreWebView2.NavigationCompleted"/> is never raised for it.
    /// Waiting on that event alone would time out on a navigation that had in fact already succeeded — and a
    /// caller that escalates on the timeout would then reload a document that never needed reloading.
    /// <see cref="CoreWebView2.SourceChanged"/> is the signal for that case.
    /// </para>
    /// </summary>
    public async Task<bool> NavigateAndWaitAsync(string url, TimeSpan timeout, CancellationToken ct)
    {
        if (Core is not { } core) return false;

        var settled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => settled.TrySetResult(true);
        void OnSourceChanged(object? s, CoreWebView2SourceChangedEventArgs e) => settled.TrySetResult(true);

        core.NavigationCompleted += OnCompleted;
        core.SourceChanged       += OnSourceChanged;
        try
        {
            core.Navigate(url);

            using var timer = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);
            using var _ = linked.Token.Register(() => settled.TrySetResult(false));
            return await settled.Task;
        }
        finally
        {
            core.NavigationCompleted -= OnCompleted;
            core.SourceChanged       -= OnSourceChanged;
        }
    }
    /// <summary>
    /// Changes only the fragment of the page already loaded, and reports whether it took.
    /// <para>
    /// <see cref="NavigateTo"/> is a <i>browser</i>-initiated navigation — the same thing as typing in an
    /// address bar — and re-fetches the document even when only the fragment differs. Assigning
    /// <c>location.hash</c> is a <i>page</i>-initiated same-document navigation: the renderer moves and
    /// nothing is re-fetched.
    /// </para>
    /// <para>
    /// False means the page wouldn't take it — script blocked, not ready, or the hash didn't stick — and the
    /// caller should fall back to a real navigation rather than assume the view moved.
    /// </para>
    /// </summary>
    /// <param name="fragment">The new fragment, with or without its leading <c>#</c>.</param>
    public async Task<bool> TrySetFragmentAsync(string fragment, CancellationToken ct)
    {
        if (Core is null) return false;

        var hash = fragment.StartsWith('#') ? fragment : "#" + fragment;
        var literal = JsonSerializer.Serialize(hash);

        string? json;
        try
        {
            // Assigning the same hash twice is a no-op that raises no event, so it is cleared first —
            // otherwise clicking the same outline row a second time would silently do nothing.
            // The hash the page ends up with is the only honest confirmation available: a blocked or failed
            // script yields null rather than a value that can be checked.
            json = await ExecuteScriptAsync(
                $"(function(){{ if (location.hash === {literal}) {{ history.replaceState(null, '', location.pathname); }} "
                + $"location.hash = {literal}; return location.hash; }})()", ct);
        }
        catch { return false; }

        if (json is null) return false;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.String
                && doc.RootElement.GetString() is { Length: > 1 } applied
                && applied.StartsWith('#');
        }
        catch { return false; }
    }


    public void GoBack()    { if (IsReady) View.GoBack(); }
    public void GoForward() { if (IsReady) View.GoForward(); }
    public void Reload()    { if (IsReady) View.Reload(); }

    private void OnViewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        CurrentUrl = e.Uri;
        NavigationStarting?.Invoke(this, new WebSurfaceNavigationEventArgs(e.Uri));
    }

    private void OnViewNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        CurrentUrl = View.Source?.ToString() ?? CurrentUrl;
        NavigationCompleted?.Invoke(this, new WebSurfaceNavigationEventArgs(CurrentUrl));
    }

    private void OnCoreDocumentTitleChanged(object? sender, object e) => DocumentTitleChanged?.Invoke(this, EventArgs.Empty);
    private void OnCoreHistoryChanged(object? sender, object e)       => HistoryChanged?.Invoke(this, EventArgs.Empty);

    // ── Reading the live page ─────────────────────────────────────────────

    /// <summary>
    /// Captures the current view as a PNG, downscaled to <paramref name="maxEdge"/> on its longest side
    /// so an upload stays cheap. Runs on the UI thread (WebView2 and WPF imaging are both thread-affine);
    /// null when the browser isn't ready.
    /// </summary>
    public async Task<byte[]?> CapturePngAsync(double maxEdge, CancellationToken ct)
    {
        if (Core is not { } core) return null;

        using var raw = new MemoryStream();
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, raw);
        ct.ThrowIfCancellationRequested();
        raw.Position = 0;

        var frame = BitmapFrame.Create(raw, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        var longest = Math.Max(frame.PixelWidth, frame.PixelHeight);
        var scale   = longest > maxEdge ? maxEdge / longest : 1.0;
        BitmapSource source = scale < 1.0
            ? new TransformedBitmap(frame, new ScaleTransform(scale, scale))
            : frame;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var outStream = new MemoryStream();
        encoder.Save(outStream);
        return outStream.ToArray();
    }

    /// <summary>Runs a script in the page and returns its JSON-serialized result; null when not ready.</summary>
    public async Task<string?> ExecuteScriptAsync(string script, CancellationToken ct)
    {
        if (Core is not { } core) return null;
        var result = await core.ExecuteScriptAsync(script);
        ct.ThrowIfCancellationRequested();
        return result;
    }

    /// <summary>Reads the page's current scroll offset and content/viewport heights via JavaScript.</summary>
    public async Task<WebScrollInfo?> GetScrollInfoAsync(CancellationToken ct)
    {
        // ExecuteScriptAsync returns the result JSON-serialized; return a plain object literal.
        var json = await ExecuteScriptAsync(
            "(function(){var d=document.documentElement,b=document.body;" +
            "return {y:window.scrollY," +
            "h:Math.max(d?d.scrollHeight:0,b?b.scrollHeight:0,window.innerHeight)," +
            "v:window.innerHeight};})()", ct);
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return new WebScrollInfo(
                r.GetProperty("y").GetDouble(),
                r.GetProperty("h").GetDouble(),
                r.GetProperty("v").GetDouble());
        }
        catch { return null; }   // page not ready / script blocked — caller proceeds without metrics
    }

    /// <summary>Scrolls by a signed fraction of the viewport height (instant), then lets it settle.</summary>
    public async Task ScrollByViewportFractionAsync(double fraction, CancellationToken ct)
    {
        var f = fraction.ToString(CultureInfo.InvariantCulture);
        if (await ExecuteScriptAsync($"window.scrollBy(0, Math.round(window.innerHeight * ({f})));", ct) is null)
            return;
        await Task.Delay(250, ct);   // let the scroll (and any lazy content) settle before capture
    }
}
