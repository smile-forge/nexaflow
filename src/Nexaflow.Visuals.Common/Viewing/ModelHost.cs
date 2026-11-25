using System.Windows;
using System.Windows.Controls;
using Nexaflow.Localization;

namespace Nexaflow.Visuals.Common.Viewing;

/// <summary>
/// Shows a model with the view that draws it: the view marked <see cref="ViewForAttribute"/> its type, made by the
/// <see cref="ViewRegistry"/> its window hands down. For a contract that hands out something to show (a page's preview, a
/// folder viewlet) without naming how it is drawn, hosted by a feature that cannot reach the app's registry itself.
/// <para>
/// A model no view can draw shows why, rather than an empty space that reads as nothing to show.
/// </para>
/// </summary>
public sealed class ModelHost : ContentControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(object), typeof(ModelHost), new PropertyMetadata(null, (host, _) => ((ModelHost)host).Show()));

    static ModelHost()
    {
        // Heard here too, so a host made before it joins a window draws its model once it does.
        ViewRegistry.RegistryProperty.OverrideMetadata(typeof(ModelHost), new FrameworkPropertyMetadata(
            null, FrameworkPropertyMetadataOptions.Inherits, (host, _) => ((ModelHost)host).Show()));
    }

    /// <summary>What to show; null shows nothing.</summary>
    public object? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    private void Show()
    {
        if (Model is not { } model)
        {
            Content = null;
            return;
        }

        // Out of a window for now (moved, or re-laid out): the view it has stays, state and all, until it is back.
        if (ViewRegistry.GetRegistry(this) is not { } registry) return;

        // Moved to another window, or told the same model again: the view it has already draws it.
        if (Content is DependencyObject shown && ReferenceEquals(ViewRegistry.ModelOf(shown), model)) return;

        try
        {
            Content = registry.Create(model);
        }
        catch (Exception failure)
        {
            var why = new TextBlock
            {
                Text = Str.Format("Common.View.CouldNotShowFormat", failure.Message),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8),
            };
            why.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            Content = why;
        }
    }
}
