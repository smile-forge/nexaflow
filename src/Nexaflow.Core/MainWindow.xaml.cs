using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
using Nexaflow.Features.Common;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;
using WorkContext = Nexaflow.Core.Models.WorkContext;

namespace Nexaflow.Core;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly ShellServices  _shellServices;

    public ShellViewModel ViewModel => _vm;

    public MainWindow(BackgroundActivityManager activityManager, WorkContext workContext,
                      ShellServices shellServices, bool openDefaultTabs = true)
    {
        InitializeComponent();

        _shellServices = shellServices;
        _vm = new ShellViewModel(activityManager, workContext, shellServices)
        {
            Window = this
        };
        DataContext = _vm;

        WireCommands();

        if (openDefaultTabs)
        {
            // Register window before opening tabs so ShellServices can track them
            shellServices.RegisterWindow(_vm);
            shellServices.SetFocused(_vm);

            shellServices.OpenTab("FileSystem", new() { ["mode"] = "thispc" });

            _vm.AddBackgroundTask(new BackgroundTask
            {
                Description = "Indexing workspace…",
                Status      = TaskStatus.Running
            });
        }
        else
        {
            // Tearoff path: register but don't open tabs (ShellServices adds the tab)
            shellServices.RegisterWindow(_vm);
        }

        FinishInit();
    }

    private void WireCommands()
    {
        TabStripControl.TearOffTabCommand = new RelayCommand<TabEntry>(
            tab => _shellServices.TearOffTab(tab!));

        TabStripControl.ReceiveTabCommand = new RelayCommand<TabEntry>(
            tab => _shellServices.MoveTab(tab!, _vm));

        BreadcrumbBarControl.OpenTabRequested += (pageKind, pageParams) =>
            _shellServices.OpenTab(pageKind, pageParams, _vm.CurrentPage as IPageView);
    }

    private void WireOptionsPanel()
    {
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.OptionsOpen) && _vm.OptionsOpen)
                ResetOptionsPanel();
        };
        ResetOptionsPanel();
    }

    private void ResetOptionsPanel()
    {
        var optionsVm = new OptionsViewModel();
        optionsVm.SaveError           += msg   => _vm.ShowErrorToast(msg);
        optionsVm.TabRefreshRequested += kinds => _vm.RefreshTabs(kinds);
        optionsVm.SaveCompleted       += ()    => _vm.OptionsOpen = false;
        OptionsPanelControl.DataContext = optionsVm;
    }

    private void WireManageAiPanel()
    {
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.ManageAiOpen) && _vm.ManageAiOpen)
                ResetManageAiPanel();
        };
        ResetManageAiPanel();
    }

    private void ResetManageAiPanel()
    {
        var manageAiVm = new ManageAiViewModel(_vm.CurrentWorkContext);
        manageAiVm.ApplyError += msg => _vm.ShowErrorToast(msg);
        ManageAiPanelControl.DataContext = manageAiVm;
    }

    private void FinishInit()
    {
        WireOptionsPanel();
        WireManageAiPanel();

        Activated   += (_, _) => _shellServices.SetFocused(_vm);
        Deactivated += (_, _) => _shellServices.ClearFocused(_vm);
        Closing     += (_, _) => _shellServices.UnregisterWindow(_vm);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _vm.OptionsOpen       = false;
                _vm.ManageAiOpen      = false;
                _vm.NotificationsOpen = false;
                _vm.RibbonEditOpen    = false;
            }
        };

        SizeChanged    += (_, _) => CapAiRowHeight();
        RootGrid.LayoutUpdated += (_, _) => CapAiRowHeight();
    }

    private void CapAiRowHeight()
    {
        double maxAi = (ActualHeight - 87) * 0.5;
        if (maxAi < 72) maxAi = 72;
        AiRow.MaxHeight = maxAi;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int dark = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Glyph: maximise (E922) when restored, restore (E923) when maximised.
        MaximizeRestoreButton.Content = ((char)(WindowState == WindowState.Maximized ? 0xE923 : 0xE922)).ToString();
        MaximizeRestoreButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximise";
        // A maximised WindowChrome frame extends past the screen edge and clips
        // content; pad to keep the caption buttons fully visible.
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : default;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e) => ToggleMaxRestore();

    private void OnCloseClick(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void ToggleMaxRestore()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else                                      SystemCommands.MaximizeWindow(this);
    }

    // ── Win11 Snap Layouts ──────────────────────────────────────────────
    // Reporting HTMAXBUTTON for the maximise button's bounds is what makes
    // Windows show the snap-layout flyout on hover. That routes the button to
    // the non-client area, so its hover visual and click are driven from here.
    private const int WM_NCHITTEST     = 0x0084;
    private const int WM_NCMOUSELEAVE  = 0x02A2;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCLBUTTONUP   = 0x00A2;
    private const int HTMAXBUTTON      = 9;

    private static readonly Brush MaxHoverBrush = new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));
    private bool _maxHovered;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                if (IsOverMaxButton(lParam))
                {
                    SetMaxHover(true);
                    handled = true;
                    return (IntPtr)HTMAXBUTTON;
                }
                SetMaxHover(false);
                break;

            case WM_NCMOUSELEAVE:
                SetMaxHover(false);
                break;

            case WM_NCLBUTTONDOWN:
                if (wParam.ToInt32() == HTMAXBUTTON) handled = true; // swallow to suppress system menu
                break;

            case WM_NCLBUTTONUP:
                if (wParam.ToInt32() == HTMAXBUTTON)
                {
                    handled = true;
                    ToggleMaxRestore();
                }
                break;
        }
        return IntPtr.Zero;
    }

    private bool IsOverMaxButton(IntPtr lParam)
    {
        long lp = lParam.ToInt64();
        var screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
        var topLeft = MaximizeRestoreButton.PointToScreen(new Point(0, 0));
        var bottomRight = MaximizeRestoreButton.PointToScreen(
            new Point(MaximizeRestoreButton.ActualWidth, MaximizeRestoreButton.ActualHeight));
        return new Rect(topLeft, bottomRight).Contains(screen);
    }

    private void SetMaxHover(bool on)
    {
        if (_maxHovered == on) return;
        _maxHovered = on;
        MaximizeRestoreButton.Background = on ? MaxHoverBrush : Brushes.Transparent;
        MaximizeRestoreButton.Foreground = (Brush)FindResource(on ? "TextBrush" : "TextMutedBrush");
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private sealed class RelayCommand<T>(Action<T?> execute) : ICommand
    {
        public bool CanExecute(object? p) => true;
        public void Execute(object? p)    => execute(p is T t ? t : default);
        public event EventHandler? CanExecuteChanged;
    }
}
