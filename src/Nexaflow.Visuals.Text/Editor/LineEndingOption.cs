using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>An entry in the editor's line-ending selector. <see cref="LineEnding.Preserve"/> keeps whatever
/// the file already uses; any other value normalizes the whole document on save. <see cref="Name"/> is what it is
/// known by, in every language; <see cref="Display"/> what the selector shows, where that is words rather than the
/// terminator's own name.</summary>
public sealed record LineEndingOption(string Name, LineEnding Eol, string? Display = null)
{
    public override string ToString() => Display ?? Name;
}
