using System.Collections.Generic;
using System.Threading;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>What a right-button drag can be told to do once it lands.</summary>
public enum DropChoice
{
    Copy,
    Move,
}

/// <summary>
/// A drop, read off its payload and held so it can be carried out later.
/// <para>
/// Capturing is not bookkeeping — a payload lives only as long as the drag, and a drop that asks the user a question is
/// answered long after it. Everything the operation needs is therefore taken while it is still there.
/// </para>
/// <para>
/// It is also the thing that remembers having been carried out. A drop is one event, and the gap
/// between capturing it and answering it is exactly where a second answer can arrive: a menu that
/// outlives its own dismissal still holds live commands over this plan. <see cref="TryClaim"/> is what makes that
/// harmless wherever it comes from, rather than at each place that might do it.
/// </para>
/// </summary>
public sealed class DropPlan(IReadOnlyList<string> sources, string destination, string destinationLabel)
{
    private int _claimed;

    /// <summary>The paths being dropped, as they were when the drop landed.</summary>
    public IReadOnlyList<string> Sources { get; } = sources;

    /// <summary>The folder they would land in.</summary>
    public string Destination { get; } = destination;

    /// <summary>That folder's display name.</summary>
    public string DestinationLabel { get; } = destinationLabel;

    /// <summary>Takes the plan, once. Every later caller gets false and must do nothing.</summary>
    public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;
}
