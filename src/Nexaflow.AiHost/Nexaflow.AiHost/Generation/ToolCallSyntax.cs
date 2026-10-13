using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntimeGenAI;
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
/// How a model writes its tool calls, read token by token — Gemma 4's own syntax: <c>&lt;|tool_call&gt;</c>, then
/// <c>call:name{key:value,…}</c> with every string between <c>&lt;|"|&gt;</c> delimiters, then <c>&lt;tool_call|&gt;</c>;
/// and after its calls it waits at <c>&lt;|tool_response&gt;</c>. Those are special tokens a tokenizer stream never
/// decodes, so the call is read from the tokens themselves, the delimiters becoming JSON's quotes.
/// </summary>
internal sealed class ToolCallSyntax
{
    private readonly int _open, _close, _quote, _wait;

    private ToolCallSyntax(int open, int close, int quote, int wait)
    {
        _open  = open;
        _close = close;
        _quote = quote;
        _wait  = wait;
    }

    /// <summary>The syntax the model's vocabulary holds, or null for a model with no tool calling of its own.</summary>
    public static ToolCallSyntax? Of(Tokenizer tokenizer)
        => Special(tokenizer, "<|tool_call>") is { } open && Special(tokenizer, "<tool_call|>") is { } close
        && Special(tokenizer, "<|\"|>") is { } quote && Special(tokenizer, "<|tool_response>") is { } wait
            ? new ToolCallSyntax(open, close, quote, wait)
            : null;

    /// <summary>The one token <paramref name="text"/> is, past whatever the tokenizer puts before any text.</summary>
    private static int? Special(Tokenizer tokenizer, string text)
    {
        using var lead = tokenizer.Encode("");
        using var encoded = tokenizer.Encode(text);
        var tokens = encoded[0].ToArray();
        int skip = lead[0].Length;
        return tokens.Length == skip + 1 ? tokens[skip] : null;
    }

    public Reader Read(TokenizerStream stream) => new(this, stream);

    /// <summary>Reads one reply's tokens: its calls, as they are made.</summary>
    internal sealed class Reader(ToolCallSyntax syntax, TokenizerStream stream)
    {
        private StringBuilder? _call;
        private bool _inString;
        private int _made;

        /// <summary>The call last made.</summary>
        public ChatToolCall? Made { get; private set; }

