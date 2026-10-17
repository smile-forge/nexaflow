using System.Text;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.Common;

namespace Nexaflow.Providers.AiHost;

/// <summary>
/// Pages of writing read in the AI host (<see cref="OwnedToolKind.PageReading"/>): where a picture holds writing, by
/// PP-OCRv5's text detector on the CPU; and a page read into its blocks by Unlimited-OCR, decoded as its makers decode it
/// — greedy, each token seeing the page and only the last 128 it wrote, no 35-token run written twice within 128 — with
/// the special tokens it marks each block with kept in its text.
/// </summary>
public sealed class OwnedPageReading : OwnedToolFront
{
    /// <summary>What the reader is asked: its makers' instruction for a page read into blocks.</summary>
    public const string Instruction = "document parsing.";

    private static readonly ReplyDecoding Makers = new(WrittenWindow: 128, RepeatBan: new RepeatBan(35, 128), WritesControlTokens: true);

    public OwnedPageReading(IBackgroundActivityManager activities, IModelAssetStore assets, IAiHost host, HostModelSetup setup, ComputeHardwareReport hardware)
        : this(new OwnedModelRuntime(activities, assets, host, setup, hardware), OwnedModels.Tools.FirstOrDefault(t => t.Kind == OwnedToolKind.PageReading))
    {
    }

    internal OwnedPageReading(OwnedModelRuntime runtime, OwnedTool? tool) : base(runtime, tool)
    {
    }

    /// <summary>Where the picture in <paramref name="pixels"/> — interleaved 8-bit RGBA, nothing else — holds writing.
    /// Never downloads.</summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, or the host could not look.</exception>
    public async Task<IReadOnlyList<TextRegion>> FindTextAsync(string pixels, int width, int height, CancellationToken ct)
    {
        try
        {
            var parts = await PlacedAsync([ReadingParts.TextFinder], ct);
            await foreach (var reply in Owned.Host.SendAsync(id => new FindTextInPicture(id, parts[ReadingParts.TextFinder], pixels, width, height),
                                                             OwnedModelRuntime.StallLimit, ct))
                if (reply is TextFoundInPicture found) return found.Regions;
            throw new InvalidOperationException("The AI host finished without saying where the text is.");
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            throw new InvalidOperationException($"The picture could not be looked at: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Reads the page in <paramref name="picture"/> — a PNG or JPEG file — and returns what the reader wrote, block marks
    /// and all, each piece handed to <paramref name="written"/> as it comes. <paramref name="waiting"/>: a person is
    /// watching for it, so it goes before background work. Never downloads.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tool is not on this machine, there is no room to run it, or the
    /// host could not read the page.</exception>
    public async Task<string> ReadPageAsync(string picture, bool waiting, IProgress<string>? written, CancellationToken ct)
    {
        try
        {
            var parts = await PlacedAsync([ReadingParts.Reader], ct);
            var turns = new[] { new ChatTurn(TurnRole.User, Instruction, [new ChatMedia(MediaKind.Image, picture, null)]) };
            var text = new StringBuilder();
            var priority = waiting ? RequestPriority.Interactive : RequestPriority.Job;
            await foreach (var reply in Owned.Host.SendAsync(id => new Generate(id, parts[ReadingParts.Reader], turns, null, priority, Thinks: false, Decoding: Makers),
                                                             OwnedModelRuntime.StallLimit, ct))
            {
                if (reply is not TextDelta delta) continue;
                text.Append(delta.Text);
                written?.Report(delta.Text);
            }
            return text.ToString();
        }
        catch (Exception ex) when (ex is AiHostException or LlmProviderException)
        {
            throw new InvalidOperationException($"The page could not be read: {ex.Message}", ex);
        }
    }
}
