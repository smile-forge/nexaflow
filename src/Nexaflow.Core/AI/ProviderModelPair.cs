namespace Nexaflow.Core.AI;

public sealed class ProviderModelPair
{
    public string Id           { get; set; } = Guid.NewGuid().ToString("N");
    public string ProviderName { get; set; } = string.Empty;
    public string Model        { get; set; } = string.Empty;
}
