using Nexaflow.Features.Common.ClientTools;
using System.Text;
using System.Text.Json.Nodes;

namespace Nexaflow.Core.Services.Agent;

/// <summary>
/// Extracts <c>client_tool</c> / <c>client_plan</c> / <c>client_prefill</c> fenced blocks from an
/// assistant reply and parses their JSON bodies. Anything outside a recognised fence — including
/// ordinary <c>```mermaid</c> / <c>```code</c> fences and plain prose — is returned verbatim as the
/// explanation. Never throws: malformed JSON is reported via
/// <see cref="ParsedAssistantTurn.ParseErrors"/>.
/// </summary>
public static class ClientBlockParser
{
    private const string ToolInfo    = "client_tool";
    private const string PlanInfo    = "client_plan";
    private const string PrefillInfo = "client_prefill";

    public static ParsedAssistantTurn Parse(string? raw)
    {
        var toolCalls   = new List<ToolCall>();
        var errors      = new List<string>();
        var explanation = new StringBuilder();
        ClientPlan? plan = null;
        string? prefill  = null;

        if (string.IsNullOrWhiteSpace(raw))
            return new ParsedAssistantTurn(string.Empty, toolCalls, plan, prefill, errors);

        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var i = 0;
        while (i < lines.Length)
        {
            if (TryParseFenceOpen(lines[i], out var fenceChar, out var fenceLen, out var info))
            {
                var bodyStart = i + 1;
                var j = bodyStart;
                while (j < lines.Length && !IsFenceClose(lines[j], fenceChar, fenceLen)) j++;
                var closed = j < lines.Length;   // lines[j] is the closing fence when true

                if (info is ToolInfo or PlanInfo or PrefillInfo)
                {
                    var body = string.Join('\n', lines[bodyStart..j]);
                    HandleRecognised(info, body, toolCalls, ref plan, ref prefill, errors);
                }
                else
                {
                    // Unrecognised fence (mermaid, code, …) — keep it verbatim in the explanation.
                    explanation.Append(lines[i]).Append('\n');
                    for (var k = bodyStart; k < j; k++) explanation.Append(lines[k]).Append('\n');
                    if (closed) explanation.Append(lines[j]).Append('\n');
                }

                i = closed ? j + 1 : lines.Length;
            }
            else
            {
                explanation.Append(lines[i]).Append('\n');
                i++;
            }
        }

        return new ParsedAssistantTurn(
            explanation.ToString().Trim(), toolCalls, plan, prefill, errors);
    }

    private static void HandleRecognised(
        string info, string body, List<ToolCall> toolCalls,
        ref ClientPlan? plan, ref string? prefill, List<string> errors)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(body); }
        catch (Exception ex) { errors.Add($"{info}: invalid JSON — {ex.Message}"); return; }

        if (node is not JsonObject obj)
        {
            errors.Add($"{info}: expected a JSON object");
            return;
        }

        switch (info)
        {
            case ToolInfo:
            {
                var name = Str(obj["tool"]);
                if (string.IsNullOrWhiteSpace(name))
                {
                    errors.Add("client_tool: missing 'tool' name");
                    return;
                }
                var argsNode = obj["arguments"] ?? obj["args"];
                var args = argsNode is JsonObject ao
                    ? (JsonObject)ao.DeepClone()
                    : new JsonObject();
                toolCalls.Add(new ToolCall(name!, args, body));
                break;
            }

            case PrefillInfo:
                prefill = Str(obj["text"]) ?? string.Empty;
                break;

            case PlanInfo:
                // Only the first plan in a reply is honoured.
                plan ??= ParsePlan(obj);
                break;
        }
    }

    private static ClientPlan ParsePlan(JsonObject obj)
    {
        var steps = new List<ClientPlanStep>();
        if (obj["steps"] is JsonArray arr)
        {
            foreach (var s in arr)
            {
                if (s is not JsonObject so) continue;
                var title = Str(so["title"]) ?? Str(so["tool"]) ?? "step";
                var tool  = Str(so["tool"]);
                var dec   = so["decision"] is JsonValue dv && dv.TryGetValue<bool>(out var b) && b;
                steps.Add(new ClientPlanStep(title, tool, dec));
            }
        }

        return new ClientPlan
        {
            Title   = Str(obj["title"]) ?? "Plan",
            Mermaid = Str(obj["mermaid"]),
            Steps   = steps
        };
    }

    /// <summary>Reads a node as a string, tolerating non-string JSON values.</summary>
    private static string? Str(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : node?.ToString();

    /// <summary>
    /// Detects a fence-opening line (≥3 backticks or tildes, optional leading spaces) and returns
    /// the fence character, length, and the normalised first info word (lower-cased).
    /// </summary>
    private static bool TryParseFenceOpen(string line, out char fenceChar, out int fenceLen, out string info)
    {
        fenceChar = '\0';
        fenceLen  = 0;
        info      = string.Empty;

        var span  = line.AsSpan();
        var start = 0;
        while (start < span.Length && span[start] == ' ') start++;
        if (start >= span.Length) return false;

        var c = span[start];
        if (c != '`' && c != '~') return false;

        var run = 0;
        var k   = start;
        while (k < span.Length && span[k] == c) { run++; k++; }
        if (run < 3) return false;

        fenceChar = c;
        fenceLen  = run;

        // Info string is the first whitespace-delimited token after the fence, lower-cased.
        var rest = span[k..].ToString().Trim();
        var sp   = rest.IndexOfAny([' ', '\t']);
        info = (sp >= 0 ? rest[..sp] : rest).ToLowerInvariant();
        return true;
    }

    /// <summary>A closing fence is a line of only the fence character (≥ the opening length).</summary>
    private static bool IsFenceClose(string line, char fenceChar, int fenceLen)
    {
        var s = line.AsSpan().Trim();
        if (s.Length < fenceLen) return false;
        foreach (var ch in s)
            if (ch != fenceChar) return false;
        return true;
    }
}
