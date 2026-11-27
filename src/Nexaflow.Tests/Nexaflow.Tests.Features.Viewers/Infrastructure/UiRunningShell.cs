using System.Reflection;
using System.Runtime.ExceptionServices;

using Nexaflow.Features.Common;

namespace Nexaflow.Tests.Features.Infrastructure;

/// <summary>
/// A shell that does what a page hands its UI thread there and then, on the caller's thread — every <c>RunOnUiAsync</c>, whatever its work
/// returns — and answers everything else as the shell it stands over does, so a substitute's <c>Received</c> checks still hold. A
/// substitute alone would hand back a default task and never do the work.
/// </summary>
public class UiRunningShell : DispatchProxy
{
    private IShellServices _inner = null!;

    /// <summary>A shell answering as <paramref name="inner"/> does, except that what it is asked to do on the UI thread is done at once.</summary>
    public static IShellServices Over(IShellServices inner)
    {
        var shell = Create<IShellServices, UiRunningShell>();
        ((UiRunningShell)(object)shell)._inner = inner;
        return shell;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        if (targetMethod.Name == nameof(IShellServices.RunOnUiAsync) && args is [Delegate work])
        {
            if (work is Action action)
            {
                action();
                return Task.CompletedTask;
            }

            // A Func<Task<T>>: the task its work returns is the one the caller awaits.
            return Unwrapped(() => work.DynamicInvoke());
        }

        return Unwrapped(() => targetMethod.Invoke(_inner, args));
    }

    /// <summary>What <paramref name="call"/> returns, with what it threw thrown as itself rather than wrapped by reflection.</summary>
    private static object? Unwrapped(Func<object?> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is { } thrown)
        {
            ExceptionDispatchInfo.Capture(thrown).Throw();
            throw;
        }
    }
}
