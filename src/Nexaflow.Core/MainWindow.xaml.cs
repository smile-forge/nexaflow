using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
using TaskStatus = Nexaflow.Core.Models.TaskStatus;

namespace Nexaflow.Core;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _vm;

    /// <summary>Exposes the view-model so <see cref="WindowManager"/> can manipulate tabs.</summary>
    public ShellViewModel ViewModel => _vm;

    // ── Primary constructor (application startup) ─────────────────────────
    public MainWindow(BackgroundActivityManager activityManager, IAIService aiService)
    {
        InitializeComponent();
        
        _vm = new ShellViewModel(activityManager, aiService);
        DataContext = _vm;

        WireCommands();

        // Open default tabs on startup. AI Chat is opened first so it ends
        // up at index 1; Dashboard is prepended on top and gets focus.
        _vm.OpenTab(new TabEntry
        {
            Title = "Dashboard",
            Icon  = "⊞",
            Breadcrumbs =
            [
                new BreadcrumbSegment { Label = "All",       Children = ["All Regions", "North America", "Europe"] },
                new BreadcrumbSegment { Label = "Acme Corp", Children = ["Acme Corp", "BrightTech", "Novus Labs"] },
                new BreadcrumbSegment { Label = "Overview" }
            ],
            PageFactory = () => new PlaceholderPage()
        });

        // Seed an initial background task (the manager's "Idle" placeholder
        // is already shown; this adds a real running task on top of it).
        _vm.AddBackgroundTask(new BackgroundTask
        {
            Description = "Indexing workspace…",
            Status      = TaskStatus.Running
        });

        FinishInit();
    }

    // ── Tearoff constructor (spawned when a tab is dragged to the desktop) ─
    public MainWindow(TabEntry initialTab)
    {
        InitializeComponent();
        _vm = new ShellViewModel(new BackgroundActivityManager(), new AIService());
        DataContext = _vm;

        WireCommands();
        _vm.ReceiveTab(initialTab);

        FinishInit();
    }

    // ── Shared init ───────────────────────────────────────────────────────

    private void WireCommands()
    {
        // Bind the new TabStrip commands
        TabStripControl.TearOffTabCommand  = new RelayCommand<TabEntry>(tab => WindowManager.TearOffTab(tab!));
        TabStripControl.ReceiveTabCommand  = new RelayCommand<TabEntry>(tab => WindowManager.TransferTab(tab!, _vm));

        // Breadcrumb cross-tab navigation: the breadcrumb bar signals the desired page kind;
        // the shell resolves and opens (or focuses) the appropriate tab.
        BreadcrumbBarControl.OpenTabRequested += (pageKind, pageParams) =>
            _vm.OpenTabForPageKind(pageKind, pageParams);
    }

    private ShellViewModel ShellVm => _vm;

    private void WireOptionsPanel()
    {
        // Recreate the VM each time the panel opens so Cancel always discards edits cleanly.
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
        optionsVm.SaveError           += msg   => ShellVm.ShowErrorToast(msg);
        optionsVm.TabRefreshRequested += kinds => ShellVm.RefreshTabs(kinds);
        optionsVm.SaveCompleted       += ()    => ShellVm.OptionsOpen = false;
        OptionsPanelControl.DataContext = optionsVm;
    }

    private void FinishInit()
    {
        WindowManager.Register(this);
        WireOptionsPanel();

        // Prevent default WPF shutdown-on-last-window behaviour — WindowManager handles it.
        Closing += (_, _) => WindowManager.Unregister(this);

        // ESC closes any open overlay
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _vm.OptionsOpen       = false;
                _vm.NotificationsOpen = false;
                _vm.RibbonEditOpen    = false;
            }
        };

        // Cap the AI row at 50% of window height when the user drags the splitter
        SizeChanged += (_, _) => CapAiRowHeight();
        RootGrid.LayoutUpdated += (_, _) => CapAiRowHeight();
    }

    private void CapAiRowHeight()
    {
        double maxAi = (ActualHeight - 72) * 0.5;   // 50% of usable area (below top bar)
        if (maxAi < 72) maxAi = 72;
        AiRow.MaxHeight = maxAi;
    }

    // ── Tiny relay command helper (avoids pulling in extra infrastructure) ─
    private sealed class RelayCommand<T>(Action<T?> execute) : ICommand
    {
        public bool CanExecute(object? p) => true;
        public void Execute(object? p)    => execute(p is T t ? t : default);
        public event EventHandler? CanExecuteChanged;
    }
}
