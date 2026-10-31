using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>
/// An element's hold on an icon the shell worker draws: the picture in hand, the ticket for a better one, and whether it
/// has to ask again. The element asks while it is drawn, so one that is never drawn never asks; out of the tree it stops
/// waiting, and back in it is drawn — and asks — again.
/// </summary>
public sealed class ShellIconRequest
{
    private readonly FrameworkElement _owner;
    private IDisposable? _pending;

    public ShellIconRequest(FrameworkElement owner)
    {
        _owner = owner;
        owner.Unloaded += (_, _) => { if (_pending is not null) Forget(); };
        owner.Loaded   += (_, _) => { if (IsStale) owner.InvalidateVisual(); };
    }

    /// <summary>The icon in hand, or null while there is none.</summary>
    public ImageSource? Icon { get; private set; }

    /// <summary>True until the element asks, and again once it has forgotten what it asked for.</summary>
    public bool IsStale { get; private set; } = true;

    /// <summary>
    /// Asks through <paramref name="request"/>, which is handed the callback a better icon arrives on, and takes what it
    /// answers at once; the arrival redraws the element. A null request asks for nothing, leaving no icon in hand.
    /// </summary>
    public void Ask(Func<Action<ImageSource>, ShellIconLookup>? request)
    {
        IsStale = false;
        Icon    = null;
        if (request is null) return;

        var lookup = request(Arrived);
        Icon     = lookup.Icon;
        _pending = lookup.Pending;
    }

    /// <summary>Stops waiting for the icon asked for, and asks afresh on the next drawing.</summary>
    public void Forget()
    {
        _pending?.Dispose();
        _pending = null;
        IsStale  = true;
    }

    private void Arrived(ImageSource icon)
    {
        _pending = null;
        Icon     = icon;
        _owner.InvalidateVisual();
    }
}
