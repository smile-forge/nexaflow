using Nexaflow.Features.Json.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Json.Services;

public sealed record EstimateResult(
    int   EstimatedCount,
    long  AvgItemBytes,
    bool  IsLikelyHomogeneous);

public sealed record LoadResult(
    JsonNodeModel? Root,
    List<long>     ChildOffsets,
    bool           IsLargeFile,
    long           FileSizeBytes,
    int            NodeCount,
    string?        ErrorMessage);

internal sealed class JsonFileLoader
{
    private const long SmallFileSizeLimit  = 1 * 1024 * 1024;
    private const int  FrontChunkSize      = 256 * 1024;
    private const int  MaxChunkSize        = 8  * 1024 * 1024;   // grow to here if one item won't fit
    private const int  BatchItemCount      = 50;                  // depth-1 items per load batch

    private static readonly JsonReaderOptions s_readerOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling     = JsonCommentHandling.Skip,
        MaxDepth            = 64,
    };

    private static readonly JsonDocumentOptions s_documentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling     = JsonCommentHandling.Skip,
        MaxDepth            = 64,
    };

    public async Task<LoadResult> LoadAsync(string filePath, CancellationToken ct)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return new LoadResult(null, [], false, 0, 0, $"File not found: {filePath}");

            return info.Length <= SmallFileSizeLimit
                ? await LoadSmallAsync(filePath, info.Length, ct)
                : await LoadLargeAsync(filePath, info.Length, ct);
        }
        catch (OperationCanceledException) { return new LoadResult(null, [], false, 0, 0, null); }
        catch (Exception ex) { return new LoadResult(null, [], false, 0, 0, $"Error loading file: {ex.Message}"); }
    }

    // ── Small file path ──────────────────────────────────────────────────────

    private static async Task<LoadResult> LoadSmallAsync(string filePath, long fileSize, CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8, ct);
        try
        {
            var jsonNode = JsonNode.Parse(text, nodeOptions: null, documentOptions: s_documentOptions);
            var nodeCount = 0;
            var root = BuildModelFromJsonNode(jsonNode, parent: null, key: null, index: null, ref nodeCount);
            return new LoadResult(root, [], false, fileSize, nodeCount, null);
        }
        catch (JsonException ex)
        {
            return new LoadResult(null, [], false, fileSize, 0, $"Invalid JSON: {ex.Message}");
        }
    }

    // ── Large file path ──────────────────────────────────────────────────────

    private async Task<LoadResult> LoadLargeAsync(string filePath, long fileSize, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Read front chunk; grow if even the first item is bigger than the chunk
        var (frontBuf, frontRead) = await ReadGrowingChunkAsync(stream, 0, fileSize, ct);

        // Find first structural char (skipping BOM/whitespace)
        var rootStart = FindFirstStructuralChar(frontBuf.AsSpan(0, frontRead), out var rootChar);
        if (rootChar != (byte)'[' && rootChar != (byte)'{')
        {
            // Scalar root — fall back to full parse
            stream.Seek(0, SeekOrigin.Begin);
            var fullText  = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync(ct);
            var r         = 0;
            var scalarRoot = BuildModelFromJsonNode(JsonNode.Parse(fullText), null, null, null, ref r);
            return new LoadResult(scalarRoot, [], true, fileSize, r, null);
        }

        var isArray = rootChar == (byte)'[';

        // Parse front items directly from the buffer (no wrapping needed — chunk starts with the real bracket)
        var frontItems = new List<(string? key, JsonNode? node)>();
        var bytesConsumedInContent = ParseItemsFromContent(
            frontBuf.AsSpan(rootStart, frontRead - rootStart),
            isArray, BatchItemCount, frontItems);

        var frontEndBytes = rootStart + bytesConsumedInContent;

        JsonNodeModel rootModel = isArray
            ? BuildStreamingArrayModel (frontItems, fileSize, frontEndBytes)
            : BuildStreamingObjectModel(frontItems, fileSize, frontEndBytes);

        var totalNodes = CountNodes(rootModel);
        return new LoadResult(rootModel, [], true, fileSize, totalNodes, null);
    }

    // ── Streaming root builders ──────────────────────────────────────────────

    private static JsonArrayNodeModel BuildStreamingArrayModel(
        List<(string? key, JsonNode? node)> frontItems, long fileSize, long frontEndBytes)
    {
        var arr = new JsonArrayNodeModel();
        for (var i = 0; i < frontItems.Count; i++)
        {
            var nc = 0;
            arr.Children.Add(BuildModelFromJsonNode(frontItems[i].node, arr, null, i, ref nc));
        }

        if (frontEndBytes < fileSize - 2)
        {
            arr.Children.Add(new VirtualJsonNodeModel
            {
                Parent     = arr,
                Index      = frontItems.Count,
                ByteOffset = frontEndBytes,
                EndOffset  = fileSize,
            });
        }
        return arr;
    }

    private static JsonObjectNodeModel BuildStreamingObjectModel(
        List<(string? key, JsonNode? node)> frontItems, long fileSize, long frontEndBytes)
    {
        var obj = new JsonObjectNodeModel();
        foreach (var (key, node) in frontItems)
        {
            var nc = 0;
            obj.Children.Add(BuildModelFromJsonNode(node, obj, key, null, ref nc));
        }
        if (frontEndBytes < fileSize - 2)
        {
            obj.Children.Add(new VirtualJsonNodeModel
            {
                Parent     = obj,
                ByteOffset = frontEndBytes,
                EndOffset  = fileSize,
            });
        }
        return obj;
    }

    // ── Virtual-chunk loading (progressive scroll) ───────────────────────────
    // Reads the next batch starting at startOffset.  Returns the parsed items
    // and the absolute file offset just past the last successfully consumed item.
    // Uses Utf8JsonReader (battle-tested JSON tokenizer) — handles strings with
    // embedded braces, escape sequences, Unicode, etc. correctly.

    public async Task<(List<(string? key, JsonNode? node)> items, long nextOffset)>
        LoadVirtualChunkAsync(
            string filePath, long startOffset, long fileSize, bool isArray, CancellationToken ct)
    {
        if (startOffset >= fileSize - 2) return ([], startOffset);

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Grow chunk if no items found — handles items larger than initial chunk size
        var chunkSize = FrontChunkSize;
        while (true)
        {
            var (buf, bytesRead) = await ReadGrowingChunkAsync(stream, startOffset, fileSize, ct, chunkSize);
            if (bytesRead <= 0) return ([], startOffset);

            var (items, consumedInChunk) = ParseChunkWrapped(buf.AsSpan(0, bytesRead), isArray, BatchItemCount);

            if (items.Count > 0)
                return (items, startOffset + consumedInChunk);

            // If we read everything available (EOF) or hit the chunk cap, stop trying
            if (bytesRead < chunkSize || chunkSize >= MaxChunkSize)
                return (items, startOffset + Math.Max(consumedInChunk, 1));

            chunkSize = Math.Min(chunkSize * 2, MaxChunkSize);
        }
    }

    // ── Estimation (for scrollbar sizing) ────────────────────────────────────

    public async Task<EstimateResult?> EstimateAsync(
        string filePath, long fileSize, bool isArray, CancellationToken ct)
    {
        const int SampleBytes = 64 * 1024;

        // Front sample: parse the first object only
        await using var fs1 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var frontBuf  = new byte[(int)Math.Min(SampleBytes, fileSize)];
        int frontRead = await fs1.ReadAsync(frontBuf, 0, frontBuf.Length, ct);

        var rootStart = FindFirstStructuralChar(frontBuf.AsSpan(0, frontRead), out var rootChar);
        if (rootChar != (byte)'[' && rootChar != (byte)'{') return null;

        var frontItems = new List<(string? key, JsonNode? node)>();
        ParseItemsFromContent(frontBuf.AsSpan(rootStart, frontRead - rootStart), isArray, 1, frontItems);
        if (frontItems.Count == 0) return null;

        var firstNode      = frontItems[0].node;
        var firstItemBytes = (long)Encoding.UTF8.GetByteCount(firstNode?.ToJsonString() ?? "null");
        var firstKeys      = GetObjectKeys(firstNode);

        // Back sample: parse the LAST object
        await using var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var backStart  = Math.Max(0, fileSize - SampleBytes);
        fs2.Seek(backStart, SeekOrigin.Begin);
        var backBuf  = new byte[(int)(fileSize - backStart)];
        int backRead = await fs2.ReadAsync(backBuf, 0, backBuf.Length, ct);

        var (lastNode, lastItemBytes) = ExtractLastItem(Encoding.UTF8.GetString(backBuf, 0, backRead), isArray);
        if (lastItemBytes <= 0) lastItemBytes = firstItemBytes;
        var lastKeys = GetObjectKeys(lastNode);

        var avgBytes       = Math.Max(1, (firstItemBytes + lastItemBytes) / 2);
        var estimatedCount = (int)Math.Clamp(fileSize / avgBytes, 1, 10_000_000);

        var isHomogeneous = firstKeys.Count > 0 && lastKeys.Count > 0
            && firstKeys.OrderBy(k => k).SequenceEqual(lastKeys.OrderBy(k => k));

        return new EstimateResult(estimatedCount, avgBytes, isHomogeneous);
    }

    private static (JsonNode? node, long itemBytes) ExtractLastItem(string text, bool isArray)
    {
        var closeChar = isArray ? ']' : '}';
        var i = text.Length - 1;
        while (i >= 0 && text[i] != closeChar) i--;
        if (i < 0) return (null, 0);

        var end = i - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
        if (end < 0) return (null, 0);

        int start;
        if (text[end] == '}' || text[end] == ']')
        {
            var depth = 1;
            start = end - 1;
            while (start >= 0 && depth > 0)
            {
                if (text[start] == '}' || text[start] == ']') depth++;
                else if (text[start] == '{' || text[start] == '[') depth--;
                start--;
            }
            start++;
        }
        else
        {
            start = end;
            while (start > 0 && text[start - 1] != ',' &&
                   text[start - 1] != '[' && text[start - 1] != '{')
                start--;
        }

        var span = text[start..(end + 1)].Trim().TrimStart(',').Trim();
        if (string.IsNullOrWhiteSpace(span)) return (null, 0);

        try
        {
            var bytes = (long)Encoding.UTF8.GetByteCount(span);
            if (!isArray && span.StartsWith('"'))
            {
                var colonIdx = FindPropertyColon(span);
                if (colonIdx >= 0)
                    return (JsonNode.Parse(span[(colonIdx + 1)..].Trim()), bytes);
            }
            return (JsonNode.Parse(span), bytes);
        }
        catch { return (null, 0); }
    }

    private static int FindPropertyColon(string span)
    {
        var inStr = true;
        var i     = 1;
        while (i < span.Length)
        {
            if (span[i] == '"') { inStr = !inStr; i++; continue; }
            if (!inStr && span[i] == ':') return i;
            i++;
        }
        return -1;
    }

    private static IReadOnlyList<string> GetObjectKeys(JsonNode? node)
        => node is JsonObject obj ? obj.Select(kv => kv.Key).ToList() : [];

    // ── Core Utf8JsonReader parsers ──────────────────────────────────────────

    // Parses items from a mid-file chunk.  Strips leading whitespace/comma,
    // prepends the opening bracket, then runs the streaming reader.  Returns
    // how many bytes of the ORIGINAL chunk we consumed (so the caller can
    // advance startOffset precisely).
    private static (List<(string? key, JsonNode? node)> items, long bytesConsumed) ParseChunkWrapped(
        ReadOnlySpan<byte> chunk, bool isArray, int maxItems)
    {
        var items = new List<(string? key, JsonNode? node)>();

        // Strip leading commas and whitespace from the chunk
        int leadingTrim = 0;
        while (leadingTrim < chunk.Length && IsStructuralWhitespaceOrComma(chunk[leadingTrim]))
            leadingTrim++;
        if (leadingTrim >= chunk.Length) return (items, leadingTrim);

        // Wrap: prepend opening bracket so Utf8JsonReader treats this as a fresh array/object
        var wrappedLen = chunk.Length - leadingTrim + 1;
        var wrapped    = new byte[wrappedLen];
        wrapped[0]     = (byte)(isArray ? '[' : '{');
        chunk[leadingTrim..].CopyTo(wrapped.AsSpan(1));

        var consumedInWrapped = ParseItemsFromContent(wrapped, isArray, maxItems, items);

        // The fake bracket at wrapped[0] adds 1 to BytesConsumed; subtract it back out.
        var consumedInChunkPastTrim = Math.Max(0, consumedInWrapped - 1);
        return (items, leadingTrim + consumedInChunkPastTrim);
    }

    // Parses items from bytes whose first non-whitespace char is the opening
    // bracket.  Returns total BytesConsumed (in the span) past the last
    // successfully parsed item.
    private static long ParseItemsFromContent(
        ReadOnlySpan<byte> data, bool isArray, int maxItems,
        List<(string? key, JsonNode? node)> outItems)
    {
        var reader = new Utf8JsonReader(data, isFinalBlock: false, new JsonReaderState(s_readerOptions));
        long lastGoodBytes = 0;

        try
        {
            // Advance past the opening bracket
            if (!reader.Read()) return 0;
            var expectedOpen = isArray ? JsonTokenType.StartArray : JsonTokenType.StartObject;
            if (reader.TokenType != expectedOpen) return 0;
            lastGoodBytes = reader.BytesConsumed;

            while (outItems.Count < maxItems)
            {
                // Advance to the next token: either a value/property or the end bracket
                if (!reader.Read()) break;
                if (reader.TokenType == JsonTokenType.EndArray ||
                    reader.TokenType == JsonTokenType.EndObject) break;

                string? key = null;
                if (!isArray)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName) break;
                    key = reader.GetString();
                    if (!reader.Read()) break;
                }

                // Reader is now positioned at the start of the value (object, array, or scalar).
                // JsonNode.Parse(ref reader) consumes one complete value of any shape.
                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(ref reader);
                }
                catch (JsonException)
                {
                    // Value cut off at chunk boundary — stop here, the caller will read more bytes next time
                    break;
                }

                outItems.Add((key, node));
                lastGoodBytes = reader.BytesConsumed;
            }
        }
        catch (JsonException)
        {
            // Truncated stream or malformed JSON in this chunk — return what we got
        }

        return lastGoodBytes;
    }

    // ── Byte stream helpers ──────────────────────────────────────────────────

    private static int FindFirstStructuralChar(ReadOnlySpan<byte> data, out byte rootChar)
    {
        rootChar = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            // Skip UTF-8 BOM (EF BB BF) at the very start
            if (i == 0 && data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            {
                i = 2; continue;
            }
            if (IsStructuralWhitespaceOrComma(b)) continue;
            rootChar = b;
            return i;
        }
        return 0;
    }

    private static bool IsStructuralWhitespaceOrComma(byte b)
        => b == (byte)',' || b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n';

    // Reads up to `initialSize` bytes from `stream` starting at `offset`.
    // Used for the initial chunk; chunk growing is handled by the caller.
    private static async Task<(byte[] buf, int read)> ReadGrowingChunkAsync(
        FileStream stream, long offset, long fileSize, CancellationToken ct, int initialSize = FrontChunkSize)
    {
        var size = (int)Math.Min(initialSize, fileSize - offset);
        if (size <= 0) return ([], 0);
        var buf = new byte[size];
        stream.Seek(offset, SeekOrigin.Begin);
        var read = await stream.ReadAsync(buf, 0, size, ct);
        return (buf, read);
    }

    // ── Node model builder ───────────────────────────────────────────────────

    internal static JsonNodeModel BuildModelFromJsonNode(
        JsonNode? node, JsonNodeModel? parent, string? key, int? index, ref int nodeCount)
    {
        nodeCount++;
        JsonNodeModel model;

        switch (node)
        {
            case JsonObject obj:
            {
                var objModel = new JsonObjectNodeModel { Key = key, Index = index, Parent = parent };
                foreach (var (propKey, propValue) in obj)
                {
                    var child = BuildModelFromJsonNode(propValue, objModel, propKey, null, ref nodeCount);
                    objModel.Children.Add(child);
                }
                model = objModel;
                break;
            }
            case JsonArray arr:
            {
                var arrModel = new JsonArrayNodeModel { Key = key, Index = index, Parent = parent };
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = BuildModelFromJsonNode(arr[i], arrModel, null, i, ref nodeCount);
                    arrModel.Children.Add(child);
                }
                model = arrModel;
                break;
            }
            case JsonValue val:
            {
                var kind = val.GetValueKind();
                object? value = kind switch
                {
                    JsonValueKind.String => val.GetValue<string>(),
                    JsonValueKind.True   => true,
                    JsonValueKind.False  => false,
                    JsonValueKind.Null   => null,
                    JsonValueKind.Number => TryGetNumber(val),
                    _                   => val.ToString(),
                };
                model = new JsonValueNodeModel { Key = key, Index = index, Parent = parent, Value = value, ValueKind = kind };
                break;
            }
            default:
                model = new JsonValueNodeModel { Key = key, Index = index, Parent = parent, Value = null, ValueKind = JsonValueKind.Null };
                break;
        }

        return model;
    }

    private static object TryGetNumber(JsonValue val)
    {
        if (val.TryGetValue<long>(out var l))   return l;
        if (val.TryGetValue<double>(out var d)) return d;
        return val.ToString();
    }

    private static int CountNodes(JsonNodeModel node)
    {
        var count = 1;
        if (node is JsonObjectNodeModel obj)
            foreach (var c in obj.Children) count += CountNodes(c);
        else if (node is JsonArrayNodeModel arr)
            foreach (var c in arr.Children) count += CountNodes(c);
        return count;
    }
}
