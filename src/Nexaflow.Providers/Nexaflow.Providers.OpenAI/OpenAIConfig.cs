using Nexaflow.Providers.Common;
using System.ComponentModel.DataAnnotations;

namespace Nexaflow.Providers.OpenAI;

public sealed class OpenAIConfig : IProviderConfig
{
    public string ConfigName   => "openai";
    public string FriendlyName => "OpenAI";

    [Required]
    [Secret]
    [ConfigDisplayName("OpenAI.Config.OpenAI.ApiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [ConfigDisplayName("OpenAI.Config.OpenAI.BaseUrl")]
    public string BaseUrl { get; set; } = string.Empty;
}
