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
    private const long SmallFileSizeLimit = 1 * 1024 * 1024;   // 1 MB
    private const int  FrontChunkSize     = 256 * 1024;         // 256 KB per load batch

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
            var jsonNode = JsonNode.Parse(text, nodeOptions: null,
                documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
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
    // Strategy: parse the first N items from the front of the file, then place a
    // single VirtualJsonNodeModel sentinel at the end.  As the user scrolls,
    // the VM calls LoadVirtualChunkAsync to load the next batch, which returns a
    // new sentinel if more content remains.  This gives seamless infinite-scroll
    // behaviour without pre-loading the back of the file.

    private async Task<LoadResult> LoadLargeAsync(string filePath, long fileSize, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Read front chunk
        var frontSize   = (int)Math.Min(FrontChunkSize, fileSize);
        var frontBuf    = new byte[frontSize];
        int frontRead   = await stream.ReadAsync(frontBuf, 0, frontSize, ct);
        var frontText   = Encoding.UTF8.GetString(frontBuf, 0, frontRead);

        // Determine root type
        var rootChar = frontText.TrimStart().FirstOrDefault();
        if (rootChar != '{' && rootChar != '[')
        {
            // Scalar root — fall back to full parse
            stream.Seek(0, SeekOrigin.Begin);
            var fullText  = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync(ct);
            var r         = 0;
            var scalarRoot = BuildModelFromJsonNode(JsonNode.Parse(fullText), null, null, null, ref r);
            return new LoadResult(scalarRoot, [], true, fileSize, r, null);
        }

        var isArray = rootChar == '[';

        // Parse the first batch of depth-1 children
        var (frontItems, frontEndChar) = ExtractFrontChildren(frontText, isArray);

        // Convert character position to absolute byte offset (UTF-8 aware)
        var frontEndBytes = (long)Encoding.UTF8.GetByteCount(
            frontText.AsSpan(0, (int)Math.Min(frontEndChar, (long)frontText.Length)));

        JsonNodeModel rootModel = isArray
            ? BuildStreamingArrayModel(frontItems, fileSize, frontEndBytes)
            : BuildStreamingObjectModel(frontItems, fileSize, frontEndBytes);

        var totalNodes = CountNodes(rootModel);
        return new LoadResult(rootModel, [], true, fileSize, totalNodes, null);
    }

    private static JsonArrayNodeModel BuildStreamingArrayModel(
        List<(string? key, JsonNode? node, long endOffset)> items,
        long fileSize, long frontEndBytes)
    {
        var arr = new JsonArrayNodeModel();
        for (var i = 0; i < items.Count; i++)
        {
            var nc    = 0;
            var child = BuildModelFromJsonNode(items[i].node, arr, null, i, ref nc);
            arr.Children.Add(child);
        }

        // Sentinel: if there is still content beyond what was parsed, add a virtual node
        if (frontEndBytes < fileSize - 10)
        {
            arr.Children.Add(new VirtualJsonNodeModel
            {
                Parent     = arr,
                Index      = items.Count,
                ByteOffset = frontEndBytes,
                EndOffset  = fileSize,
            });
        }

        return arr;
    }

    private static JsonObjectNodeModel BuildStreamingObjectModel(
        List<(string? key, JsonNode? node, long endOffset)> items,
        long fileSize, long frontEndBytes)
    {
        var obj = new JsonObjectNodeModel();
        foreach (var (key, node, _) in items)
        {
            var nc    = 0;
            var child = BuildModelFromJsonNode(node, obj, key, null, ref nc);
            obj.Children.Add(child);
        }

        if (frontEndBytes < fileSize - 10)
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

    // ── Virtual chunk loading (progressive scroll) ───────────────────────────
    // Reads the next batch of items starting at startOffset.
    // Returns the parsed items and the absolute file offset immediately after
    // the last complete item — the caller uses nextOffset to decide whether to
    // add a new sentinel (nextOffset < fileSize) or declare loading complete.

    public async Task<(List<(string? key, JsonNode? node)> items, long nextOffset)>
        LoadVirtualChunkAsync(
            string filePath, long startOffset, long fileSize, bool isArray, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(startOffset, SeekOrigin.Begin);

        var chunkLen = (int)Math.Min(FrontChunkSize, fileSize - startOffset);
        if (chunkLen <= 0) return ([], startOffset);

        var buf  = new byte[chunkLen];
        var read = await stream.ReadAsync(buf, 0, chunkLen, ct);
        var text = Encoding.UTF8.GetString(buf, 0, read);

        // Strip any leading comma/whitespace left over from the preceding item boundary,
        // then wrap in the appropriate opening bracket so ExtractFrontChildren can parse it.
        var leadTrimLen = text.Length - text.TrimStart(',', ' ', '\r', '\n', '\t').Length;
        var trimmed     = text[leadTrimLen..];
        var wrapped     = (isArray ? "[" : "{") + trimmed;

        var (items, endOffsetInWrapped) = ExtractFrontChildren(wrapped, isArray, 50);

        // Convert position in wrapped back to absolute file byte offset:
        //   wrapped = "[" + trimmed  →  position in trimmed = endOffsetInWrapped - 1
        //   position in text         = leadTrimLen + (endOffsetInWrapped - 1)
        var posInText      = leadTrimLen + (int)Math.Max(0L, endOffsetInWrapped - 1);
        var bytesConsumed  = (long)Encoding.UTF8.GetByteCount(
            text.AsSpan(0, Math.Min(posInText, text.Length)));
        var nextOffset     = startOffset + bytesConsumed;

        return (items.Select(x => (x.key, x.node)).ToList(), nextOffset);
    }

    // ── Structure estimation ─────────────────────────────────────────────────
    // Reads the first and last objects to estimate total item count and
    // whether the array is likely homogeneous (all objects share the same keys).
    // This is used to size the scrollbar without loading the whole file.

    public async Task<EstimateResult?> EstimateAsync(
        string filePath, long fileSize, bool isArray, CancellationToken ct)
    {
        const int SampleBytes = 64 * 1024;

        // ── First item ───────────────────────────────────────────────────
        await using var fs1 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var frontBuf  = new byte[(int)Math.Min(SampleBytes, fileSize)];
        int frontRead = await fs1.ReadAsync(frontBuf, 0, frontBuf.Length, ct);
        var frontText = Encoding.UTF8.GetString(frontBuf, 0, frontRead);

        var (frontItems, _) = ExtractFrontChildren(frontText, isArray, 1);
        if (frontItems.Count == 0) return null;

        var firstNode      = frontItems[0].node;
        var firstItemBytes = (long)Encoding.UTF8.GetByteCount(firstNode?.ToJsonString() ?? "null");
        var firstKeys      = GetObjectKeys(firstNode);

        // ── Last item ────────────────────────────────────────────────────
        await using var fs2 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var backStart  = Math.Max(0, fileSize - SampleBytes);
        fs2.Seek(backStart, SeekOrigin.Begin);
        var backBuf  = new byte[(int)(fileSize - backStart)];
        int backRead = await fs2.ReadAsync(backBuf, 0, backBuf.Length, ct);
        var backText = Encoding.UTF8.GetString(backBuf, 0, backRead);

        var (lastNode, lastItemBytes) = ExtractLastItem(backText, isArray);
        if (lastItemBytes <= 0) lastItemBytes = firstItemBytes;
        var lastKeys = GetObjectKeys(lastNode);

        // ── Estimate ─────────────────────────────────────────────────────
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

        // Walk back past whitespace
        var end = i - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end])) end--;
        if (end < 0) return (null, 0);

        int start;
        if (text[end] == '}' || text[end] == ']')
        {
            // Complex item — scan backward for matching open bracket
            var depth = 1;
            start = end - 1;
            while (start >= 0 && depth > 0)
            {
                if (text[start] == '}' || text[start] == ']') depth++;
                else if (text[start] == '{' || text[start] == '[') depth--;
                start--;
            }
            start++; // land on the opening bracket
        }
        else
        {
            // Scalar — scan back to comma or opening bracket
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

    private static IReadOnlyList<string> GetObjectKeys(JsonNode? node)
        => node is JsonObject obj ? obj.Select(kv => kv.Key).ToList() : [];

    // ── Item extraction (forward scan) ───────────────────────────────────────

    internal static (List<(string? key, JsonNode? node, long endOffset)> items, long endOffset)
        ExtractFrontChildren(string text, bool isArray, int maxItems = 50)
    {
        var items     = new List<(string? key, JsonNode? node, long endOffset)>();
        var depth     = 0;
        var inStr     = false;
        var escaped   = false;
        var i         = 0;

        // Skip past the opening bracket/brace
        while (i < text.Length && text[i] != (isArray ? '[' : '{')) i++;
        if (i >= text.Length) return (items, 0);
        i++; // skip opening

        var itemStart = i;

        while (i < text.Length && items.Count < maxItems)
        {
            var ch = text[i];
            if (escaped)     { escaped = false; i++; continue; }
            if (ch == '\\' && inStr) { escaped = true; i++; continue; }
            if (ch == '"') { inStr = !inStr; i++; continue; }
            if (inStr)     { i++; continue; }

            if (ch == '{' || ch == '[') { depth++; i++; continue; }
            if (ch == '}' || ch == ']')
            {
                if (depth == 0)
                {
                    // Root close: capture any trailing scalar item before breaking
                    var trailing = text[itemStart..i].Trim().TrimStart(',').Trim();
                    if (!string.IsNullOrWhiteSpace(trailing))
                        TryParseItem(trailing, isArray, items, i);
                    break;
                }
                depth--;
                if (depth == 0)
                {
                    // Complete complex item (object or array) closed
                    var span = text[itemStart..(i + 1)].Trim().TrimStart(',').Trim();
                    TryParseItem(span, isArray, items, i + 1);
                    itemStart = i + 1;
                }
                i++;
                continue;
            }
            if (depth == 0 && ch == ',')
            {
                // Scalar item ends at this comma
                var span = text[itemStart..i].Trim().TrimStart(',').Trim();
                if (!string.IsNullOrWhiteSpace(span))
                    TryParseItem(span, isArray, items, i);
                itemStart = i + 1;
            }
            i++;
        }

        return (items, i);
    }

    private static void TryParseItem(string span, bool isArray,
        List<(string? key, JsonNode? node, long endOffset)> items, long offset)
    {
        if (string.IsNullOrWhiteSpace(span)) return;
        try
        {
            if (!isArray && span.StartsWith('"'))
            {
                // Object property: "key": value
                var colonIdx = FindPropertyColon(span);
                if (colonIdx < 0) return;
                var rawKey    = span[1..colonIdx].TrimEnd('"').Trim();
                var valueText = span[(colonIdx + 1)..].Trim();
                var node      = JsonNode.Parse(valueText);
                items.Add((rawKey, node, offset));
            }
            else
            {
                var node = JsonNode.Parse(span);
                items.Add((null, node, offset));
            }
        }
        catch { /* skip unparseable items */ }
    }

    private static int FindPropertyColon(string span)
    {
        var inStr = true; // we start inside the key string (first char is '"')
        var i     = 1;    // skip leading quote
        while (i < span.Length)
        {
            if (span[i] == '"') { inStr = !inStr; i++; continue; }
            if (!inStr && span[i] == ':') return i;
            i++;
        }
        return -1;
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
