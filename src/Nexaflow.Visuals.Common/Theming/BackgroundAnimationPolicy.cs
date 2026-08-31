using System;

namespace Nexaflow.Visuals.Common.Theming;

/// <summary>
/// One switch deciding whether a theme's decorative backdrop - the animated <c>Scene.{Region}</c>
/// layer a <see cref="Controls.ThemedRegion"/> renders behind its content - may run at all.
/// <para>
/// A scene is pure decoration: it is the only part of the shell that animates forever with nothing
/// happening, so it is the only part worth switching off wholesale to save power. When this is
/// false a <see cref="Controls.ThemedRegion"/> renders no scene at all - the same visuals a "Pro"
/// theme (Dark/Light) gives, which is strictly cheaper than pausing a built scene: no forever
/// clocks, no large blended surfaces to composite, no visual tree.
/// </para>
/// <para>
/// This type knows nothing about <em>why</em> scenes are off - power state, a preference, a remote
/// session - so it stays testable and reusable; the shell owns the reasons and pushes the answer in
/// (see <c>BatteryAnimationGuard</c> in Core). It is deliberately static because a scene is realised
/// by WPF from a <c>DataTemplate</c> in a theme dictionary, which has no constructor to inject into.
/// </para>
/// </summary>
public static class BackgroundAnimationPolicy
{
    private static bool _scenesEnabled = true;

    /// <summary>
    /// Raised when <see cref="ScenesEnabled"/> flips. May be raised on any thread (power-state
    /// notifications arrive on a system thread), so a handler that touches UI must marshal.
    /// </summary>
    public static event EventHandler? Changed;

    /// <summary>Whether theme scene backdrops may render. Defaults to true.</summary>
    public static bool ScenesEnabled
    {
        get => Volatile.Read(ref _scenesEnabled);
        set
        {
            if (Volatile.Read(ref _scenesEnabled) == value) return;
            Volatile.Write(ref _scenesEnabled, value);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
