using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Generation;

/// <summary>What a generated token is to the tool-call reader.</summary>
internal enum ToolCallToken
{
    /// <summary>Text of the reply.</summary>
    Text,
    /// <summary>Part of a call being written.</summary>
    Call,
    /// <summary>The end of a call: it has been read.</summary>
    Made,
    /// <summary>The model waits for what came of its calls: its turn is over.</summary>
    Waits,
}

/// <summary>
/// How a model writes its tool calls in its reply — the syntax its own chat template teaches it, told apart by the
/// special tokens its vocabulary holds. Read token by token: a call's tokens never reach the reply's text, and each
/// call is sent as soon as its closing token is out.
/// </summary>
internal abstract class ToolCallSyntax
{
    /// <summary>The syntax <paramref name="vocabulary"/>'s model writes, or null for one with no tool calling of its own.</summary>
    /// <param name="special">The one special token a text is in the vocabulary, or null.</param>
    /// <param name="template">The model's chat template, which tells syntaxes that share their tokens apart.</param>
    public static ToolCallSyntax? Of(Func<string, int?> special, string template)
    {
        if (special("<|tool_call>") is { } open && special("<tool_call|>") is { } close
            && special("<|\"|>") is { } quote && special("<|tool_response>") is { } wait)
            return new GemmaToolCalls(open, close, quote, wait);
        if (special("<tool_call>") is { } xmlOpen && special("</tool_call>") is { } xmlClose && template.Contains("<function=", StringComparison.Ordinal))
            return new QwenToolCalls(xmlOpen, xmlClose);
        return null;
    }

    /// <summary>A reader for one reply.</summary>
    /// <param name="text">A token's text in the reply.</param>
    /// <param name="tools">The tools offered, whose parameter types decide how an argument is read.</param>
    public abstract ToolCallReader Read(Func<int, string> text, IReadOnlyList<ChatTool> tools);
}

/// <summary>Reads one reply's tokens: its calls, as they are made.</summary>
internal abstract class ToolCallReader
{
    private int _made;

    /// <summary>The call last made.</summary>
    public ChatToolCall? Made { get; protected set; }

    public abstract ToolCallToken Take(int token);

    protected string NextId() => $"call_{_made++}";
}

/// <summary>
/// Gemma 4's syntax: <c>&lt;|tool_call&gt;</c>, then <c>call:name{key:value,…}</c> with every string between
/// <c>&lt;|"|&gt;</c> delimiters, then <c>&lt;tool_call|&gt;</c>; after its calls it waits at
/// <c>&lt;|tool_response&gt;</c>. The delimiters become JSON's quotes, and the bare keys are quoted.
/// </summary>
internal sealed class GemmaToolCalls(int open, int close, int quote, int wait) : ToolCallSyntax
{
    private readonly int _open = open, _close = close, _quote = quote, _wait = wait;
    public override ToolCallReader Read(Func<int, string> text, IReadOnlyList<ChatTool> tools) => new Reader(this, text);

    private sealed class Reader(GemmaToolCalls syntax, Func<int, string> text) : ToolCallReader
    {
        private StringBuilder? _call;
        private bool _inString;

        public override ToolCallToken Take(int token)
        {
            if (_call is null)
            {
                if (token == syntax._wait) return ToolCallToken.Waits;
                if (token != syntax._open) return ToolCallToken.Text;
                _call = new StringBuilder();
                _inString = false;
                return ToolCallToken.Call;
            }
            if (token == syntax._close)
            {
                Made  = Parse(_call.ToString(), NextId());
                _call = null;
                return ToolCallToken.Made;
            }
            if (token == syntax._quote)
            {
                _call.Append('"');
                _inString = !_inString;
                return ToolCallToken.Call;
            }
            string piece = text(token);
            _call.Append(_inString ? JsonEncodedText.Encode(piece).Value : piece);
            return ToolCallToken.Call;
        }
    }

    /// <summary>A call as written — <c>call:name{key:value,…}</c>, its strings already quoted — as a tool, and its
    /// arguments as a JSON object: the keys, written bare, quoted. Arguments that still will not read are passed on as the
    /// model wrote them, so the app can have the call written again rather than run it with none.</summary>
    internal static ChatToolCall Parse(string written, string id)
    {
        var text = written.Trim();
        if (text.StartsWith("call:", StringComparison.Ordinal)) text = text[5..];
        int open = text.IndexOf('{');
        string name = (open < 0 ? text : text[..open]).Trim();
        string body = open < 0 ? "{}" : text[open..];
        string arguments;
        try { arguments = JsonNode.Parse(Quoted(body)) is JsonObject args ? args.ToJsonString() : body; }
        catch (JsonException) { arguments = body; }
        return new ChatToolCall(id, name, arguments);
    }

