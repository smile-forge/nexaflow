using Nexaflow.IO.Common;

namespace Nexaflow.Visuals.Text.Editor;

/// <summary>An entry in the editor's line-ending selector. <see cref="LineEnding.Preserve"/> keeps whatever
/// the file already uses; any other value normalizes the whole document on save.</summary>
public sealed record LineEndingOption(string Name, LineEnding Eol)
{
    public override string ToString() => Name;
}
