using Nexaflow.Features.Common.Search;

namespace Nexaflow.Features.Json.Services;

/// <summary>
/// Streams a JSON file's top-level items straight off disk and tests each against a search matcher.
/// <para>
/// This exists because the viewer's display window holds at most a few hundred realised depth-1 nodes
/// out of a file that may hold millions — so searching what is loaded would silently mean "the items you
/// happen to have scrolled past". It reuses <see cref="JsonFileLoader.LoadVirtualChunkAsync"/>, the same
/// primitive every scroll already goes through, rather than adding a second way to read the file.
/// </para>
/// <para>Pure IO + CPU: no model is built and nothing on the page is touched, so it runs entirely off
/// the UI thread.</para>
/// </summary>
internal sealed class JsonTextScanner(JsonFileLoader loader)
{
    /// <param name="Index">The item's depth-1 position in the file — the one identity that survives the
    /// display window evicting the node.</param>
    /// <param name="ChunkFirstIndex">Depth-1 index of the first item in the chunk this match came from.</param>
    /// <param name="ChunkOffset">File offset that chunk was read from.</param>
    /// <remarks>The chunk pair is what makes a match <i>reachable</i>: the viewer's byte-offset index is
    /// sparse and only knows offsets it has already loaded, so without an exact seek point a reveal would
    /// crawl forward a batch at a time. Seeding these two lets the loader jump straight there.</remarks>
    internal readonly record struct ItemMatch(
        int Index, int ChunkFirstIndex, long ChunkOffset, string Label, string Preview);

    private const int PreviewChars = 200;

    /// <summary>
    /// Walks every depth-1 item from <paramref name="startOffset"/>, serialising each to compact JSON and
    /// testing the whole thing — so a match anywhere in an item's subtree reports on that item.
    /// </summary>
    /// <returns>The matches (capped), how many items were examined, and whether the file was fully read.</returns>
    public async Task<(List<ItemMatch> Matches, int Scanned, bool Complete)> ScanAsync(
        string filePath, long startOffset, long fileSize, bool isArray,
        TextSearchMatcher matcher, int cap, CancellationToken ct)
    {
        var matches = new List<ItemMatch>();
        var offset  = startOffset;
        var index   = 0;

        while (offset < fileSize && matches.Count < cap)
        {
            ct.ThrowIfCancellationRequested();

            var (items, nextOffset) = await loader.LoadVirtualChunkAsync(filePath, offset, fileSize, isArray, ct);

            // No progress means a malformed or unreadable region — stop rather than spin on it.
            if (items.Count == 0 || nextOffset <= offset) break;

            var chunkFirstIndex = index;
            foreach (var (key, node) in items)
            {
                var body = node?.ToJsonString() ?? "null";
                var text = key is null ? body : $"\"{key}\": {body}";

                if (matcher.Matches(text))
                    matches.Add(new ItemMatch(index, chunkFirstIndex, offset,
                                              key is null ? $"[{index}]" : $"\"{key}\"", Preview(text)));

                index++;
                if (matches.Count >= cap) break;
            }

            offset = nextOffset;
        }

        return (matches, index, offset >= fileSize && matches.Count < cap);
    }

    private static string Preview(string text) =>
        text.Length <= PreviewChars ? text : text[..PreviewChars] + "…";
}
