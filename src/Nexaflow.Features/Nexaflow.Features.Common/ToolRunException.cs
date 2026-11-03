namespace Nexaflow.Features.Common;

/// <summary>Why a local tool did not do what it was asked — which says whether asking it again, as it is, can help.</summary>
public enum ToolRunFailure
{
    /// <summary>What runs the tool stopped while it worked, and stopped again when the app asked once more: something on this
    /// machine is wrong for now, and asking later may work.</summary>
    Interrupted,

    /// <summary>The tool could not run now — the memory it needs is taken by other models, or what runs it could not be
    /// started: once that is freed, asking again can work.</summary>
    NotNow,

    /// <summary>The tool ran and could not do it: the same ask fails the same way, and only a different one can work.</summary>
    Unworkable,
}

/// <summary>
/// A local tool's job that did not end in what was asked, saying why (<see cref="Failure"/>). An
/// <see cref="InvalidOperationException"/>, so whatever caught a tool's failure before still does, and a caller that offers
/// to try again asks <see cref="MayWorkAgain"/> first.
/// </summary>
public sealed class ToolRunException(ToolRunFailure failure, string message, Exception? inner = null) : InvalidOperationException(message, inner)
{
    public ToolRunFailure Failure { get; } = failure;

    /// <summary>Asking again, as it is, can end otherwise.</summary>
    public bool MayWorkAgain => Failure is not ToolRunFailure.Unworkable;
}
