using Nexaflow.IO.Common;
using System.Text;

namespace Nexaflow.Features.Text.ViewModels;

public sealed record EncodingOption(string Name, Encoding Encoding)
{
    /// <summary>What the encoding selector and the context preview show. <see cref="Name"/> stays the invariant
    /// name the assistant's context carries and code compares; only a name that is a word is given its own label.</summary>
    public string Label { get; init; } = Name;

    public override string ToString() => Label;
}

public sealed record SplitModeOption(SplitMode Mode, string Label)
{
    public override string ToString() => Label;
}
