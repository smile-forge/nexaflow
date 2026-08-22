using System.Text;

namespace Nexaflow.Features.Logs.ViewModels;

public sealed record EncodingOption(string Name, Encoding Encoding)
{
    public override string ToString() => Name;
}
