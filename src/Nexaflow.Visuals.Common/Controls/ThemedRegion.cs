using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// Wraps a UI region with up to two theme-supplied layers behind its content, resolved by the
/// region's name from the active theme's resources:
/// <list type="bullet">
///   <item><c>Scene.{Region}</c> — an optional <see cref="DataTemplate"/> rendering a living
///   backdrop (animated <c>Canvas</c>, shader, etc.). Bottom layer.</item>
///   <item><c>{Region}.Bg</c> — an optional <see cref="Brush"/> veil/tint over the scene. Middle
///   layer.</item>
/// </list>
/// The region's actual content sits on top, untouched. A "Pro" theme (Dark/Light) that supplies
/// neither key renders identically to having no wrapper — zero extra visuals, no layout change —
/// while an immersive theme can drop a scene behind the exact region it names. Because the layers
/// are keyed per region, two regions (e.g. the AI bar vs the page area) can be themed independently
/// without either feature code or Core knowing what art a theme chose.
/// </summary>
public class ThemedRegion : ContentControl
{
    static ThemedRegion()
        => DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ThemedRegion), new FrameworkPropertyMetadata(typeof(ThemedRegion)));

    public static readonly DependencyProperty RegionProperty =
        DependencyProperty.Register(nameof(Region), typeof(string), typeof(ThemedRegion),
            new PropertyMetadata(null, (d, _) => ((ThemedRegion)d).ApplyRegion()));

    /// <summary>Region key driving the <c>Scene.{Region}</c> / <c>{Region}.Bg</c> lookups,
    /// e.g. "Page" or "AiBar".</summary>
    public string? Region
    {
        get => (string?)GetValue(RegionProperty);
        set => SetValue(RegionProperty, value);
    }

    private Border? _backdrop;
    private ContentControl? _scene;

    public ThemedRegion()
    {
        // Resources may not be reachable until the control is in the live tree; re-resolve then.
        Loaded += (_, _) => ApplyRegion();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _backdrop = GetTemplateChild("PART_Backdrop") as Border;
        _scene    = GetTemplateChild("PART_Scene") as ContentControl;
        ApplyRegion();
    }

    private void ApplyRegion()
    {
        if (_backdrop is null || _scene is null || string.IsNullOrEmpty(Region)) return;

        _backdrop.Background = TryFindResource($"{Region}.Bg") as Brush ?? Brushes.Transparent;

        if (TryFindResource($"Scene.{Region}") is DataTemplate scene)
        {
            _scene.ContentTemplate = scene;
            _scene.Content         = Region;   // any non-null value realises the template
        }
        else
        {
            _scene.Content         = null;
            _scene.ContentTemplate = null;
        }
    }
}
