using Nexaflow.IO.Common;
using System.Text;

namespace Nexaflow.Features.Text.ViewModels;

public sealed record EncodingOption(string Name, Encoding Encoding)
{
    public override string ToString() => Name;
}

public sealed record SplitModeOption(SplitMode Mode, string Label)
{
    public override string ToString() => Label;
}
