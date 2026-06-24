using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Controls;

/// <summary>
/// Read-only About page for the Options panel: app version, source link and the
/// bundled third-party notices (rendered from <c>Assets/ThirdPartyNotices.md</c>).
/// </summary>
public partial class AboutControl : UserControl
{
    public AboutControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text  = $"Version {SetupWizardViewModel.CurrentVersion()}";
        NoticesView.Markdown = LoadNotices();
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
    /// "Reset Config" — confirms via the shell overlay, then wipes all app-data and relaunches.
    /// Reaches the shell directly (rather than a RelativeSource-bound command) because this control is
    /// hosted in the Options panel's ContentControl with its DataContext set to the config POCO.
    /// </summary>
    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        Nexaflow.Features.Common.IShellServices? shell =
            WorkspaceManager.Instance.FirstActive?.ShellServices;
        shell?.ShowConfirmation(
            "Reset configuration?",
            "This permanently deletes every Nexaflow setting, workspace, provider and conversation " +
            "for all profiles, then restarts the app. This cannot be undone.",
            App.ResetAndRestart,
            () => { });
    }
}
