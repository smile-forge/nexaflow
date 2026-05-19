using Nexaflow.Features.Json.Models;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nexaflow.Features.Json.Services;

public sealed record LoadResult(
    JsonNodeModel? Root,
    List<long>     ChildOffsets,
    bool           IsLargeFile,
    long           FileSizeBytes,
    int            NodeCount,
    string?        ErrorMessage);

internal sealed class JsonFileLoader
{
    private const long SmallFileSizeLimit = 1 * 1024 * 1024;
    private const int  FrontChunkSize     = 256 * 1024;
    private const int  BackChunkSize      = 128 * 1024;
    private const int  ScanChunkSize      = 64  * 1024;

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

    // ── Large file path ──────────────────────────────────────────────────────

    private async Task<LoadResult> LoadLargeAsync(string filePath, long fileSize, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Read front chunk
        var frontBuffer = new byte[Math.Min(FrontChunkSize, fileSize)];
        int frontRead = await stream.ReadAsync(frontBuffer, 0, frontBuffer.Length, ct);
        var frontText = Encoding.UTF8.GetString(frontBuffer, 0, frontRead);

        // Determine root type
        var rootChar = frontText.TrimStart().FirstOrDefault();
        if (rootChar != '{' && rootChar != '[')
        {
            // Fall back to small-file parse (might be a scalar)
            stream.Seek(0, SeekOrigin.Begin);
            var fullText = await new StreamReader(stream, Encoding.UTF8).ReadToEndAsync(ct);
            var r = 0;
            var root = BuildModelFromJsonNode(JsonNode.Parse(fullText), null, null, null, ref r);
            return new LoadResult(root, [], true, fileSize, r, null);
        }

        var isArray = rootChar == '[';

        // Parse front: extract first N complete depth-1 children
        var (frontChildren, frontEndOffset) = ExtractFrontChildren(frontText, isArray);

        // Read back chunk
        var backStart  = Math.Max(frontRead, fileSize - BackChunkSize);
        var backLength = (int)(fileSize - backStart);
        var backBuffer = new byte[backLength];
        stream.Seek(backStart, SeekOrigin.Begin);
        int backRead = await stream.ReadAsync(backBuffer, 0, backBuffer.Length, ct);
        var backText  = Encoding.UTF8.GetString(backBuffer, 0, backRead);

        // Parse back: extract last N complete depth-1 children
        var (backChildren, backStartInChunk) = ExtractBackChildren(backText, isArray, backStart);

        // Build root model with front + virtual + back children
        JsonNodeModel rootModel = isArray
            ? BuildLargeArrayModel(frontChildren, backChildren, fileSize, frontEndOffset, backStart + backStartInChunk)
            : BuildLargeObjectModel(frontChildren, backChildren, fileSize, frontEndOffset, backStart + backStartInChunk);

        int totalNodes = CountNodes(rootModel);
        return new LoadResult(rootModel, [], true, fileSize, totalNodes, null);
    }

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
                    // Root close: capture any trailing scalar item
                    var trailing = text[itemStart..i].Trim().TrimStart(',').Trim();
                    if (!string.IsNullOrWhiteSpace(trailing))
                        TryParseItem(trailing, isArray, items, i);
                    break;
                }
                depth--;
                if (depth == 0)
                {
                    // Complete complex item (object or array) found
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
        // Find the colon after the key string (accounting for nested quotes)
        var inStr  = true; // we start inside the key string
        var i = 1; // skip leading quote
        while (i < span.Length)
        {
            if (span[i] == '"') { inStr = !inStr; i++; continue; }
            if (!inStr && span[i] == ':') return i;
            i++;
        }
        return -1;
    }

    private static (List<(string? key, JsonNode? node, long offsetInChunk)> items, long startOffset)
        ExtractBackChildren(string text, bool isArray, long chunkAbsoluteStart)
    {
        var maxItems  = 50;
        var closeChar = isArray ? ']' : '}';

        // Find the root closing bracket from the end of the chunk
        var rootClose = text.Length - 1;
        while (rootClose >= 0 && text[rootClose] != closeChar) rootClose--;
        if (rootClose < 0) return ([], text.Length);

        // Backward scan from just before the root close, collecting item boundary positions.
        // We record: {/[ at depth 0 (start of a complex item), and , at depth 0 (scalar separators).
        var boundaries = new List<int>();
        boundaries.Add(rootClose); // end sentinel

        var depth = 0;
        var inStr = false;

        for (var i = rootClose - 1; i >= 0 && boundaries.Count < maxItems + 2; i--)
        {
            var ch = text[i];

            // Escape-aware quote detection for backward scan
            if (ch == '"')
            {
                var slashes = 0;
                for (var j = i - 1; j >= 0 && text[j] == '\\'; j--) slashes++;
                if (slashes % 2 == 0) inStr = !inStr; // unescaped quote
            }
            if (inStr) continue;

            if (ch == '}' || ch == ']') { depth++; continue; }
            if (ch == '{' || ch == '[')
            {
                if (depth == 0) boundaries.Add(i); // start of a top-level complex item
                else depth--;
                continue;
            }
            if (ch == ',' && depth == 0) boundaries.Add(i); // scalar separator
        }

        boundaries.Sort();

        var allItems = new List<(string? key, JsonNode? node, long offsetInChunk)>();
        for (var b = 0; b < boundaries.Count - 1; b++)
        {
            var start = boundaries[b];
            var end   = boundaries[b + 1];
            var span  = text[start..end].Trim().TrimStart(',').Trim();
            if (string.IsNullOrWhiteSpace(span)) continue;
            TryParseItem(span, isArray, allItems, chunkAbsoluteStart + start);
        }

        // Take last maxItems (closest to the end of the file)
        var result = allItems.Count <= maxItems
            ? allItems
            : allItems.GetRange(allItems.Count - maxItems, maxItems);

        long firstStart = result.Count > 0
            ? result[0].offsetInChunk - chunkAbsoluteStart
            : rootClose;
        return (result, firstStart);
    }

    private static JsonArrayNodeModel BuildLargeArrayModel(
        List<(string? key, JsonNode? node, long endOffset)> front,
        List<(string? key, JsonNode? node, long offsetInChunk)> back,
        long fileSize, long frontEnd, long backAbsStart)
    {
        var arrModel = new JsonArrayNodeModel();
        var idx = 0;

        // Front items
        foreach (var (_, node, _) in front)
        {
            var nodeCount = 0;
            var child = BuildModelFromJsonNode(node, arrModel, null, idx, ref nodeCount);
            arrModel.Children.Add(child);
            idx++;
        }

        // Virtual placeholder for the middle section (if gap exists)
        if (backAbsStart > frontEnd && backAbsStart < fileSize - 100)
        {
            var virtualNode = new VirtualJsonNodeModel
            {
                Parent     = arrModel,
                Index      = idx,
                ByteOffset = frontEnd,
                EndOffset  = backAbsStart,
            };
            arrModel.Children.Add(virtualNode);
            idx++;
        }

        // Back items
        foreach (var (_, node, _) in back)
        {
            var nodeCount = 0;
            var child = BuildModelFromJsonNode(node, arrModel, null, idx, ref nodeCount);
            arrModel.Children.Add(child);
            idx++;
        }

        return arrModel;
    }

    private static JsonObjectNodeModel BuildLargeObjectModel(
        List<(string? key, JsonNode? node, long endOffset)> front,
        List<(string? key, JsonNode? node, long offsetInChunk)> back,
        long fileSize, long frontEnd, long backAbsStart)
    {
        var objModel = new JsonObjectNodeModel();

        foreach (var (key, node, _) in front)
        {
            var nodeCount = 0;
            var child = BuildModelFromJsonNode(node, objModel, key, null, ref nodeCount);
            objModel.Children.Add(child);
        }

        if (backAbsStart > frontEnd && backAbsStart < fileSize - 100)
        {
            var virtualNode = new VirtualJsonNodeModel
            {
                Parent     = objModel,
                ByteOffset = frontEnd,
                EndOffset  = backAbsStart,
            };
            objModel.Children.Add(virtualNode);
        }

        foreach (var (key, node, _) in back)
        {
            var nodeCount = 0;
            var child = BuildModelFromJsonNode(node, objModel, key, null, ref nodeCount);
            objModel.Children.Add(child);
        }

        return objModel;
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

    // ── Virtual node loading ─────────────────────────────────────────────────

    public async Task<List<(string? key, JsonNode? node)>> LoadVirtualChunkAsync(
        string filePath, long startOffset, long endOffset, bool isArray, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(startOffset, SeekOrigin.Begin);

        var chunkLen = (int)Math.Min(endOffset - startOffset, 5 * 1024 * 1024);
        var buf  = new byte[chunkLen];
        var read = await stream.ReadAsync(buf, 0, chunkLen, ct);
        var text = Encoding.UTF8.GetString(buf, 0, read);

        // Strip any leading comma/whitespace left over from the preceding item boundary,
        // then wrap in the appropriate opening bracket so ExtractFrontChildren can parse it.
        var trimmed = text.TrimStart(',', ' ', '\r', '\n', '\t');
        var wrapped = (isArray ? "[" : "{") + trimmed;
        var (items, _) = ExtractFrontChildren(wrapped, isArray, int.MaxValue);
        return items.Select(x => (x.key, x.node)).ToList();
    }

    public async Task<JsonNodeModel?> LoadVirtualNodeAsync(
        string filePath, long byteOffset, JsonNodeModel parent, string? key, int? index, CancellationToken ct)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(byteOffset, SeekOrigin.Begin);

        var chunkSize = 32 * 1024;
        while (chunkSize <= 2 * 1024 * 1024)
        {
            var buf  = new byte[Math.Min(chunkSize, stream.Length - stream.Position)];
            var read = await stream.ReadAsync(buf, ct);
            if (read == 0) return null;
            var text = Encoding.UTF8.GetString(buf, 0, read);
            try
            {
                var jsonNode  = JsonNode.Parse(text);
                var nodeCount = 0;
                return BuildModelFromJsonNode(jsonNode, parent, key, index, ref nodeCount);
            }
            catch (JsonException) { chunkSize *= 2; stream.Seek(byteOffset, SeekOrigin.Begin); }
        }
        return null;
    }

    // ── Background offset scanner ────────────────────────────────────────────

    public async Task<List<long>> ScanChildOffsetsAsync(string filePath, bool isArray, CancellationToken ct)
    {
        var offsets = new List<long>();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer  = new byte[ScanChunkSize];
        var depth   = 0;
        var inStr   = false;
        var escaped = false;
        var pos     = 0L;
        var rootSkipped = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (escaped) { escaped = false; continue; }
                if (b == (byte)'\\' && inStr) { escaped = true; continue; }
                if (b == (byte)'"') { inStr = !inStr; continue; }
                if (inStr) continue;

                if (b == (byte)'{' || b == (byte)'[')
                {
                    if (!rootSkipped) { rootSkipped = true; depth++; continue; }
                    if (depth == 1) offsets.Add(pos + i);
                    depth++;
                }
                else if (b == (byte)'}' || b == (byte)']')
                {
                    depth--;
                }
            }
            pos += read;
        }

        return offsets;
    }
}
