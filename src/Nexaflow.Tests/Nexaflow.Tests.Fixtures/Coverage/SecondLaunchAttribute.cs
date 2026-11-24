namespace Nexaflow.Tests.Fixtures;

/// <summary>Why a feature's UI journeys need a second launch of the app.</summary>
public enum SecondLaunchReason
{
    /// <summary>The feature's steps together take longer than two minutes, so one journey would be a block nobody can wait for.</summary>
    OverTwoMinutes,

    /// <summary>There is a genuinely second complete state to check — a feature enabled and disabled, the models present and absent — and one app is in one of them.</summary>
    SecondState,
}

/// <summary>
/// Marks a journey as its feature's <b>second</b> launch of the app. A feature has one journey, made of steps; the app costs about
/// twenty seconds to launch and take down, so every launch beyond the first has to say why it is not a step of the first
/// (the architecture suite's <c>JourneyLaunchRulesTests</c> holds the line). The <see cref="Why"/> is read by whoever next wants to fold it back in.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class SecondLaunchAttribute(SecondLaunchReason reason, string why) : Attribute
{
    /// <summary>Which of the two reasons a launch may have.</summary>
    public SecondLaunchReason Reason { get; } = reason;

    /// <summary>What makes it so, in a sentence — the measured time, or the state that differs.</summary>
    public string Why { get; } = why;
}
