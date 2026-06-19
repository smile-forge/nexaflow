using Nexaflow.Features.Processes.Models;

namespace Nexaflow.Features.Processes.Services;

/// <summary>
/// Supplies process data to the view-models. Abstracted so the view-models can be unit-tested against a
/// fake with no live system. <see cref="LiveProcessSource"/> is the real implementation; it carries the
/// per-PID CPU-delta state between <see cref="Snapshot"/> calls, so each consumer owns its own instance.
/// </summary>
public interface IProcessSource
{
    /// <summary>One whole-system sample for the main list + toolbar gauges.</summary>
    ProcessSnapshot Snapshot();

    /// <summary>Full detail for one process, or <c>null</c> if it no longer exists.</summary>
    ProcessDetail? ReadDetail(int pid);
}
