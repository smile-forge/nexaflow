using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>
/// A double animation whose value on every frame is a function of where its clock is in the cycle: the
/// seconds since the current repeat began. It is the animation for motion that answers to something larger
/// than one element — a scene whose elements all follow one shared model (a wind, a tide) evaluates that
/// model per frame through this, instead of baking it into a keyframe list per element that has to agree
/// with every other element's list.
/// <para>
/// The curve must be a pure function of time. It is evaluated on the UI thread every frame the clock ticks,
/// so it should cost about what a keyframe lookup costs, and every copy of the timeline — the clock always
/// runs a frozen copy — shares the one delegate.
/// </para>
/// </summary>
public sealed class CurveAnimation : DoubleAnimationBase
{
    private Func<double, double> _curve = static _ => 0;

    /// <summary>For <see cref="CreateInstanceCore"/>; a usable animation takes its curve and cycle.</summary>
    public CurveAnimation() { }

    /// <summary>An animation that follows <paramref name="curve"/> round a cycle of <paramref name="cycle"/>, forever.</summary>
    public CurveAnimation(Func<double, double> curve, TimeSpan cycle)
    {
        _curve         = curve;
        Duration       = new Duration(cycle);
        RepeatBehavior = RepeatBehavior.Forever;
    }

    protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue,
                                                  AnimationClock animationClock)
        => animationClock.CurrentTime is TimeSpan t ? _curve(t.TotalSeconds) : defaultOriginValue;

    protected override Freezable CreateInstanceCore() => new CurveAnimation();

    // The curve is a plain field, which Freezable's property-wise copy cannot see — and a clock never runs the
    // timeline it was made from, only a frozen copy of it. Every copy path carries the curve, or the clock
    // would animate the default and the scene would sit still with no error anywhere.
    protected override void CloneCore(Freezable sourceFreezable)
    {
        base.CloneCore(sourceFreezable);
        _curve = ((CurveAnimation)sourceFreezable)._curve;
    }

    protected override void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        base.CloneCurrentValueCore(sourceFreezable);
        _curve = ((CurveAnimation)sourceFreezable)._curve;
    }

    protected override void GetAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetAsFrozenCore(sourceFreezable);
        _curve = ((CurveAnimation)sourceFreezable)._curve;
    }

    protected override void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        base.GetCurrentValueAsFrozenCore(sourceFreezable);
        _curve = ((CurveAnimation)sourceFreezable)._curve;
    }
}
