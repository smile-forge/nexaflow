using Nexaflow.Providers.Common;
using System.ComponentModel.DataAnnotations;

namespace Nexaflow.Providers.Claude;

public sealed class ClaudeConfig : IProviderConfig
{
    public string ConfigName   => "claude";
    public string FriendlyName => "Claude";

    [Required]
    [Secret]
    [ConfigDisplayName("Claude.Config.Claude.ApiKey")]
    public string ApiKey { get; set; } = "";

    [ConfigDisplayName("Claude.Config.Claude.BaseUrl")]
    public string BaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>Output-token ceiling per completion; 0 = automatic (a per-model default).</summary>
    [ConfigDisplayName("Claude.Config.Claude.MaxOutputTokens")]
    public int MaxOutputTokens { get; set; }
}
