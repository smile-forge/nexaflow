using Nexaflow.Providers.Common;
using System.ComponentModel.DataAnnotations;

namespace Nexaflow.Providers.Gemini;

public sealed class GeminiConfig : IProviderConfig
{
    public string ConfigName   => "gemini";
    public string FriendlyName => "Gemini";

    [Required]
    [Secret]
    [ConfigDisplayName("Gemini.Config.Gemini.ApiKey")]
    public string ApiKey { get; set; } = string.Empty;
}
