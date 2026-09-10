using System.Collections.Generic;
using System.Windows;
using System.Threading;

namespace Nexaflow.Features.Common;

/// <summary>What a right-button drag can be told to do once it lands.</summary>
public enum DropChoice
{
    Copy,
    Move,
}

/// <summary>
/// A drop, read off the data object and held so it can be carried out later.
/// <para>
/// Capturing is not bookkeeping — an <see cref="IDataObject"/> handed to a drop handler is only
/// valid for the length of that callback, and a drop that asks the user a question is answered long
/// after it has returned. Everything the operation needs is therefore taken while it is still there.
/// </para>
/// <para>
/// It is also the thing that remembers having been carried out. A drop is one event, and the gap
/// between capturing it and answering it is exactly where a second answer can arrive: a menu that
/// outlives its own dismissal still holds live commands over this plan, and a copy chosen once was
/// being run again by a later, unrelated click. <see cref="TryClaim"/> is what makes that harmless
/// wherever it comes from, rather than at each place that might do it.
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

/// <summary>
/// An <see cref="IDropTarget"/> that can offer the choice rather than infer it: dragging with the
/// right button asks at the destination — copy or move — instead of requiring the modifier to have
/// been decided before the button came up.
/// <para>
/// Separate from <see cref="IDropTarget"/> because it is an extra a target may or may not have; a
/// target that does not implement it simply never offers a menu, and a right-drag onto it behaves
/// like the ordinary one.
/// </para>
/// </summary>
public interface IDropChoiceTarget
{
    /// <summary>
    /// Reads <paramref name="data"/> now and returns a plan that can be executed after the drop
    /// callback has returned. Null when there is nothing droppable to offer a choice about.
    /// </summary>
    DropPlan? Capture(IDataObject data, string destinationPath);

    /// <summary>Carries out a captured plan the way the user chose.</summary>
    void Execute(DropPlan plan, DropChoice choice);

    /// <summary>
    /// Whether <paramref name="choice"/> would actually do anything to <paramref name="plan"/>. A move
    /// onto the folder the sources already sit in is the case that matters: it has nothing to do, so it
    /// is offered greyed rather than as a button that swallows the click.
    /// </summary>
    bool CanExecute(DropPlan plan, DropChoice choice);

    /// <summary>
    /// The hover text for a right-drag, where the answer is not yet known — the counterpart to
    /// <see cref="IDropTarget.GetDropDescription"/>, which can say "Copy to X" because the modifier
    /// has already settled it. <paramref name="targetFolderName"/> is the folder under the cursor,
    /// or null over the background.
    /// </summary>
    string GetChoicePrompt(string? targetFolderName);
}
