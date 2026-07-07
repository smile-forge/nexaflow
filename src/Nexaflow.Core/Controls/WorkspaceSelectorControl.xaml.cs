using Nexaflow.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Core.Controls;

public partial class WorkspaceSelectorControl : UserControl
{
    public static readonly DependencyProperty CurrentWorkspaceProperty =
        DependencyProperty.Register(nameof(CurrentWorkspace), typeof(Workspace),
            typeof(WorkspaceSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty WorkspacesProperty =
        DependencyProperty.Register(nameof(Workspaces), typeof(ObservableCollection<Workspace>),
            typeof(WorkspaceSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectWorkspaceCommandProperty =
        DependencyProperty.Register(nameof(SelectWorkspaceCommand), typeof(ICommand),
            typeof(WorkspaceSelectorControl), new PropertyMetadata(null));

    /// <summary>True when switching is allowed (no modal overlay open).</summary>
    public static readonly DependencyProperty CanSwitchProperty =
        DependencyProperty.Register(nameof(CanSwitch), typeof(bool),
            typeof(WorkspaceSelectorControl), new PropertyMetadata(true));

    /// <summary>Right-click action: configure the current workspace (no parameter).</summary>
    public static readonly DependencyProperty ConfigureCommandProperty =
        DependencyProperty.Register(nameof(ConfigureCommand), typeof(ICommand),
            typeof(WorkspaceSelectorControl), new PropertyMetadata(null));

    /// <summary>Right-click action: clone the current workspace into a new one and configure it.</summary>
    public static readonly DependencyProperty NewWorkspaceCommandProperty =
        DependencyProperty.Register(nameof(NewWorkspaceCommand), typeof(ICommand),
            typeof(WorkspaceSelectorControl), new PropertyMetadata(null));

    /// <summary>Right-click action: save the current window's tabs/panes as this workspace's startup tabset.</summary>
    public static readonly DependencyProperty UseTabsetAsDefaultCommandProperty =
        DependencyProperty.Register(nameof(UseTabsetAsDefaultCommand), typeof(ICommand),
            typeof(WorkspaceSelectorControl), new PropertyMetadata(null));

    public Workspace? CurrentWorkspace
    {
        get => (Workspace?)GetValue(CurrentWorkspaceProperty);
        set => SetValue(CurrentWorkspaceProperty, value);
    }

    public ObservableCollection<Workspace>? Workspaces
    {
        get => (ObservableCollection<Workspace>?)GetValue(WorkspacesProperty);
        set => SetValue(WorkspacesProperty, value);
    }

    public ICommand? SelectWorkspaceCommand
    {
        get => (ICommand?)GetValue(SelectWorkspaceCommandProperty);
        set => SetValue(SelectWorkspaceCommandProperty, value);
    }

    public bool CanSwitch
    {
        get => (bool)GetValue(CanSwitchProperty);
        set => SetValue(CanSwitchProperty, value);
    }

    public ICommand? ConfigureCommand
    {
        get => (ICommand?)GetValue(ConfigureCommandProperty);
        set => SetValue(ConfigureCommandProperty, value);
    }

    public ICommand? NewWorkspaceCommand
    {
        get => (ICommand?)GetValue(NewWorkspaceCommandProperty);
        set => SetValue(NewWorkspaceCommandProperty, value);
    }

    public ICommand? UseTabsetAsDefaultCommand
    {
        get => (ICommand?)GetValue(UseTabsetAsDefaultCommandProperty);
        set => SetValue(UseTabsetAsDefaultCommandProperty, value);
    }

    public WorkspaceSelectorControl()
    {
        InitializeComponent();
    }

    private void OnTriggerClick(object sender, RoutedEventArgs e)
    {
        // Modal overlays (Options / Manage-AI) block switching.
        if (!CanSwitch) return;
        ContextPopup.IsOpen = true;
    }

    // Right-click opens a menu to configure the current workspace or create a new one (cloned from it).
    // Configure leads (the more common action), separated from New. A bare ContextMenu in XAML can't bind
    // ElementName=Root (it's outside the namescope), so build it here.
    private void OnTriggerRightClick(object sender, MouseButtonEventArgs e)
    {
        if (!CanSwitch) return;

        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        AddMenuItem(menu, "Configure workspace…", ConfigureCommand);
        AddMenuItem(menu, "Use Tabset as Default", UseTabsetAsDefaultCommand);
        if (menu.Items.Count > 0 && NewWorkspaceCommand is not null)
            menu.Items.Add(new Separator());
        AddMenuItem(menu, "New workspace…", NewWorkspaceCommand);
        if (menu.Items.Count == 0) return;

        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void AddMenuItem(ContextMenu menu, string header, ICommand? command)
    {
        if (command is null) return;
        var item = new MenuItem { Header = header };
        item.Click += (_, _) =>
        {
            if (command.CanExecute(null)) command.Execute(null);
        };
        menu.Items.Add(item);
    }

    private void OnContextItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Workspace workspace })
        {
            SelectWorkspaceCommand?.Execute(workspace);
            ContextPopup.IsOpen = false;
        }
    }
}
