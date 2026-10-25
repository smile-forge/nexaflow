using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json.Nodes;

namespace Nexaflow.Providers.XAI;

/// <summary>What xAI says of one language model: whether it takes pictures, and how many tokens it reads.</summary>
internal sealed record XAIModelDetails(bool TakesImages, int? ContextLength);

/// <summary>
/// What xAI says of its language models, asked of the endpoints it serves beside the OpenAI-compatible ones: which there
/// are (<c>/language-models</c> — <c>/models</c> names its picture and video models too), which take pictures (each one's
/// <c>input_modalities</c>), and how much each reads (<c>/models/{id}</c>'s <c>context_length</c>). Asked through the SDK
/// client's own pipeline, so the key, retries and failures are the chat requests' own.
/// </summary>
/// <param name="endpoint">The API the client was built for, which the paths are asked of.</param>
internal sealed class XAIModelCatalog(OpenAIClient client, Uri endpoint)
{
    /// <summary>xAI's language models, sorted.</summary>
    public async Task<IReadOnlyList<string>> LanguageModelsAsync(CancellationToken ct)
        => ReadLanguageModels(await GetAsync("language-models", ct));

    public async Task<XAIModelDetails> DetailsAsync(string model, CancellationToken ct)
    {
        var id        = Uri.EscapeDataString(model);
        var described = GetAsync($"language-models/{id}", ct);
        var measured  = GetAsync($"models/{id}", ct);
        return new XAIModelDetails(TakesImages(await described), ContextLength(await measured));
    }

    internal static IReadOnlyList<string> ReadLanguageModels(JsonNode? list)
        => list?["models"] is JsonArray models
            ? models.Select(m => m?["id"]?.GetValue<string>()).OfType<string>().Order().ToList()
            : [];

    internal static bool TakesImages(JsonNode? model)
        => model?["input_modalities"] is JsonArray inputs && inputs.Any(i => i?.GetValue<string>() == "image");

    internal static int? ContextLength(JsonNode? model)
        => model?["context_length"]?.GetValue<int>() is > 0 and var tokens ? tokens : null;

    private async Task<JsonNode?> GetAsync(string path, CancellationToken ct)
    {
        using var message = client.Pipeline.CreateMessage();
        message.Request.Method = "GET";
        message.Request.Uri    = new Uri($"{endpoint.AbsoluteUri.TrimEnd('/')}/{path}");
        message.Apply(new RequestOptions { CancellationToken = ct });

        await client.Pipeline.SendAsync(message);
        var response = message.Response!;
        if (response.IsError) throw new ClientResultException(response);
        return JsonNode.Parse(response.Content.ToString());
    }
}
