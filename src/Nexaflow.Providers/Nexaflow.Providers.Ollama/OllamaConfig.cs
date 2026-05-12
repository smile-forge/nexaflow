using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Ollama;

public sealed class OllamaConfig : IProviderConfig
{
    public string ConfigName   => "ollama";
    public string FriendlyName => "Ollama";

    [ConfigDisplayName("Ollama URL")]
    public string Url { get; set; } = "http://localhost:11434";

    [ConfigDisplayName("Model")]
    public string Model { get; set; } = "llama3.2";
}
