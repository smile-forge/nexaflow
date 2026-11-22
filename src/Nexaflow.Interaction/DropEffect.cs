namespace Nexaflow.Interaction;

/// <summary>What a drop would do with what was dragged.</summary>
public enum DropEffect
{
    None,
    Copy,

    /// <summary>
    /// The target takes it away itself. Files dragged from another application are still never reported as moved: that
    /// is leave for the source to delete them, and the target has done, or queued, the whole move.
    /// </summary>
    Move,

    Link,
}
