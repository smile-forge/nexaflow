using Nexaflow.Providers.Common;

namespace Nexaflow.Core.AI;

public sealed class ProviderModelPair
{
    public string Id               { get; set; } = Guid.NewGuid().ToString("N");
    public string ProviderName     { get; set; } = string.Empty;
    public string Model            { get; set; } = string.Empty;

    /// <summary>The kind of model — which ability rows can use this column. Saved grids that predate kinds
    /// held language models only, which is the default.</summary>
    public ModelKind Kind          { get; set; } = ModelKind.Language;

    /// <summary>File name of the plugin DLL that supplies this provider (e.g. "Nexaflow.Providers.Claude.dll").</summary>
    public string AssemblyFileName { get; set; } = string.Empty;
}
