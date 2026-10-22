using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// Puts a control behind an AI ability gate — any object with an <c>IsEnabled</c> and a <c>DisabledTip</c> that it raises
/// changes of, as <c>IAiAbilityGate</c> is: the control is enabled as the gate is, and while it is disabled its tooltip is
/// the gate's, saying how to enable it, its own coming back once it is enabled — bound as it was. A null gate leaves the control as it is,
/// so one template serves controls that need no ability.
/// </summary>
/// <example><c>&lt;Button Command="{Binding SolveCommand}" vcc:AiAbilityGating.Gate="{Binding Answering}"/&gt;</c></example>
public static class AiAbilityGating
{
    public static readonly DependencyProperty GateProperty = DependencyProperty.RegisterAttached(
        "Gate", typeof(object), typeof(AiAbilityGating), new PropertyMetadata(null, OnGateChanged));

    public static object? GetGate(DependencyObject element) => element.GetValue(GateProperty);
    public static void SetGate(DependencyObject element, object? value) => element.SetValue(GateProperty, value);

    /// <summary>The gate's <c>IsEnabled</c>, bound.</summary>
    private static readonly DependencyProperty GateEnabledProperty = DependencyProperty.RegisterAttached(
        "GateEnabled", typeof(bool), typeof(AiAbilityGating), new PropertyMetadata(true, OnGateStateChanged));

    /// <summary>The gate's <c>DisabledTip</c>, bound.</summary>
    private static readonly DependencyProperty DisabledTipProperty = DependencyProperty.RegisterAttached(
        "DisabledTip", typeof(string), typeof(AiAbilityGating), new PropertyMetadata(null, OnGateStateChanged));

    /// <summary>Whether the gate's tooltip stands in for the control's own now.</summary>
    private static readonly DependencyProperty ShowingTipProperty = DependencyProperty.RegisterAttached(
        "ShowingTip", typeof(bool), typeof(AiAbilityGating));

    /// <summary>The control's own tooltip, kept while the gate's stands in for it.</summary>
    private static readonly DependencyProperty OwnTipProperty = DependencyProperty.RegisterAttached(
        "OwnTip", typeof(object), typeof(AiAbilityGating));

    private static void OnGateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if (e.NewValue is not { } gate)
        {
            BindingOperations.ClearBinding(element, UIElement.IsEnabledProperty);
            BindingOperations.ClearBinding(element, GateEnabledProperty);
            BindingOperations.ClearBinding(element, DisabledTipProperty);
            return;
        }
        ToolTipService.SetShowOnDisabled(element, true);
        element.SetBinding(UIElement.IsEnabledProperty, new Binding("IsEnabled") { Source = gate, Mode = BindingMode.OneWay });
        element.SetBinding(GateEnabledProperty, new Binding("IsEnabled") { Source = gate, Mode = BindingMode.OneWay });
        element.SetBinding(DisabledTipProperty, new Binding("DisabledTip") { Source = gate, Mode = BindingMode.OneWay });
    }

    private static void OnGateStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        bool showing = (bool)element.GetValue(ShowingTipProperty);
        if (!(bool)element.GetValue(GateEnabledProperty) && element.GetValue(DisabledTipProperty) is string tip)
        {
            // A bound tip is kept as its binding — an expression belongs to the one property it was made for.
            if (!showing && element.ReadLocalValue(FrameworkElement.ToolTipProperty) is var own)
                element.SetValue(OwnTipProperty, own is BindingExpressionBase bound ? bound.ParentBindingBase : own);
            element.SetValue(ShowingTipProperty, true);
            element.ToolTip = tip;
        }
        else if (showing)
        {
            element.SetValue(ShowingTipProperty, false);
            Restore(element, element.GetValue(OwnTipProperty));
        }
    }

    /// <summary>
    /// The control's own tooltip back as it was before the gate's stood in for it: bound again where it was bound — a tip that
    /// says what is under way, a string that follows the reader's language — set again where it was set, cleared where it had none.
    /// </summary>
    private static void Restore(FrameworkElement element, object? own)
    {
        if (own is BindingBase bound) element.SetBinding(FrameworkElement.ToolTipProperty, bound);
        else if (own == DependencyProperty.UnsetValue) element.ClearValue(FrameworkElement.ToolTipProperty);
        else element.ToolTip = own;
    }
}
