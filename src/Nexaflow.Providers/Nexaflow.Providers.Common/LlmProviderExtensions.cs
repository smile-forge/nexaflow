using System.Text;

namespace Nexaflow.Providers.Common;

public static class LlmProviderExtensions
{
    /// <summary>
    /// The whole reply at once, for callers that act on a finished answer (ranking tools, describing an image,
    /// a background analysis): the stream read to its end. Null when the model wrote nothing.
    /// </summary>
    public static async Task<LlmResponse?> CompleteAsync(
        this ILlmProvider         provider,
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default)
    {
        var text = new StringBuilder();
        await foreach (var e in provider.StreamAsync(messages, ct))
            if (e is LlmTextDelta delta) text.Append(delta.Text);

        return text.Length == 0 ? null : new LlmResponse(text.ToString());
    }
}
