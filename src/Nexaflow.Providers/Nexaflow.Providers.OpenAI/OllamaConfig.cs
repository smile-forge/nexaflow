using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.OpenAI;

public sealed class OpenAIConfig : IProviderConfig
{
    public string ConfigName   => "openai";
    public string FriendlyName => "OpenAI";

    [ConfigDisplayName("OpenAI URL")]
    public string Url { get; set; } = "http://localhost:11434";
}
