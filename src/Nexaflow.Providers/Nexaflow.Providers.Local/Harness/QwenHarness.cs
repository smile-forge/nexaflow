using System.Text;
using System.Text.Json;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Providers.Local.Harness;

/// <summary>
/// Qwen 3.5 harness — ChatML turns (<c>&lt;|im_start|&gt;role … &lt;|im_end|&gt;</c>), Hermes-style tool
/// calling (<c>&lt;tool_call&gt;{json}&lt;/tool_call&gt;</c> / <c>&lt;tool_response&gt;</c>), and
/// <c>&lt;think&gt;…&lt;/think&gt;</c> thinking. Built fresh (the reference engine has no Qwen formatter).
/// </summary>
public sealed class QwenHarness : IModelHarness
{
    private const string ImStart = "<|im_start|>";
    private const string ImEnd   = "<|im_end|>";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public IReadOnlyList<string> AntiPrompts => [ImEnd, ImStart];

    // ─────────────────────────────────────────────────────────────────────────
    // Prompt building
    // ─────────────────────────────────────────────────────────────────────────

    public string Format(IReadOnlyList<LlmMessage> messages, HarnessOptions options, IReadOnlyList<IServerTool> tools)
    {
        var sb = new StringBuilder();

        // 1. Authoritative server system turn FIRST.
        sb.Append(ImStart).Append("system\n");
        sb.Append(ServerSystemPrompt.Build(tools));
        if (tools.Count > 0) sb.Append(ToolBlock(tools));
        sb.Append(ImEnd).Append('\n');

        // 2. Nexaflow's content as caller input; its System message demoted to a user turn.
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case LlmRole.System:
                    AppendTurn(sb, "user", "## Instructions from Nexaflow (the client application)\n" + m.Text);
                    break;
                case LlmRole.Assistant:
                    AppendTurn(sb, "assistant", m.Text);
                    break;
                default:
                    AppendTurn(sb, "user", m.Text);
                    break;
            }
        }

        // 3. Open the assistant turn.
        sb.Append(ImStart).Append("assistant\n");
        return sb.ToString();
    }

    private static void AppendTurn(StringBuilder sb, string role, string content)
        => sb.Append(ImStart).Append(role).Append('\n').Append(content).Append(ImEnd).Append('\n');

    private static string ToolBlock(IReadOnlyList<IServerTool> tools)
    {
        var sb = new StringBuilder();
        sb.Append("\n\n# Tools\n\n")
          .Append("You may call your server-side tools to help answer. Function signatures are inside <tools></tools>:\n<tools>\n");
        foreach (var t in tools)
            sb.Append(JsonSerializer.Serialize(ToSchema(t), Json)).Append('\n');
        sb.Append("</tools>\n\n")
          .Append("To call a function, return a JSON object with its name and arguments inside <tool_call></tool_call> and then stop:\n")
          .Append("<tool_call>\n{\"name\": \"calculator\", \"arguments\": {\"expression\": \"2+2\"}}\n</tool_call>\n")
          .Append("The result returns to you inside <tool_response></tool_response>, after which you continue. Call a tool only when it helps; otherwise just answer.\n");
        return sb.ToString();
    }

    private static object ToSchema(IServerTool t) => new
    {
        type     = "function",
        function = new
        {
            name        = t.Name,
            description = t.Description,
            parameters  = new
            {
                type       = "object",
                properties = t.Parameters.ToDictionary(
                                 p => p.Name,
                                 p => (object)new { type = p.Type.ToLowerInvariant(), description = p.Description }),
                required   = t.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray()
            }
        }
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Tool-round rendering (close assistant, add tool response, reopen assistant)
    // ─────────────────────────────────────────────────────────────────────────

    public string RenderToolRound(ServerToolCall call, string? thought, string resultText)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(thought))
            sb.Append("<think>\n").Append(thought).Append("\n</think>\n");
        sb.Append("<tool_call>\n").Append(SerialiseCall(call)).Append("\n</tool_call>").Append(ImEnd).Append('\n');
        sb.Append(ImStart).Append("user\n<tool_response>\n").Append(resultText).Append("\n</tool_response>").Append(ImEnd).Append('\n');
        sb.Append(ImStart).Append("assistant\n");
        return sb.ToString();
    }

    private static string SerialiseCall(ServerToolCall call)
        => JsonSerializer.Serialize(new { name = call.Name, arguments = call.Arguments }, Json);

    // ─────────────────────────────────────────────────────────────────────────
    // Response parsing
    // ─────────────────────────────────────────────────────────────────────────

    public HarnessResult Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new HarnessResult();

        string working = TruncateAt(raw, ImEnd, ImStart);
        (working, string? thought)    = ExtractThink(working);
        (working, ServerToolCall? tc) = ExtractToolCall(working);

        return new HarnessResult { VisibleText = working.Trim(), Thought = thought, ToolCall = tc };
    }

    private static string TruncateAt(string text, params string[] markers)
    {
        int earliest = text.Length;
        foreach (var m in markers)
        {
            int idx = text.IndexOf(m, StringComparison.Ordinal);
            if (idx >= 0 && idx < earliest) earliest = idx;
        }
        return earliest < text.Length ? text[..earliest] : text;
    }

    private static (string remaining, string? thought) ExtractThink(string text)
    {
        const string open = "<think>", close = "</think>";
        int s = text.IndexOf(open, StringComparison.Ordinal);
        if (s < 0) return (text, null);
        int e = text.IndexOf(close, s, StringComparison.Ordinal);
        if (e < 0) return (string.Empty, text[(s + open.Length)..].Trim());   // unclosed → all thought
        string thought   = text[(s + open.Length)..e].Trim();
        string remaining = text[..s] + text[(e + close.Length)..];
        return (remaining, thought);
    }

    private static (string remaining, ServerToolCall? toolCall) ExtractToolCall(string text)
    {
        const string open = "<tool_call>", close = "</tool_call>";
        int s = text.IndexOf(open, StringComparison.Ordinal);
        if (s < 0) return (text, null);
        int e = text.IndexOf(close, s, StringComparison.Ordinal);
        if (e < 0) return (text, null);

        string body      = text[(s + open.Length)..e].Trim();
        string remaining = text[..s] + text[(e + close.Length)..];
        return (remaining, ParseCallJson(body));
    }

    private static ServerToolCall? ParseCallJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("name", out var nameEl)) return null;

            var args = new Dictionary<string, object?>(StringComparer.Ordinal);
            if (root.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                foreach (var p in argsEl.EnumerateObject())
                    args[p.Name] = ToClr(p.Value);

            return new ServerToolCall { Name = nameEl.GetString() ?? "", Arguments = args };
        }
        catch { return null; }
    }

    private static object? ToClr(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True   => true,
        JsonValueKind.False  => false,
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.Null   => null,
        _                    => e.GetRawText(),
    };
}