        public ToolCallToken Take(int token)
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
                Made  = Parse(_call.ToString(), $"call_{_made++}");
                _call = null;
                return ToolCallToken.Made;
            }
            if (token == syntax._quote)
            {
                _call.Append('"');
                _inString = !_inString;
                return ToolCallToken.Call;
            }
            string piece = stream.Decode(token);
            _call.Append(_inString ? JsonEncodedText.Encode(piece).Value : piece);
            return ToolCallToken.Call;
        }
    }

    /// <summary>A call as written — <c>call:name{key:value,…}</c>, its strings already quoted — as a tool, and its
    /// arguments as a JSON object: the keys, written bare, quoted. Arguments that still will not read are none, so the
    /// tool says what it is missing.</summary>
    internal static ChatToolCall Parse(string written, string id)
    {
        var text = written.Trim();
        if (text.StartsWith("call:", StringComparison.Ordinal)) text = text[5..];
        int open = text.IndexOf('{');
        string name = (open < 0 ? text : text[..open]).Trim();
        string body = open < 0 ? "{}" : text[open..];
        string arguments;
        try { arguments = JsonNode.Parse(Quoted(body)) is JsonObject args ? args.ToJsonString() : "{}"; }
        catch (JsonException) { arguments = "{}"; }
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

    /// <summary>
    /// The tools as the model's chat template declares them — <c>&lt;|tool&gt;declaration:name{description:…,parameters:{…}}&lt;tool|&gt;</c>
    /// each — written here because the template's own rendering of them needs a filter (<c>upper</c>) onnxruntime-genai's
    /// template engine lacks. They end the system turn, where the template puts them.
    /// </summary>
    public static string Declarations(IReadOnlyList<ChatTool> tools)
    {
        var text = new StringBuilder();
        foreach (var tool in tools)
        {
            text.Append("<|tool>declaration:").Append(tool.Name).Append("{description:").Append(Quote(tool.Description));
            if (JsonNode.Parse(tool.Parameters) is JsonObject parameters)
            {
                text.Append(",parameters:{");
                if (parameters["properties"] is JsonObject properties && properties.Count > 0)
                    text.Append("properties:{").Append(Properties(properties)).Append("},");
                if (parameters["required"] is JsonArray { Count: > 0 } required)
                    text.Append("required:[").Append(string.Join(',', required.Select(r => Quote((string?)r ?? "")))).Append("],");
                text.Append("type:").Append(Quote(Type(parameters))).Append('}');
            }
            text.Append("}<tool|>");
        }
        return text.ToString();
    }

    /// <summary>Each property in key order: <c>name:{description:…,type:…}</c>, and an object's own properties, an array's
    /// items and a string's choices as the template writes them.</summary>
    private static string Properties(JsonObject properties)
        => string.Join(',', properties.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
        {
            var value = p.Value as JsonObject ?? [];
            var parts = new List<string>();
            if ((string?)value["description"] is { } description) parts.Add("description:" + Quote(description));
            string type = Type(value);
            if (type == "STRING" && value["enum"] is JsonArray choices) parts.Add("enum:" + Argument(choices));
            if (type == "ARRAY" && value["items"] is JsonObject items) parts.Add("items:{" + Items(items) + "}");
            if (value["nullable"] is JsonValue nullable && nullable.TryGetValue<bool>(out var isNullable) && isNullable) parts.Add("nullable:true");
            if (type == "OBJECT" && value["properties"] is JsonObject inner)
            {
                parts.Add("properties:{" + Properties(inner) + "}");
                if (value["required"] is JsonArray { Count: > 0 } required)
                    parts.Add("required:[" + string.Join(',', required.Select(r => Quote((string?)r ?? ""))) + "]");
            }
            parts.Add("type:" + Quote(type));
            return p.Key + ":{" + string.Join(',', parts) + "}";
        }));

    private static string Items(JsonObject items)
        => string.Join(',', items.OrderBy(i => i.Key, StringComparer.Ordinal).Where(i => i.Value is not null).Select(i => i.Key switch
        {
            "properties" => "properties:{" + (i.Value is JsonObject inner ? Properties(inner) : "") + "}",
            "required"   => "required:[" + string.Join(',', (i.Value as JsonArray ?? []).Select(r => Quote((string?)r ?? ""))) + "]",
            "type"       => "type:" + Quote(Type(items)),
            _            => i.Key + ":" + Argument(i.Value),
        }));

    private static string Type(JsonObject schema) => ((string?)schema["type"] ?? "object").ToUpperInvariant();

    /// <summary>A call's arguments as the model writes them — <c>key:value,…</c> in key order, its strings between the
    /// model's delimiters — for a call the conversation remembers it made.</summary>
    public static string Arguments(string json)
        => JsonNode.Parse(json) is JsonObject arguments
            ? string.Join(',', arguments.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + ":" + Argument(a.Value)))
            : "";

    /// <summary>A value as the model writes it: a string between delimiters, a key bare.</summary>
    private static string Argument(JsonNode? value) => value switch
    {
        JsonObject o => "{" + string.Join(',', o.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => a.Key + ":" + Argument(a.Value))) + "}",
        JsonArray a  => "[" + string.Join(',', a.Select(Argument)) + "]",
        JsonValue v when v.TryGetValue<string>(out var s) => Quote(s),
        JsonValue v when v.TryGetValue<bool>(out var b)   => b ? "true" : "false",
        null => "null",
        _    => value.ToJsonString(),
    };

    private static string Quote(string text) => "<|\"|>" + text + "<|\"|>";
}
