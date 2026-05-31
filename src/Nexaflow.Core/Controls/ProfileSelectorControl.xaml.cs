using Nexaflow.Core.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Nexaflow.Core.Controls;

public partial class ProfileSelectorControl : UserControl
{
    public static readonly DependencyProperty CurrentProfileProperty =
        DependencyProperty.Register(nameof(CurrentProfile), typeof(Profile),
            typeof(ProfileSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty ProfilesProperty =
        DependencyProperty.Register(nameof(Profiles), typeof(ObservableCollection<Profile>),
            typeof(ProfileSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectProfileCommandProperty =
        DependencyProperty.Register(nameof(SelectProfileCommand), typeof(ICommand),
            typeof(ProfileSelectorControl), new PropertyMetadata(null));

    /// <summary>True when switching is allowed (no modal overlay open).</summary>
    public static readonly DependencyProperty CanSwitchProperty =
        DependencyProperty.Register(nameof(CanSwitch), typeof(bool),
            typeof(ProfileSelectorControl), new PropertyMetadata(true));

    public Profile? CurrentProfile
    {
        get => (Profile?)GetValue(CurrentProfileProperty);
        set => SetValue(CurrentProfileProperty, value);
    }

    public ObservableCollection<Profile>? Profiles
    {
        get => (ObservableCollection<Profile>?)GetValue(ProfilesProperty);
        set => SetValue(ProfilesProperty, value);
    }

    public ICommand? SelectProfileCommand
    {
        get => (ICommand?)GetValue(SelectProfileCommandProperty);
        set => SetValue(SelectProfileCommandProperty, value);
    }

    public bool CanSwitch
    {
        get => (bool)GetValue(CanSwitchProperty);
        set => SetValue(CanSwitchProperty, value);
    }

    public ProfileSelectorControl()
    {
        InitializeComponent();
    }

    private void OnTriggerClick(object sender, RoutedEventArgs e)
    {
        // Modal overlays (Options / Manage-AI) block switching.
        if (!CanSwitch) return;
        ContextPopup.IsOpen = true;
    }

    private void OnContextItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Profile profile })
        {
            SelectProfileCommand?.Execute(profile);
            ContextPopup.IsOpen = false;
        }
    }
}
