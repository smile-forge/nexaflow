using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Metadata;

namespace Nexaflow.AvaloniaShell.Theming;

/// <summary>
/// A shell region as the theme draws it, the WPF app's <c>ThemedRegion</c> on Avalonia: bottom to top, the theme's backdrop for the
/// region (<c>Scene.{Region}</c>, animated, or <c>StillScene.{Region}</c>, drawn once), its <c>{Region}.Bg</c> veil, and the region's
/// own content. A theme that supplies neither draws nothing extra. A new theme is followed as it is applied.
/// <para>
/// A scene is an Avalonia control of <c>Nexaflow.AvaloniaShell.Scenes</c> named as the theme names it, its properties set from the
/// theme's text. A theme naming a scene this app has no port of draws its veil alone.
/// </para>
/// </summary>
public sealed class ThemedRegion : Panel
{
    public static readonly StyledProperty<string> RegionProperty = AvaloniaProperty.Register<ThemedRegion, string>(nameof(Region), "");

    public static readonly StyledProperty<Control?> ChildProperty = AvaloniaProperty.Register<ThemedRegion, Control?>(nameof(Child));

    private readonly Border _veil = new() { IsHitTestVisible = false };
    private Control? _scene;
    private SceneDefinition? _drawn;

    public ThemedRegion() => Children.Add(_veil);

    /// <summary>The region's name: the <c>{Region}</c> of its theme keys.</summary>
    public string Region
    {
        get => GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    /// <summary>The region's own content, over the backdrop.</summary>
    [Content]
    public Control? Child
    {
        get => GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChildProperty)
        {
            if (change.OldValue is Control old) Children.Remove(old);
            if (change.NewValue is Control added) Children.Add(added);
        }
        else if (change.Property == RegionProperty)
        {
            Follow();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResourcesChanged += OnResourcesChanged;
        Follow();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ResourcesChanged -= OnResourcesChanged;
        Draw(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnResourcesChanged(object? sender, ResourcesChangedEventArgs e) => Follow();

    /// <summary>Draws what the theme now gives the region: its veil, and its scene unless it is the one already drawn.</summary>
    private void Follow()
    {
        if (Region.Length == 0) return;
        _veil.Background = this.TryFindResource($"{Region}.Bg", ActualThemeVariant, out var veil) ? veil as IBrush : null;

        var scene = this.TryFindResource($"Scene.{Region}", ActualThemeVariant, out var animated) && animated is SceneDefinition moving ? moving
                  : this.TryFindResource($"StillScene.{Region}", ActualThemeVariant, out var still) && still is SceneDefinition resting ? resting
                  : null;
        if (!Equals(scene, _drawn)) Draw(scene);
    }

    private void Draw(SceneDefinition? scene)
    {
        if (_scene is not null) Children.Remove(_scene);
        _scene = scene is null ? null : Make(scene);
        _drawn = scene;
        if (_scene is not null) Children.Insert(0, _scene);
    }

    private static Control? Make(SceneDefinition scene)
    {
        var type = typeof(ThemedRegion).Assembly.GetType($"Nexaflow.AvaloniaShell.Scenes.{scene.TypeName}");
        if (type is null || !typeof(Control).IsAssignableFrom(type)) return null;

        var made = (Control)Activator.CreateInstance(type)!;
        foreach (var (name, written) in scene.Properties)
            if (type.GetProperty(name) is { CanWrite: true } property)
                property.SetValue(made, TypeDescriptor.GetConverter(property.PropertyType).ConvertFromString(null, CultureInfo.InvariantCulture, written));
        made.IsHitTestVisible = false;
        return made;
    }
}
