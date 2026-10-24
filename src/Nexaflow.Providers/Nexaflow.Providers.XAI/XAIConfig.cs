using Nexaflow.Providers.Common;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Nexaflow.Providers.XAI;

public sealed class XAIConfig : IProviderConfig
{
    /// <summary>xAI's API, where requests go while the base URL is left empty.</summary>
    public const string DefaultEndpoint = "https://api.x.ai/v1";

    public string ConfigName   => "xai";
    public string FriendlyName => "xAI";

    [Required]
    [Secret]
    [ConfigDisplayName("XAI.Config.XAI.ApiKey")]
    public string ApiKey { get; set; } = string.Empty;

    [ConfigDisplayName("XAI.Config.XAI.BaseUrl")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Where requests go: the base URL when one is set (a gateway in front of xAI), xAI's API otherwise — never
    /// the OpenAI SDK's own default, which would hand the key to OpenAI.</summary>
    [JsonIgnore]
    public Uri Endpoint => new(string.IsNullOrWhiteSpace(BaseUrl) ? DefaultEndpoint : BaseUrl);
}
