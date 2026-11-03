using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Icons;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// A group of an edit panel's controls under its title — Light, Colour, Brush — that folds away at a press on the title, or
/// Space or Enter on it, so a person keeps open only what they are working with and the panel needs no scrolling for the rest.
/// Whether it is open is <see cref="IsOpen"/>, bound to what remembers it (<c>PanelFolds</c>); to UI Automation it is a group
/// that expands and collapses.
/// </summary>
[TemplatePart(Name = HeaderPart, Type = typeof(FrameworkElement))]
public class FoldingGroup : ContentControl
{
    public const string HeaderPart = "PART_Header";

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FoldingGroup), new PropertyMetadata(""));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(IconRef), typeof(FoldingGroup), new PropertyMetadata(default(IconRef)));

    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(FoldingGroup),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIsOpenChanged));

    /// <summary>Raised as the group opens — and as it is first shown open — for a group whose controls are read when wanted.</summary>
    public static readonly RoutedEvent OpenedEvent = EventManager.RegisterRoutedEvent(
        nameof(Opened), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(FoldingGroup));

    private FrameworkElement? _header;

    /// <summary>What the group is called, at its head.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The icon beside the title, as the icon set names it.</summary>
    public IconRef Icon
    {
        get => (IconRef)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>Its controls show.</summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>The group opened, or was first shown open.</summary>
    public event RoutedEventHandler Opened
    {
        add => AddHandler(OpenedEvent, value);
        remove => RemoveHandler(OpenedEvent, value);
    }

    public FoldingGroup()
    {
        Loaded += (_, _) =>
        {
            if (IsOpen) RaiseEvent(new RoutedEventArgs(OpenedEvent, this));
        };
    }

    public override void OnApplyTemplate()
    {
        if (_header is not null)
        {
            _header.MouseLeftButtonUp -= HeaderPressed;
            _header.KeyDown -= HeaderKeyed;
        }
        base.OnApplyTemplate();
        _header = GetTemplateChild(HeaderPart) as FrameworkElement;
        if (_header is null) return;
        _header.MouseLeftButtonUp += HeaderPressed;
        _header.KeyDown += HeaderKeyed;
    }

    private void HeaderPressed(object sender, MouseButtonEventArgs e)
    {
        IsOpen = !IsOpen;
        e.Handled = true;
    }

    private void HeaderKeyed(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Space or Key.Enter)) return;
        IsOpen = !IsOpen;
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FoldingGroupAutomationPeer(this);

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var group = (FoldingGroup)d;
        if ((bool)e.NewValue && group.IsLoaded) group.RaiseEvent(new RoutedEventArgs(OpenedEvent, group));
        if (UIElementAutomationPeer.FromElement(group) is { } peer)
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                                           FoldingGroupAutomationPeer.StateOf((bool)e.OldValue), FoldingGroupAutomationPeer.StateOf((bool)e.NewValue));
    }
}

/// <summary>A <see cref="FoldingGroup"/> to UI Automation: a group named by its title, expanded while it is open.</summary>
internal sealed class FoldingGroupAutomationPeer(FoldingGroup owner) : FrameworkElementAutomationPeer(owner), IExpandCollapseProvider
{
    private FoldingGroup Group => (FoldingGroup)Owner;

    internal static ExpandCollapseState StateOf(bool open) => open ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(FoldingGroup);

    protected override string GetNameCore() => base.GetNameCore() is { Length: > 0 } name ? name : Group.Title;

    public override object? GetPattern(PatternInterface patternInterface)
        => patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

    public ExpandCollapseState ExpandCollapseState => StateOf(Group.IsOpen);

    public void Expand() => Group.IsOpen = true;

    public void Collapse() => Group.IsOpen = false;
}
