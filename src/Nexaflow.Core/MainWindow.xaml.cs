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
using Workspace = Nexaflow.Core.Models.Workspace;

namespace Nexaflow.Core;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly ShellServices _shellServices;
    private SnapLayoutHook _snapHook = null!;

    public ShellViewModel ViewModel => _vm;

    public MainWindow(BackgroundActivityManager activityManager, Workspace workspace,
                      bool openDefaultTabs = true)
    {
        InitializeComponent();

        _shellServices = workspace.ShellServices!;
        _vm = new ShellViewModel(activityManager, workspace)
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
        }
        else
        {
            // Tearoff path: register but don't open tabs (ShellServices adds the tab)
            _shellServices.RegisterWindow(_vm);
        }

        FinishInit();
    }

    // Set when Options is reopened to land on a specific tab (e.g. returning from Configure).
    private string? _returnToOptionsSection;

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
        optionsVm.SaveCompleted       += ()    =>
        {
            _vm.OptionsOpen = false;
            // A theme change can't live-reflow (StaticResource by design); restart this window in
            // place, reopening the same tabs against the new theme.
            if (ConfigManager.Instance.GetAll().OfType<ShellConfig>().FirstOrDefault() is { } shell
                && shell.Theme != ThemeManager.Current)
                _shellServices.RestartWindowForTheme(_vm, shell.Theme);
        };

        if (_returnToOptionsSection is not null)
        {
            optionsVm.SelectSection(_returnToOptionsSection);
            _returnToOptionsSection = null;
        }

        OptionsPanelControl.DataContext = optionsVm;
    }

    private ManageAiViewModel? _manageAiVm;

    private void WireManageAiPanel()
    {
        // Built lazily on open — the Configure panel loads config from disk and acquires a provider
        // set, so there's no point doing that work at every window startup.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(ShellViewModel.ManageAiOpen)) return;
            if (_vm.ManageAiOpen) { ResetManageAiPanel(); return; }

            // Closing: apply pending disk changes to the running workspace. If nothing changed and the
            // panel was opened from Options, bounce back to the Options popup — on the Workspaces tab.
            bool changed = _manageAiVm?.Close() ?? false;
            bool returnToOptions = _vm.ConfigureReturnToOptions;
            _vm.ConfigureReturnToOptions = false;
            if (!changed && returnToOptions)
            {
                _returnToOptionsSection = "workcontexts";   // WorkspacesConfig.ConfigName
                _vm.OptionsOpen = true;
            }
        };
    }

    private void ResetManageAiPanel()
    {
        _manageAiVm?.Close();
        var profile = _vm.ConfigureTargetProfile ?? _vm.CurrentWorkspace.Profile;
        var manageAiVm = new ManageAiViewModel(profile);
        manageAiVm.ApplyError += msg => _vm.ShowErrorToast(msg);
        _manageAiVm = manageAiVm;
        ManageAiPanelControl.DataContext = manageAiVm;
    }

    private void FinishInit()
    {
        WireOptionsPanel();
        WireManageAiPanel();

        _vm.Ribbon = RibbonControl.ViewModel;

        // ShellServices is stable for the life of the window: switching profiles reconfigures the
        // Workspace in place (same ShellServices), so no resync is needed here.

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
        Closing     += (_, _) => { _shellServices.UnregisterWindow(_vm); _vm.Detach(); };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _vm.OptionsOpen            = false;
                _vm.ManageAiOpen           = false;
                _vm.NotificationsOpen      = false;
                _vm.Ai.AiResponseOverlayOpen = false;
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

    private void NotificationScrim_MouseDown(object sender, MouseButtonEventArgs e)
        => _vm.NotificationsOpen = false;

    private void CapAiRowHeight()
    {
        double maxAi = (ActualHeight - 91) * 0.5;
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
