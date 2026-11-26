namespace Nexaflow.Interaction;

/// <summary>One question put to an <see cref="IDropTarget"/>: what is over it, where, and how it is held.</summary>
/// <param name="Payload">What is carried: the drag itself while it hovers, and what was taken of it once it is dropped.</param>
/// <param name="At">Where, on the surface the target was found on.</param>
/// <param name="Modifiers">The keys held, as the drag reports them — right whichever application has the focus.</param>
/// <param name="AsksChoice">Whether the drag is held with the right button, so the target may ask what to do rather than
/// infer it. Still true at the drop, when the button has come up.</param>
public sealed record DropRequest(IDragPayload Payload, DropPoint At, KeyModifiers Modifiers = KeyModifiers.None, bool AsksChoice = false);
