namespace Nexaflow.Features.Text.ViewModels;

/// <summary>
/// Singleton that tracks whichever TextViewModel is currently active.
/// Query handlers read this to find the target for search operations.
/// </summary>
public sealed class ActiveTextViewTracker
{
    public static ActiveTextViewTracker Instance { get; } = new();

    public TextViewModel? Current { get; set; }
}
