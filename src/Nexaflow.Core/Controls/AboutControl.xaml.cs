using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
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
}
