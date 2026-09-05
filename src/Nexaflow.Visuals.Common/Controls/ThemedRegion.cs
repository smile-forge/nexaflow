using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Theming;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// Wraps a UI region with up to two theme-supplied layers behind its content, resolved by the
/// region's name from the active theme's resources:
/// <list type="bullet">
///   <item><c>Scene.{Region}</c> — an optional <see cref="DataTemplate"/> rendering a living
///   backdrop (animated <c>Canvas</c>, shader, etc.). Bottom layer. Switched off wholesale by the
///   battery policy, since forever-animating is what makes it worth reclaiming.</item>
///   <item><c>StillScene.{Region}</c> — the same bottom layer for a backdrop that draws once and
///   never animates. Never battery-gated: cached as a texture it costs what the colour veil below
///   costs, so dropping it would lose the theme's art and save nothing.</item>
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
        // The policy subscribe is idempotent and made from both here and Loaded: a region built but never
        // loaded still tracks the policy, and one re-parented after Unloaded picks the handler back up.
        SubscribeToAnimationPolicy();
        Loaded   += (_, _) => { SubscribeToAnimationPolicy(); ApplyRegion(); };
        Unloaded += (_, _) => BackgroundAnimationPolicy.Changed -= OnAnimationPolicyChanged;
    }

    private void SubscribeToAnimationPolicy()
    {
        BackgroundAnimationPolicy.Changed -= OnAnimationPolicyChanged;
        BackgroundAnimationPolicy.Changed += OnAnimationPolicyChanged;
    }

    // The policy can flip from a system power-state thread, so hop to this region's own dispatcher
    // before touching its visual tree. Dropping the handler on Unloaded is what keeps a static event
    // from outliving the region that subscribed to it.
    private void OnAnimationPolicyChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess()) ApplyRegion();
        else                          Dispatcher.BeginInvoke(ApplyRegion);
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

        // Two backdrop keys, and what separates them is precisely what the power policy is entitled to
        // take away.
        //
        // Scene.{Region} animates forever, which is the whole reason the shell can switch it off at all.
        // A suppressed one is DROPPED rather than paused: the scene unloads, stops its clocks and clears
        // its visuals, leaving exactly what a theme with no scene key renders. Pausing would keep a large
        // blended visual tree alive for no benefit.
        //
        // StillScene.{Region} draws once and never animates. After the base class's cache pass it is a
        // static texture, costing what the flat {Region}.Bg veil beside it already costs — and that veil
        // is left alone on battery for exactly the same reason. Measured on a folder tab with the window
        // in front: Flowers (a still plate of some hundreds of drawn plants) idles at 0.0% of a core,
        // against Dark's 0.7% and Ocean's 10.7%. There is nothing there to reclaim, so it is never
        // gated. A theme supplies one key or the other; a still backdrop wins if both are somehow
        // present, being the one that survives either state.
        var backdrop = TryFindResource($"StillScene.{Region}") as DataTemplate
                       ?? (BackgroundAnimationPolicy.ScenesEnabled
                               ? TryFindResource($"Scene.{Region}") as DataTemplate
                               : null);

        if (backdrop is not null)
        {
            _scene.ContentTemplate = backdrop;
            _scene.Content         = Region;   // any non-null value realises the template
        }
        else
        {
            _scene.Content         = null;
            _scene.ContentTemplate = null;
        }
    }
}
