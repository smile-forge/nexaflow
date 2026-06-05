using System.Globalization;
using System.Text;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Local.ServerTools;

namespace Nexaflow.Providers.Local.Harness;

/// <summary>
/// Gemma 4 harness — mirrors the proven control-token format from the reference engine
/// (<c>&lt;|turn&gt;</c> turns, <c>&lt;|tool&gt;</c> declarations, <c>&lt;|tool_call&gt;</c> /
/// <c>&lt;|tool_response&gt;</c>, <c>&lt;|channel&gt;</c> thinking, <c>&lt;|"|&gt;</c> string delimiter).
/// </summary>
public sealed class GemmaHarness : IModelHarness
{
    // ── Control tokens ───────────────────────────────────────────────────────
    private const string TurnStart         = "<|turn>";
    private const string TurnEnd           = "<turn|>";
    private const string ThinkToken        = "<|think|>";
    private const string ToolDeclStart     = "<|tool>";
    private const string ToolDeclEnd       = "<tool|>";
    private const string ToolCallStart     = "<|tool_call>";
    private const string ToolCallEnd       = "<tool_call|>";
    private const string ToolResponseStart = "<|tool_response>";
    private const string ToolResponseEnd   = "<tool_response|>";
    private const string ChannelStart      = "<|channel>";
    private const string ChannelEnd        = "<channel|>";
    private const string StringDelim       = "<|\"|>";

    private static readonly string[] StopMarkers =
        [TurnEnd, TurnStart, "User:", "Assistant:", "model:", "user\n"];

    public IReadOnlyList<string> AntiPrompts => [TurnEnd, "User:"];

    // ─────────────────────────────────────────────────────────────────────────
    // Prompt building
    // ─────────────────────────────────────────────────────────────────────────

    public string Format(IReadOnlyList<LlmMessage> messages, HarnessOptions options, IReadOnlyList<IServerTool> tools)
    {
        var sb = new StringBuilder();

        // 1. Authoritative server system turn FIRST.
        sb.Append(TurnStart).Append("system\n");
        if (options.ThinkingEnabled) sb.Append(ThinkToken);
        sb.Append(ServerSystemPrompt.Build(tools));
        sb.Append(NativeSyntaxHelp());
        foreach (var t in tools)
            sb.Append(ToolDeclStart).Append(BuildDeclaration(t)).Append(ToolDeclEnd);
        sb.Append(TurnEnd).Append('\n');

        // 2. Everything Nexaflow sent is the caller's input. Its System message is demoted to a user
        //    turn (the persona / client-tool catalogue survives verbatim for the outer client loop).
        foreach (var m in messages)
        {
            switch (m.Role)
            {
                case LlmRole.System:
                    AppendUserTurn(sb, "## Instructions from Nexaflow (the client application)\n" + m.Text);
                    break;
                case LlmRole.Assistant:
                    AppendModelTurn(sb, m.Text);
                    break;
                default:
                    AppendUserTurn(sb, m.Text);
                    break;
            }
        }

        // 3. Open the model turn so generation starts immediately.
        sb.Append(TurnStart).Append("model\n");
        if (options.ThinkingEnabled)
            sb.Append(ChannelStart).Append("thought\n").Append(ChannelEnd);

        return sb.ToString();
    }

    private static void AppendUserTurn(StringBuilder sb, string content)
        => sb.Append(TurnStart).Append("user\n").Append(content).Append(TurnEnd).Append('\n');

    private static void AppendModelTurn(StringBuilder sb, string content)
        => sb.Append(TurnStart).Append("model\n").Append(content).Append(TurnEnd).Append('\n');

    private static string NativeSyntaxHelp() =>
        "\n\n# Calling your server-side tools\n" +
        "To call one of the tools above, emit a tool call in EXACTLY this form and then stop:\n" +
        ToolCallStart + "call:tool_name{arg:" + StringDelim + "text value" + StringDelim + ",number:42,flag:true}" + ToolCallEnd + "\n" +
        "Wrap string values in " + StringDelim + " … " + StringDelim + "; numbers and true/false are bare. The result returns to you as " +
        ToolResponseStart + "response:tool_name{...}" + ToolResponseEnd + ", after which you continue. Call a tool only when it helps; otherwise just answer.\n";

    private static string BuildDeclaration(IServerTool t)
    {
        var sb = new StringBuilder();
        sb.Append("declaration:").Append(t.Name).Append('{');
        sb.Append("description:").Append(StringDelim).Append(t.Description).Append(StringDelim);
        if (t.Parameters.Count > 0)
        {
            sb.Append(",parameters:{");
            bool first = true;
            foreach (var p in t.Parameters)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(p.Name).Append(":{")
                  .Append("type:").Append(StringDelim).Append(p.Type).Append(StringDelim)
                  .Append(",description:").Append(StringDelim).Append(p.Description).Append(StringDelim)
                  .Append(",required:").Append(p.Required ? "true" : "false")
                  .Append('}');
            }
            sb.Append('}');
        }
        sb.Append('}');
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tool-round rendering (continue the open model turn)
    // ─────────────────────────────────────────────────────────────────────────

