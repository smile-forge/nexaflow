using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;
using WorkContext = Nexaflow.Core.Models.WorkContext;

namespace Nexaflow.Core;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private ShellServices _shellServices;
    private SnapLayoutHook _snapHook = null!;

    public ShellViewModel ViewModel => _vm;

    public MainWindow(BackgroundActivityManager activityManager, WorkContext workContext,
                      bool openDefaultTabs = true)
    {
        InitializeComponent();

        _shellServices = workContext.ShellServices!;
        _vm = new ShellViewModel(activityManager, workContext)
        {
            Window = this
        };
        DataContext = _vm;

        if (openDefaultTabs)
        {
            // Register window before opening tabs so ShellServices can track them
            _shellServices.RegisterWindow(_vm);
            _shellServices.SetFocused(_vm);

            _shellServices.OpenTab("FileSystem", new() { ["mode"] = "thispc" });

            _vm.AddBackgroundTask(new BackgroundTask
            {
                Description = "Indexing workspace…",
                Status      = TaskStatus.Running
            });
        }
        else
        {
            // Tearoff path: register but don't open tabs (ShellServices adds the tab)
            _shellServices.RegisterWindow(_vm);
        }

        FinishInit();
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

        _vm.Ribbon = RibbonControl.ViewModel;

        // Keep _shellServices in sync when the user switches WorkContext so that
        // Activated / Deactivated / Closing handlers always reference the live service.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.CurrentWorkContext))
                _shellServices = (ShellServices)_vm.CurrentWorkContext.ShellServices!;
        };

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.PromptVisible) && _vm.PromptVisible)
                Dispatcher.BeginInvoke(() =>
                {
                    PromptTextBox.Focus();
                    PromptTextBox.SelectAll();
                });
        };

        Activated   += (_, _) => _shellServices.SetFocused(_vm);
        Deactivated += (_, _) => _shellServices.ClearFocused(_vm);
        Closing     += (_, _) => _shellServices.UnregisterWindow(_vm);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _vm.OptionsOpen            = false;
                _vm.ManageAiOpen           = false;
                _vm.NotificationsOpen      = false;
                _vm.AiResponseOverlayOpen  = false;
                RibbonControl.ViewModel.IsEditOpen = false;
                if (_vm.ConfirmationVisible)
                    _vm.CancelShellConfirmationCommand.Execute(null);
                if (_vm.PromptVisible)
                    _vm.CancelShellPromptCommand.Execute(null);
            }
        };

        // Ctrl+Tab anywhere in the app focuses the AI input. Plain Tab is left
        // alone so it keeps normal focus navigation, and so the focused input's
        // PlaceholderTextBox can use Tab to accept its inline completion.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Tab
                && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                && !AiInput.IsKeyboardFocusWithin)
            {
                AiInput.Focus();
                e.Handled = true;
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
        NativeMethods.EnableDarkMode(this);
        _snapHook = new SnapLayoutHook(MaximizeRestoreButton, ToggleMaxRestore);
        _snapHook.Install(this);
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

}
