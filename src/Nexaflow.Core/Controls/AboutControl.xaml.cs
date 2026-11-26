using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Media;
using System.Windows.Navigation;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Localization;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Core.Controls;

/// <summary>
/// About page for the Options panel: app version and whether a newer release is offered (with Check for updates /
/// Update), source link, the system components Nexaflow relies on, and the bundled third-party notices (rendered from
/// <c>Assets/ThirdPartyNotices.md</c>). Bound to an <see cref="AboutViewModel"/> the settings panel makes
/// (<see cref="ConfigPageModels"/>), which carries the session's components and its update offer.
/// </summary>
[ViewFor(typeof(AboutViewModel))]
public partial class AboutControl : UserControl
{
    /// <summary>What the page shows while it is on screen: its view-model, taken as it loads.</summary>
    private AboutViewModel? _model;

    /// <summary>The components the page lists while it is on screen: its view-model's.</summary>
    private ExternalDependencyRegistry? _dependencies;

    public AboutControl()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The About page, showing <paramref name="model"/>.</summary>
    internal AboutControl(AboutViewModel model) : this() => DataContext = model;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text  = Str.Format("Shell.About.VersionFormat", App.Version);
        NoticesView.Document = new EditedDocument(LoadNotices());

        _model = DataContext as AboutViewModel;
        Follow(_model?.Dependencies);

        // The startup check, a toast's Update or this page's own buttons can each move it on while the page is open.
        if (_model is { } model)
        {
            model.Updates.Changed -= OnUpdatesChanged;
            model.Updates.Changed += OnUpdatesChanged;
        }
        ShowUpdateStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Follow(null);
        if (_model is { } model) model.Updates.Changed -= OnUpdatesChanged;
        _model = null;
    }

    /// <summary>Lists <paramref name="dependencies"/> and follows its probes, letting go of the one followed before; nothing
    /// for null.</summary>
    private void Follow(ExternalDependencyRegistry? dependencies)
    {
        if (_dependencies is { } followed) followed.Updated -= OnComponentsUpdated;
        _dependencies = dependencies;
        if (dependencies is null) return;

        // The registry re-raises this after every probe, including the ones the Re-check button starts.
        dependencies.Updated += OnComponentsUpdated;
        ShowComponents();
        dependencies.StartProbe();
    }

    private void OnComponentsUpdated(object? sender, EventArgs e) => ShowComponents();

    /// <summary>
    /// Rebuilds the component list from the registry's cached probe. Called on load, and again whenever a
    /// probe finishes — the first one usually completes after this page is already on screen.
    /// </summary>
    private void ShowComponents()
    {
        if (_dependencies is not { } registry) return;
        var rows = registry.Reports.Select(r => new ComponentRow(r, ResolveBrush)).ToList();

        ComponentsList.ItemsSource = rows;

        var missing = registry.Reports.Count(r => r.IsBlocking);
        ComponentsSummary.Text = !registry.IsReady ? Str.Get("Shell.About.Checking")
        : missing > 0       ? Str.Format("Shell.About.MissingFormat", missing)
        : rows.Count > 0    ? Str.Get("Shell.About.AllPresent")
                               : string.Empty;

        // Only force it open for a real problem. Toggling it back closed on a later probe would fight a
        // user who deliberately expanded it, so this never un-checks.
        if (missing > 0) ComponentsToggle.IsChecked = true;

        var empty = registry.IsReady && rows.Count == 0;
        ComponentsEmptyText.Text = empty ? Str.Get("Shell.About.NoComponents") : string.Empty;
        ComponentsEmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;

        RecheckButton.IsEnabled = registry.IsReady;
    }

    /// <summary>
    /// Looks a theme brush up by key, walking to <c>Application.Resources</c> like any
    /// <c>{StaticResource}</c> would. Returns null when the key is absent so the row can fall back rather
    /// than throw — About must still render on a half-built theme.
    /// </summary>
    private Brush? ResolveBrush(string key)
    {
        try { return TryFindResource(key) as Brush ?? Application.Current?.TryFindResource(key) as Brush; }
        catch { return null; }
    }

    private async void OnRecheckClick(object sender, RoutedEventArgs e)
    {
        if (_dependencies is not { } registry) return;
        RecheckButton.IsEnabled = false;
        try { await registry.RefreshAsync(); }
        catch { /* the registry already degrades a failed probe to Unknown; nothing to add here */ }
        finally { ShowComponents(); }
    }

    private void OnUpdatesChanged(object? sender, EventArgs e) => ShowUpdateStatus();

    /// <summary>
    /// Shows where this build stands against its newest release, and offers the one thing to do about it: Update while
    /// a release is offered, Check for updates otherwise — disabled while either is under way.
    /// </summary>
    private void ShowUpdateStatus()
    {
        if (_model is not { } model) return;
        var status = model.Updates.Status;

        UpdateStatusText.Text = status.Stage switch
        {
            AppUpdateStage.Checking      => Str.Get("Shell.About.Updates.Checking"),
            AppUpdateStage.Current       => Str.Get("Shell.About.Updates.Current"),
            AppUpdateStage.Available     => Str.Format("Shell.About.Updates.AvailableFormat", status.Offer?.Version ?? string.Empty),
            AppUpdateStage.CheckFailed   => Str.Get("Shell.About.Updates.CheckFailed"),
            AppUpdateStage.Installing    => Str.Format("Shell.About.Updates.InstallingFormat", status.Offer?.Version ?? string.Empty),
            AppUpdateStage.InstallFailed => Str.Get("Shell.About.Updates.InstallFailed"),
            _                            => Str.Get("Shell.About.Updates.Unchecked"),
        };

        var tone = status.Stage switch
        {
            AppUpdateStage.Available                                => "AccentBrush",
            AppUpdateStage.CheckFailed or AppUpdateStage.InstallFailed => "WarningBrush",
            _                                                       => "TextMutedBrush",
        };
    if (ResolveBrush(tone) is { } brush) UpdateStatusText.Foreground = brush;
        UpdateStatusText.ToolTip = status.Failure;

        InstallUpdateButton.Visibility = status.IsOffered ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled  = status.CanInstall;
        CheckUpdatesButton.Visibility  = status.IsOffered ? Visibility.Collapsed : Visibility.Visible;
        CheckUpdatesButton.IsEnabled   = status.CanCheck;
    }

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_model is { } model) _ = model.Updates.CheckAsync();
    }

    private void OnInstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_model is { } model) _ = model.Updates.InstallAsync();
    }

    private static string LoadNotices()
    {
        try
        {
            var info = Application.GetResourceStream(new Uri("Assets/ThirdPartyNotices.md", UriKind.Relative));
            if (info is null) return string.Empty;
            using var reader = new StreamReader(info.Stream);
            return reader.ReadToEnd();
        }
        catch { return string.Empty; }
    }

    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
    }

    /// <summary>
    /// "Reset Config" — confirms in a window open on the session (its view-model's <see cref="AboutViewModel.Shell"/>), then
    /// wipes all app-data and relaunches.
    /// </summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _model?.Shell?.ShowConfirmation(
            Str.Get("Shell.About.Reset.Ask"),
            Str.Get("Shell.About.Reset.AskBody"),
            App.ResetAndRestart,
            () => { });
    }
}
