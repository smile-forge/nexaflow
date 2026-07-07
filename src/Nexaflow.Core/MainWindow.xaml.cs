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
using WorkspaceRuntime = Nexaflow.Core.Models.WorkspaceRuntime;

namespace Nexaflow.Core;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;
    private readonly ShellServices _shellServices;
    private SnapLayoutHook _snapHook = null!;

    public ShellViewModel ViewModel => _vm;

    public MainWindow(BackgroundActivityManager activityManager, WorkspaceRuntime workspace,
                      bool openDefaultTabs = true)
    {
        Services.StartupTimings.Mark("MainWindow.ctor enter");
        InitializeComponent();
        Services.StartupTimings.Mark("MainWindow.InitializeComponent");

        _shellServices = workspace.ShellServices!;
        _vm = new ShellViewModel(activityManager, workspace)
        {
            Window = this
        };
        DataContext = _vm;
        Services.StartupTimings.Mark("MainWindow.ShellViewModel");

        if (openDefaultTabs)
        {
            // Register window before opening tabs so ShellServices can track them
            _shellServices.RegisterWindow(_vm);
            _shellServices.SetFocused(_vm);

            _shellServices.OpenDefaultTabs(workspace.Profile.DefaultTabs);

            // Ribbon-independent deep-link: --openTab <PageKind> opens that page too (used by UI tests to
            // reach views that aren't on the default ribbon).
            if (App.OpenTabKind is { Length: > 0 } openKind)
                _shellServices.OpenTab(openKind, new());

            // Startup profiling: the first window's first render is "time to first window". In timing mode
            // we report and then shut down so a harness can cold-start the process repeatedly.
            if (StartupTimings.Enabled)
                ContentRendered += (_, _) =>
                {
                    StartupTimings.Report();
                    Application.Current.Shutdown();
                };
        }
        else
        {
            // Tearoff path: register but don't open tabs (ShellServices adds the tab)
            _shellServices.RegisterWindow(_vm);
        }

        FinishInit();
    }

    // Backdrop-click on the overlay host closes only overlays that opt in (feature overlays via
    // IShellOverlay.DismissOnBackdrop); the built-in modals dismiss via their own buttons / Escape.
    private void OverlayBackdrop_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_vm.ActiveOverlay is IShellOverlay { DismissOnBackdrop: true })
            _vm.CloseOverlay();
    }

    private void FinishInit()
    {
        // Overlay content (Options / Manage-AI / confirmation / prompt) is built lazily in ShellViewModel
        // when the corresponding flag flips, and rendered by the OverlayHost's DataTemplates — no per-panel
        // wiring is needed here any more. Prompt-textbox autofocus lives in the template (FocusOnLoad).

        _vm.Ribbon = RibbonControl.ViewModel;

        // ShellServices is stable for the life of the window: switching profiles reconfigures the
        // Workspace in place (same ShellServices), so no resync is needed here.

        Activated   += (_, _) => _shellServices.SetFocused(_vm);
        Deactivated += (_, _) => _shellServices.ClearFocused(_vm);
        Closing     += (_, _) => { _shellServices.UnregisterWindow(_vm); _vm.Detach(); };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_vm.HasFeatureOverlay) { _vm.CloseOverlay(); return; }
                _vm.OptionsOpen            = false;
                _vm.WorkspaceConfigOpen    = false;
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

        WireAiBarChatHandlers();
    }

    // ── AI input bar: feature key/drop handlers + insert requests ─────────
    // The bar is shell chrome, but the active page's feature can participate (terminal history/completion,
    // drag-a-file-to-insert-its-path, paste-from-console). Routing lives on the ViewModel; the control
    // mutation (caret insert, key result) stays here since the ViewModel can't reach the TextBox.

    private void WireAiBarChatHandlers()
    {
        AiInput.KeyInterceptor = _vm.HandleChatKey;

        _vm.ChatInputInsertRequested += InsertIntoAiBar;

        AiInput.AllowDrop        = true;
        AiInput.PreviewDragOver += OnAiInputDragOver;
        AiInput.Drop            += OnAiInputDrop;
    }

    private void InsertIntoAiBar(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        AiInput.Focus();
        int caret = Math.Clamp(AiInput.CaretIndex, 0, AiInput.Text.Length);
        AiInput.Text       = AiInput.Text.Insert(caret, text);
        AiInput.CaretIndex = caret + text.Length;
    }

    private void OnAiInputDragOver(object sender, DragEventArgs e)
    {
        if (_vm.CanHandleChatDrop(e.Data))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;   // suppress the TextBox's own drag handling for payloads we own
        }
    }

    private void OnAiInputDrop(object sender, DragEventArgs e)
    {
        var text = _vm.BuildChatDropText(e.Data);
        if (text is not null)
        {
            InsertIntoAiBar(text);
            e.Handled = true;
        }
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
