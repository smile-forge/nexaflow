using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.Claude;

public sealed class ClaudeConfig : IProviderConfig
{
    public string ConfigName   => "claude";
    public string FriendlyName => "Claude";

    [ConfigDisplayName("API Key")]
    public string ApiKey { get; set; } = "";

    [ConfigDisplayName("Base URL")]
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
}
