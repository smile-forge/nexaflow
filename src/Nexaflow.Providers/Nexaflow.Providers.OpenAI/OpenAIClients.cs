using System.ClientModel;
using OpenAI;

namespace Nexaflow.Providers.OpenAI;

/// <summary>The SDK client every OpenAI provider builds from its config: the API key, and a base URL when one is
/// set (an OpenAI-compatible server).</summary>
internal static class OpenAIClients
{
    public static OpenAIClient Create(OpenAIConfig config)
    {
        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            options.Endpoint = new Uri(config.BaseUrl);
        return new OpenAIClient(new ApiKeyCredential(config.ApiKey), options);
    }
}