    public string RenderToolRound(ServerToolCall call, string? thought, string resultText)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(thought))
            sb.Append(ChannelStart).Append("thought\n").Append(thought).Append('\n').Append(ChannelEnd);
        sb.Append(SerialiseToolCall(call));
        sb.Append(SerialiseToolResponse(call.Name, resultText));
        return sb.ToString();
    }

    private static string SerialiseToolCall(ServerToolCall tc)
    {
        var sb = new StringBuilder();
        sb.Append(ToolCallStart).Append("call:").Append(tc.Name).Append('{');
        bool first = true;
        foreach (var (k, v) in tc.Arguments)
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(k).Append(':');
            AppendGemmaValue(sb, v);
        }
        sb.Append('}').Append(ToolCallEnd);
        return sb.ToString();
    }

    private static string SerialiseToolResponse(string name, string result)
        => ToolResponseStart + "response:" + name + "{result:" + StringDelim + result + StringDelim + "}" + ToolResponseEnd;

    private static void AppendGemmaValue(StringBuilder sb, object? v)
    {
        switch (v)
        {
            case null:      sb.Append(StringDelim).Append(StringDelim); break;
            case string s:  sb.Append(StringDelim).Append(s).Append(StringDelim); break;
            case bool b:    sb.Append(b ? "true" : "false"); break;
            default:        sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Response parsing
    // ─────────────────────────────────────────────────────────────────────────

    public HarnessResult Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new HarnessResult();

        string working = TruncateAtStopMarkers(raw);
        (working, string? thought)  = ExtractChannel(working);
        (working, ServerToolCall? tc) = ExtractToolCall(working);

        return new HarnessResult { VisibleText = working.Trim(), Thought = thought, ToolCall = tc };
    }

    private static string TruncateAtStopMarkers(string text)
    {
        int earliest = text.Length;
        foreach (var marker in StopMarkers)
        {
            int idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < earliest) earliest = idx;
        }
        return earliest < text.Length ? text[..earliest] : text;
    }

    private static (string remaining, string? thought) ExtractChannel(string text)
    {
        int start = text.IndexOf(ChannelStart, StringComparison.Ordinal);
        if (start < 0) return (text, null);

        int contentStart = text.IndexOf('\n', start + ChannelStart.Length);
        if (contentStart < 0) return (text, null);
        contentStart++;

        int end = text.IndexOf(ChannelEnd, contentStart, StringComparison.Ordinal);
        if (end < 0)
            return (string.Empty, text[contentStart..].Trim());

        string thought   = text[contentStart..end].Trim();
        string remaining = text[..start] + text[(end + ChannelEnd.Length)..];
        return (remaining, thought);
    }

    private static (string remaining, ServerToolCall? toolCall) ExtractToolCall(string text)
    {
        int start = text.IndexOf(ToolCallStart, StringComparison.Ordinal);
        if (start < 0) return (text, null);

        int end = text.IndexOf(ToolCallEnd, start, StringComparison.Ordinal);
        if (end < 0) return (text, null);

        string body      = text[(start + ToolCallStart.Length)..end];   // "call:fn{…}"
        string remaining = text[..start] + text[(end + ToolCallEnd.Length)..];
        return (remaining, ParseToolCallBody(body));
    }

    private static ServerToolCall ParseToolCallBody(string body)
    {
        const string callPrefix = "call:";
        if (body.StartsWith(callPrefix, StringComparison.Ordinal)) body = body[callPrefix.Length..];

        int braceOpen = body.IndexOf('{');
        if (braceOpen < 0) return new ServerToolCall { Name = body.Trim() };

        string name   = body[..braceOpen].Trim();
        string argStr = body[(braceOpen + 1)..].TrimEnd('}');
        return new ServerToolCall { Name = name, Arguments = ParseArguments(argStr) };
    }

    private static Dictionary<string, object?> ParseArguments(string argStr)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(argStr)) return result;

        foreach (var pair in SplitArgs(argStr))
        {
            int colon = pair.IndexOf(':');
            if (colon < 0) continue;
            string key   = pair[..colon].Trim();
            string value = pair[(colon + 1)..].Trim();

            if (value.StartsWith(StringDelim, StringComparison.Ordinal) &&
                value.EndsWith(StringDelim, StringComparison.Ordinal) &&
                value.Length >= StringDelim.Length * 2)
                result[key] = value[StringDelim.Length..^StringDelim.Length];
            else if (bool.TryParse(value, out bool b))
                result[key] = b;
            else if (long.TryParse(value, out long l))
                result[key] = l;
            else if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                result[key] = d;
            else
                result[key] = value;
        }
        return result;
    }

    /// <summary>Splits on commas that are NOT inside a <c>&lt;|"|&gt; … &lt;|"|&gt;</c> pair.</summary>
    private static List<string> SplitArgs(string argStr)
    {
        var parts   = new List<string>();
        var current = new StringBuilder();
        bool inStr  = false;
        int i = 0;
        while (i < argStr.Length)
        {
            if (argStr.AsSpan(i).StartsWith(StringDelim.AsSpan(), StringComparison.Ordinal))
            {
                inStr = !inStr;
                current.Append(StringDelim);
                i += StringDelim.Length;
                continue;
            }
            if (!inStr && argStr[i] == ',')
            {
                parts.Add(current.ToString());
                current.Clear();
                i++;
                continue;
            }
            current.Append(argStr[i]);
            i++;
        }
        if (current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