    /// <summary>The body with every bare key quoted — and any bare word value that is not <c>true</c>, <c>false</c> or
    /// <c>null</c>, as a model's own syntax may leave one.</summary>
    internal static string Quoted(string body)
    {
        var json = new StringBuilder(body.Length + 16);
        for (int i = 0; i < body.Length; )
        {
            char c = body[i];
            if (c == '"')
            {
                int end = i + 1;
                while (end < body.Length && body[end] != '"') end += body[end] == '\\' ? 2 : 1;
                end = Math.Min(end, body.Length - 1);
                json.Append(body, i, end - i + 1);
                i = end + 1;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int end = i;
                while (end < body.Length && (char.IsLetterOrDigit(body[end]) || body[end] is '_' or '-' or '.')) end++;
                string word = body[i..end];
                json.Append(word is "true" or "false" or "null" ? word : $"\"{word}\"");
                i = end;
                continue;
            }
            json.Append(c);
            i++;
        }
        return json.ToString();
    }
}

/// <summary>
/// Qwen's syntax (3.5 and later): between <c>&lt;tool_call&gt;</c> and <c>&lt;/tool_call&gt;</c>,
/// <c>&lt;function=name&gt;</c> holding one <c>&lt;parameter=key&gt;value&lt;/parameter&gt;</c> per argument — a
/// string written as it is, anything else as JSON, as the template writes past calls. Which is which comes from the
/// tool's parameter types; a parameter the tool does not declare is JSON where it reads as JSON.
/// </summary>
internal sealed partial class QwenToolCalls(int open, int close) : ToolCallSyntax
{
    private readonly int _open = open, _close = close;
    [GeneratedRegex(@"<function=([^>\n]+)>", RegexOptions.CultureInvariant)]
    private static partial Regex Function();

    [GeneratedRegex(@"<parameter=([^>\n]+)>\n?(.*?)\n?</parameter>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Parameter();

    public override ToolCallReader Read(Func<int, string> text, IReadOnlyList<ChatTool> tools) => new Reader(this, text, tools);

    private sealed class Reader(QwenToolCalls syntax, Func<int, string> text, IReadOnlyList<ChatTool> tools) : ToolCallReader
    {
        private StringBuilder? _call;

        public override ToolCallToken Take(int token)
        {
            if (_call is null)
            {
                if (token != syntax._open) return ToolCallToken.Text;
                _call = new StringBuilder();
                return ToolCallToken.Call;
            }
            if (token == syntax._close)
            {
                Made  = Parse(_call.ToString(), NextId(), tools);
                _call = null;
                return ToolCallToken.Made;
            }
            _call.Append(text(token));
            return ToolCallToken.Call;
        }
    }

    /// <summary>A call's body as a tool and its arguments. A body with no function in it is passed on as written,
    /// under no name, so the app can have the call written again.</summary>
    internal static ChatToolCall Parse(string body, string id, IReadOnlyList<ChatTool> tools)
    {
        if (Function().Match(body) is not { Success: true } function) return new ChatToolCall(id, "", body.Trim());
        string name = function.Groups[1].Value.Trim();
        var types = ParameterTypes(tools.FirstOrDefault(t => t.Name == name));
        var arguments = new JsonObject();
        foreach (Match parameter in Parameter().Matches(body, function.Index + function.Length))
        {
            string key = parameter.Groups[1].Value.Trim(), value = parameter.Groups[2].Value;
            arguments[key] = Value(value, types.GetValueOrDefault(key));
        }
        return new ChatToolCall(id, name, arguments.ToJsonString());
    }

    private static JsonNode? Value(string written, string? type)
    {
        if (type == "string") return JsonValue.Create(written);
        // Qwen 3.5's template writes a past call's booleans as Python does.
        if (type == "boolean" && written.Trim() is "True" or "False") return JsonValue.Create(written.Trim() == "True");
        try { return JsonNode.Parse(written) ?? JsonValue.Create(written); }
        catch (JsonException) { return JsonValue.Create(written); }
    }

    private static Dictionary<string, string> ParameterTypes(ChatTool? tool)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tool is null) return types;
        try
        {
            if (JsonNode.Parse(tool.Parameters)?["properties"] is JsonObject properties)
                foreach (var (key, schema) in properties)
                    if (schema?["type"] is JsonValue type && type.TryGetValue<string>(out var name)) types[key] = name;
        }
        catch (JsonException) { }
        return types;
    }
}
