namespace Nexaflow.Core.AI;

public sealed class ProviderModelPair
{
    public string Id               { get; set; } = Guid.NewGuid().ToString("N");
    public string ProviderName     { get; set; } = string.Empty;
    public string Model            { get; set; } = string.Empty;
    /// <summary>File name of the plugin DLL that supplies this provider (e.g. "Nexaflow.Providers.Claude.dll").</summary>
    public string AssemblyFileName { get; set; } = string.Empty;
}
