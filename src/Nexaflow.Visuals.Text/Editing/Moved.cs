namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What moving part of some content came to: the source it produced, where the caret goes, and the
/// stretch it wrote — what to mark out while it is still being carried.
/// </summary>
public readonly record struct Moved(string Source, int Caret, EditRange Wrote);
