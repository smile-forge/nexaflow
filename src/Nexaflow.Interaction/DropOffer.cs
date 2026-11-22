namespace Nexaflow.Interaction;

/// <summary>A target's answer to a drag over it.</summary>
/// <param name="Effect">What letting go would do.</param>
/// <param name="Description">What to say beside the pointer, in the reader's language; null says nothing.</param>
public sealed record DropOffer(DropEffect Effect, string? Description = null)
{
    /// <summary>It is this target's to answer and the answer is no, so no surface around it is asked.</summary>
    public static DropOffer Refused { get; } = new(DropEffect.None);
}
