namespace Nexaflow.Providers.Common;

/// <summary>
/// Allows a provider to report activity that gets surfaced in the shell's
/// activity ticker, without the provider knowing anything about WPF or
/// observable collections.
/// </summary>
public interface IBackgroundActivityManager
{
    /// <summary>
    /// Starts a named activity.  The returned handle must have
    /// <see cref="IActivityHandle.Complete"/> or <see cref="IActivityHandle.Fail"/>
    /// called when the work finishes.
    /// </summary>
    IActivityHandle StartActivity(string description);
}

/// <summary>
/// Represents one in-flight activity.  Call <see cref="Complete"/> or
/// <see cref="Fail"/> exactly once when the work is done.
/// </summary>
public interface IActivityHandle
{
    void Complete();
    void Fail(string? reason = null);
}
