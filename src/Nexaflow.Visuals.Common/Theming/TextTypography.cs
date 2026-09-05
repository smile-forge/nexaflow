using System;
using System.Collections.Generic;
using System.Threading;

namespace Nexaflow.Visuals.Common.Theming;

/// <summary>
/// The point size text content is read at — the shell's "Text size" setting, in one place so every
/// surface that shows a document agrees on it.
/// <para>
/// It is the <em>base</em>: a viewer's own zoom multiplies it (see <see cref="TextZoom"/>), and a
/// surface with its own typographic ladder (markdown's headings) expresses that as ratios of this
/// rather than as absolute sizes. So one number moves the text editor, the markdown document and the
/// hex grid together, and each keeps its own proportions.
/// </para>
/// <para>
/// Deliberately static, and for the same reason as <see cref="BackgroundAnimationPolicy"/>: the things
/// that read it include a <c>FrameworkElement</c> WPF realises from a template and a static renderer,
/// neither of which has a constructor to inject into. It knows nothing about <em>where</em> the number
/// came from — the shell owns the setting and pushes it in (see <c>App.InitializeApp</c> and
/// <c>ShellConfigControl.Apply</c> in Core).
/// </para>
/// </summary>
public static class TextTypography
{
    /// <summary>Size used when nothing has been configured — what the text editor shipped with.</summary>
    public const double DefaultBaseFontSize = 13.0;

    /// <summary>Smallest size the setting may take. Below this, line metrics stop being legible.</summary>
    public const double MinBaseFontSize = 8.0;

    /// <summary>Largest size the setting may take.</summary>
    public const double MaxBaseFontSize = 32.0;

    private static double _baseFontSize = DefaultBaseFontSize;

    private static readonly List<WeakReference<Action>> Listeners = [];

    /// <summary>
    /// Base point size for text content. Assignments are clamped to
    /// [<see cref="MinBaseFontSize"/>, <see cref="MaxBaseFontSize"/>] — a stored config from a future
    /// version, or a hand-edited one, can never render the app unreadable.
    /// </summary>
    public static double BaseFontSize
    {
        get => Volatile.Read(ref _baseFontSize);
        set
        {
            var clamped = Clamp(value);
            if (Volatile.Read(ref _baseFontSize).Equals(clamped)) return;
            Volatile.Write(ref _baseFontSize, clamped);
            Notify();
        }
    }

    /// <summary>
    /// Runs <paramref name="handler"/> whenever <see cref="BaseFontSize"/> changes, for as long as
    /// <paramref name="handler"/> itself is reachable — <b>keep it in a field of the object that cares</b>,
    /// and the registration is collected with that object.
    /// <para>
    /// Weak rather than a plain event because the things that listen are per-tab (a
    /// <see cref="TextZoom"/> lives as long as one document is open) while this lives as long as the
    /// process. An unsubscribe every host has to remember is a leak every host can forget; there is
    /// nothing to remember here.
    /// </para>
    /// <para>May be invoked on any thread, so a handler that touches UI must marshal.</para>
    /// </summary>
    public static void AddChangeListener(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (Listeners)
        {
            Prune();
            Listeners.Add(new WeakReference<Action>(handler));
        }
    }

    /// <summary>Clamps <paramref name="size"/> into the allowed range, mapping a non-finite or
    /// non-positive value (an unset config field) onto <see cref="DefaultBaseFontSize"/>.</summary>
    public static double Clamp(double size)
        => !double.IsFinite(size) || size <= 0
            ? DefaultBaseFontSize
            : Math.Clamp(size, MinBaseFontSize, MaxBaseFontSize);

    private static void Notify()
    {
        Action[] live;
        lock (Listeners)
        {
            Prune();
            live = new Action[Listeners.Count];
            for (var i = 0; i < Listeners.Count; i++)
                Listeners[i].TryGetTarget(out live[i]!);
        }

        // Outside the lock: a handler re-entering (a viewer that re-registers on resize) would deadlock,
        // and one that throws must not take the rest of the list down with it.
        foreach (var handler in live)
            try { handler?.Invoke(); } catch { }
    }

    /// <summary>Drops registrations whose handler has been collected. Called under the lock.</summary>
    private static void Prune()
        => Listeners.RemoveAll(w => !w.TryGetTarget(out _));
}
