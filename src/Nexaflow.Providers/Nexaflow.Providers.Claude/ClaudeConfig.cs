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

    /// <summary>Output-token ceiling per completion; 0 = automatic (a per-model default).</summary>
    [ConfigDisplayName("Max Output Tokens (0 = auto)")]
    public int MaxOutputTokens { get; set; }
}
