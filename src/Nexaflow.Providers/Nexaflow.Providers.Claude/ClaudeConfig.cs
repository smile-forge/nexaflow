using Nexaflow.Providers.Common;
using System.ComponentModel.DataAnnotations;

namespace Nexaflow.Providers.Claude;

public sealed class ClaudeConfig : IProviderConfig
{
    public string ConfigName   => "claude";
    public string FriendlyName => "Claude";

    [Required]
    [ConfigDisplayName("API Key")]
    public string ApiKey { get; set; } = "";

    [ConfigDisplayName("Base URL")]
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
}
