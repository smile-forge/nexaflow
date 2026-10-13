using Nexaflow.Features.Common.ClientTools;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

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

    /// <param name="toolNames">The tools the model may call. A <c>client_tool</c> body written without its fence — as
    /// some models do in their own call framing, <c>call:client_tool</c> then the JSON — is the call it plainly is, when it
    /// names one of them. A reply with no block that calls one of them as text — <c>add_note(…)</c>, or a model's own
    /// argument syntax such as <c>call:add_note{…}</c> — is reported as a parse error, so the model is asked to call it
    /// properly rather than the call reaching the user as the answer.</param>
    public static ParsedAssistantTurn Parse(string? raw, IReadOnlyCollection<string>? toolNames = null)
    {
        var toolCalls   = new List<ToolCall>();
        var errors      = new List<string>();
        var explanation = new StringBuilder();
        var textCalls   = new List<(string Line, string Tool)>();
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

                if (IsActionBlock(info))
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
            else if (toolNames is not null && BareCall(lines, i, toolNames) is var (body, next))
            {
                // A client_tool body written without its fence — after a model's own call marker, or alone.
                HandleRecognised(ToolInfo, body, toolCalls, ref plan, ref prefill, errors);
                i = next;
            }
            else
            {
                if (toolNames is not null && TextCall(lines[i], toolNames) is { } tool) textCalls.Add((lines[i].Trim(), tool));
                // A call opened outside a fence whose body cannot be read is still a call, not prose: the model is asked
                // to write it again rather than the attempt reaching the user as the answer.
                if (toolNames is not null && OpensBareCall(lines[i]))
                    errors.Add($"`{lines[i].Trim()}` starts a client_tool call that could not be read, so it did not run. A tool "
                             + "runs only from a fenced client_tool block of JSON:\n```client_tool\n{\"tool\": \"…\", \"arguments\": { … }}\n```");
                explanation.Append(lines[i]).Append('\n');
                i++;
            }
        }

        // A call written as text runs nothing; with no block beside it, it is the model's attempt at one.
        if (toolCalls.Count == 0)
            foreach (var (line, tool) in textCalls)
                errors.Add($"`{line}` is a call to {tool} written as text, so it did not run. A tool runs only from a fenced "
                         + $"client_tool block:\n```client_tool\n{{\"tool\": \"{tool}\", \"arguments\": {{ … }}}}\n```");

        return new ParsedAssistantTurn(
            explanation.ToString().Trim(), toolCalls, plan, prefill, errors);
    }

    /// <summary>The tool a line calls as text — its name straight followed by arguments, <c>name(…)</c> or
    /// <c>name{…}</c>, alone or after a model's own <c>call:</c> marker; null for a line that only mentions one.</summary>
    internal static string? TextCall(string line, IReadOnlyCollection<string> toolNames)
    {
        var text = line.Trim().Trim('`');
        if (text.StartsWith("call:", StringComparison.OrdinalIgnoreCase)) text = text[5..].TrimStart();

        int end = 0;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '_' or '-' or '.')) end++;
        if (end == 0 || end >= text.Length || text[end] is not ('(' or '{')) return null;

        var name = text[..end];
        return toolNames.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Longest a bare call's JSON may run, in lines, before it is taken for prose.</summary>
    private const int BareCallLines = 200;

    /// <summary>
    /// A <c>client_tool</c> body without its fence starting at <paramref name="at"/> — <c>{"tool": …, "arguments": …}</c>
    /// naming one of <paramref name="toolNames"/>, alone or after a <c>call:client_tool</c> / <c>client_tool</c> marker,
    /// on one line or several — and the line after it; null when there is none.
    /// </summary>
    internal static (string Body, int Next)? BareCall(string[] lines, int at, IReadOnlyCollection<string> toolNames)
    {
        var first = lines[at].Trim();
        if (first.StartsWith("call:", StringComparison.OrdinalIgnoreCase)) first = first[5..].TrimStart();

        int start = at;
        string opening;
        if (first.Equals(ToolInfo, StringComparison.OrdinalIgnoreCase))
        {
            start = at + 1;
            while (start < lines.Length && lines[start].Trim().Length == 0) start++;
            if (start >= lines.Length) return null;
            opening = lines[start].Trim();
        }
        else if (first.StartsWith(ToolInfo, StringComparison.OrdinalIgnoreCase))
            opening = first[ToolInfo.Length..].TrimStart();
        else
            opening = first;

        if (!opening.StartsWith('{')) return null;

        var body = new StringBuilder(opening);
        for (int end = start; ; )
        {
            var candidate = body.ToString();
            if (TryCallObject(candidate, toolNames)) return (candidate, end + 1);
            // A model's own call syntax leaves words unquoted — "tool": delete_file — where JSON wants strings.
            if (Requoted(candidate) is var repaired && TryCallObject(repaired, toolNames)) return (repaired, end + 1);
            if (++end >= lines.Length || end - start > BareCallLines) return null;
            body.Append('\n').Append(lines[end]);
        }
    }

    /// <summary>A bare word where JSON wants a string — a key, or a value that is not <c>true</c>, <c>false</c> or <c>null</c>.</summary>
    private static readonly Regex BareKey   = new(@"(?<=[{,]\s*)([A-Za-z_][\w\-]*)(?=\s*:)", RegexOptions.Compiled);
    private static readonly Regex BareValue = new(@"(?<=[:\[,]\s*)(?!(?:true|false|null)\b)([A-Za-z_][\w.\-/\\]*)(?=\s*[,}\]])", RegexOptions.Compiled);

    /// <summary>The call body with its bare words quoted, as a model writing its own call syntax means them.</summary>
    private static string Requoted(string body) => BareValue.Replace(BareKey.Replace(body, "\"$1\""), "\"$1\"");

    /// <summary>A line that opens a <c>client_tool</c> call outside a fence — <c>call:client_tool</c> or <c>client_tool {</c>.</summary>
    internal static bool OpensBareCall(string line)
    {
        var text = line.Trim().Trim('`');
        if (text.StartsWith("call:", StringComparison.OrdinalIgnoreCase)) text = text[5..].TrimStart();
        return text.StartsWith(ToolInfo, StringComparison.OrdinalIgnoreCase)
            && (text.Length == ToolInfo.Length || text[ToolInfo.Length] is '{' or ' ' or '\t');
    }

    /// <summary>Whether <paramref name="json"/> is a whole client_tool object naming a known tool.</summary>
    private static bool TryCallObject(string json, IReadOnlyCollection<string> toolNames)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject obj
                && Str(obj["tool"]) is { } tool
                && toolNames.Contains(tool, StringComparer.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    /// <summary>Whether a fence with this info word holds something the agent acts on rather than prose to show.</summary>
    internal static bool IsActionBlock(string info) => info is ToolInfo or PlanInfo or PrefillInfo;

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

    internal static ClientPlan ParsePlan(JsonObject obj)
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
    internal static bool TryParseFenceOpen(string line, out char fenceChar, out int fenceLen, out string info)
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
    internal static bool IsFenceClose(string line, char fenceChar, int fenceLen)
    {
        var s = line.AsSpan().Trim();
        if (s.Length < fenceLen) return false;
        foreach (var ch in s)
            if (ch != fenceChar) return false;
        return true;
    }
}
