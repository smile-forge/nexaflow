using Nexaflow.Features.Common.Viewlets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Features.WindowsFileSystem.Controls;

public partial class ViewletHost : UserControl, IViewletController
{
    private readonly IFolderViewlet _viewlet;
    private ViewletDisplayMode _mode;

    public event Action<ViewletDisplayMode>? ModeChanged;
    public event EventHandler? DisplayModeChanged;
    public event EventHandler? FileViewRequested;
    public event EventHandler? ViewletViewRequested;
    public event EventHandler? SwitchFullViewletRequested;

    /// <summary>Set by the file browser to its folder-wide quiesce fan-out. A viewlet's
    /// <see cref="IViewletController.QuiesceFolderAsync"/> routes here so one viewlet can quiesce them all.</summary>
    public Func<CancellationToken, Task>? QuiesceFolderHandler { get; set; }

    /// <summary>Set by the file browser to its "re-home the tab if the folder vanished" handler. A viewlet's
    /// <see cref="IViewletController.InvalidateLocation"/> routes here.</summary>
    public Action? InvalidateLocationHandler { get; set; }

    public ViewletDisplayMode CurrentMode => _mode;

    /// <summary>The hosted viewlet's AI surface, if its view exposes one — its context line and
    /// tools are merged into the file browser's AI context. Null when the viewlet contributes nothing.</summary>
    public IViewletAiSurface? AiSurface => ViewletContentArea.Content as IViewletAiSurface;

    /// <summary>The hosted viewlet's quiesce surface, if its view exposes one (see
    /// <see cref="IViewletQuiescible"/>). Null when it holds nothing that needs releasing before a mutation.</summary>
    public IViewletQuiescible? Quiescible => ViewletContentArea.Content as IViewletQuiescible;

    Task IViewletController.QuiesceFolderAsync(CancellationToken ct)
        => QuiesceFolderHandler?.Invoke(ct) ?? Task.CompletedTask;

    void IViewletController.InvalidateLocation() => InvalidateLocationHandler?.Invoke();

    public ViewletHost(IFolderViewlet viewlet, string folderPath)
    {
        InitializeComponent();
        _viewlet = viewlet;
        _mode    = viewlet.DefaultDisplayMode;

        DisplayNameText.Text        = viewlet.DisplayName;
        ViewletContentArea.Content  = viewlet.CreateView(folderPath, this);

        ApplyMode();
    }

    // ── IViewletController ────────────────────────────────────────────────

    public void SetDisplayMode(ViewletDisplayMode mode)
    {
        if (!_viewlet.SupportedModes.Contains(mode)) return;
        if (_mode == mode) return;
        _mode = mode;
        ApplyMode();
        ModeChanged?.Invoke(mode);
        DisplayModeChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Banner / switch-button update called by FileSystemView ────────────

    /// <summary>
    /// Sets the label shown on the cycle button (e.g. name of next full viewlet).
    /// Pass null to hide the button.
    /// </summary>
    public void SetSwitchButtonLabel(string? label)
    {
        if (label is null)
        {
            SwitchViewletBtn.Visibility = Visibility.Collapsed;
        }
        else
        {
            SwitchViewletBtn.Content    = label;
            SwitchViewletBtn.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Updates the visual state of the page-toggle buttons (which is "active").
    /// </summary>
    public void SetPageToggleState(bool viewletViewActive)
    {
        // The Viewlet View button is highlighted when that page is active.
        ViewletViewBtn.Opacity = viewletViewActive ? 1.0 : 0.5;
        FileViewBtn.Opacity    = viewletViewActive ? 0.5 : 1.0;
    }

    // ── Mode layout ───────────────────────────────────────────────────────

    private void ApplyMode()
    {
        switch (_mode)
        {
            case ViewletDisplayMode.SingleBar:
                Height                       = 55;
                BannerBorder.Visibility      = Visibility.Collapsed;
                FullModeButtonsPanel.Visibility = Visibility.Collapsed;
                break;

            case ViewletDisplayMode.DoubleBar:
                Height                       = 110;
                BannerBorder.Visibility      = Visibility.Collapsed;
                FullModeButtonsPanel.Visibility = Visibility.Collapsed;
                break;

            case ViewletDisplayMode.Large:
                Height                       = 220;
                BannerBorder.Visibility      = Visibility.Visible;
                FullModeButtonsPanel.Visibility = Visibility.Collapsed;
                break;

            case ViewletDisplayMode.Full:
                Height                       = double.NaN;
                BannerBorder.Visibility      = Visibility.Visible;
                FullModeButtonsPanel.Visibility = Visibility.Visible;
                break;
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void ViewletViewBtn_Click(object sender, RoutedEventArgs e)
        => ViewletViewRequested?.Invoke(this, EventArgs.Empty);

    private void FileViewBtn_Click(object sender, RoutedEventArgs e)
        => FileViewRequested?.Invoke(this, EventArgs.Empty);

    private void SwitchViewletBtn_Click(object sender, RoutedEventArgs e)
        => SwitchFullViewletRequested?.Invoke(this, EventArgs.Empty);
}
