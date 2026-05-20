using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.OpenAI;

public sealed class OpenAIConfig : IProviderConfig
{
    public string ConfigName   => "openai";
    public string FriendlyName => "OpenAI";

    [ConfigDisplayName("API Key")]
    public string ApiKey { get; set; } = string.Empty;

    [ConfigDisplayName("Base URL (optional)")]
    public string BaseUrl { get; set; } = string.Empty;
}
