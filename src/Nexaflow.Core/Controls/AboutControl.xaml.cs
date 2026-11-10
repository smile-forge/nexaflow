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
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Core.Controls;

/// <summary>
/// About page for the Options panel: app version and whether a newer release is offered (with Check for updates /
/// Update), source link, the system components Nexaflow relies on, and the bundled third-party notices (rendered from
/// <c>Assets/ThirdPartyNotices.md</c>).
/// </summary>
public partial class AboutControl : UserControl
{
    public AboutControl()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text  = Str.Format("Shell.About.VersionFormat", SetupWizardViewModel.CurrentVersion());
        NoticesView.Document = new EditedDocument(LoadNotices());

        // The registry re-raises this after every probe, including the ones the Re-check button starts.
        ExternalDependencyRegistry.Instance.Updated -= OnComponentsUpdated;
        ExternalDependencyRegistry.Instance.Updated += OnComponentsUpdated;

        ShowComponents();
        ExternalDependencyRegistry.Instance.StartProbe();

        // The startup check, a toast's Update or this page's own buttons can each move it on while the page is open.
        App.Updates.Changed -= OnUpdatesChanged;
        App.Updates.Changed += OnUpdatesChanged;
        ShowUpdateStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ExternalDependencyRegistry.Instance.Updated -= OnComponentsUpdated;
        App.Updates.Changed -= OnUpdatesChanged;
    }

    private void OnComponentsUpdated(object? sender, EventArgs e) => ShowComponents();

    /// <summary>
    /// Rebuilds the component list from the registry's cached probe. Called on load, and again whenever a
    /// probe finishes — the first one usually completes after this page is already on screen.
    /// </summary>
    private void ShowComponents()
    {
        var registry = ExternalDependencyRegistry.Instance;
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
        RecheckButton.IsEnabled = false;
        try { await ExternalDependencyRegistry.Instance.RefreshAsync(); }
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
        var status = App.Updates.Status;

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

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e) => _ = App.Updates.CheckAsync();

    private void OnInstallUpdateClick(object sender, RoutedEventArgs e) => _ = App.Updates.InstallAsync();

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
    /// "Reset Config" — confirms via the shell overlay, then wipes all app-data and relaunches.
    /// Reaches the shell directly (rather than a RelativeSource-bound command) because this control is
    /// hosted in the Options panel's ContentControl with its DataContext set to the config POCO.
    /// </summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        Nexaflow.Features.Common.IShellServices? shell =
            WorkspaceManager.Instance.FirstActive?.ShellServices;
        shell?.ShowConfirmation(
            Str.Get("Shell.About.Reset.Ask"),
            Str.Get("Shell.About.Reset.AskBody"),
            App.ResetAndRestart,
            () => { });
    }
}
