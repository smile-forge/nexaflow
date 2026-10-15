using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Jinja;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// A conversation as the model reads it: rendered by the model's own chat template, so a new model needs no harness
/// of its own. The template is given what transformers would give it — OpenAI-style messages, the tools, the
/// generation prompt, the model's start and end tokens — with reasoning on unless the request asks otherwise: a model
/// says what its actions came to only when it has thought about them, and a quick spoken answer is asked without it.
/// Where a turn carries a picture or a
/// recording the model can take in, its content holds the projector's marker, which the medium is read in place of;
/// any other is noted in words.
/// </summary>
internal static class ChatPrompt
{
    public static string Render(ChatModel model, IReadOnlyList<ChatTurn> turns, bool generationPrompt, IReadOnlyList<ChatTool>? tools = null,
                                bool thinks = true)
    {
        try
        {
            return model.Template.Render(Variables(turns, generationPrompt, tools, model.Model.BosText, model.Model.EosText, model.MarkerFor, thinks));
        }
        catch (JinjaException ex)
        {
            throw new HostRequestException(HostErrorKind.Invalid, $"{model.Spec.Id}'s chat template refused the conversation: {ex.Message}");
        }
    }

    /// <summary>The media the rendered prompt reads, in the order their markers come.</summary>
    public static IReadOnlyList<ChatMedia> Media(ChatModel model, IEnumerable<ChatTurn> turns)
        => [.. turns.SelectMany(t => t.Media ?? []).Where(m => model.MarkerFor(m.Kind) is not null)];

    /// <param name="marker">What stands in a prompt for a medium of a kind the model reads; null for one it cannot.</param>
    /// <param name="thinks">Whether the model reasons before it answers, as the template's <c>enable_thinking</c> asks.</param>
    internal static Dictionary<string, object?> Variables(IReadOnlyList<ChatTurn> turns, bool generationPrompt, IReadOnlyList<ChatTool>? tools,
                                                          string bos, string eos, Func<MediaKind, string?> marker, bool thinks = true)
    {
        var variables = new Dictionary<string, object?>
        {
            ["messages"]              = turns.Select(t => (object?)Message(t, marker)).ToList(),
            ["add_generation_prompt"] = generationPrompt,
            ["bos_token"]             = bos,
            ["eos_token"]             = eos,
            ["enable_thinking"]       = thinks,
        };
        if (tools is { Count: > 0 })
            variables["tools"] = tools.Select(t => (object?)JinjaValues.Mapping(
            [
                ("type", "function"),
                ("function", JinjaValues.Mapping([("name", t.Name), ("description", t.Description), ("parameters", Json(t.Parameters))])),
            ])).ToList();
        return variables;
    }

    private static OrderedDictionary<string, object?> Message(ChatTurn turn, Func<MediaKind, string?> marker)
    {
        var message = JinjaValues.Mapping(
        [
            ("role", turn.Role switch { TurnRole.System => "system", TurnRole.Assistant => "assistant", TurnRole.Tool => "tool", _ => "user" }),
            ("content", Content(turn, marker)),
        ]);
        if (turn.ToolCalls is { Count: > 0 } calls)
            message["tool_calls"] = calls.Select(c => (object?)JinjaValues.Mapping(
            [
                ("id", c.Id),
                ("type", "function"),
                ("function", JinjaValues.Mapping([("name", c.Name), ("arguments", Json(c.Arguments))])),
            ])).ToList();
        if (turn.Answers is { } answered)
        {
            message["tool_call_id"] = answered.Id;
            message["name"]         = answered.Name;
        }
        return message;
    }

    /// <summary>A turn's content: its text alone, or — with media — a part for each medium, then the text.</summary>
    private static object? Content(ChatTurn turn, Func<MediaKind, string?> marker)
    {
        if (turn.Media is not { Count: > 0 } media) return turn.Text;

        var notes = media.Where(m => marker(m.Kind) is null).Select(m => m.Kind == MediaKind.Image ? "[image]" : "[recording]").ToList();
        string text = notes.Count == 0 ? turn.Text : string.Join(" ", notes) + "\n" + turn.Text;
        var read = media.Select(m => marker(m.Kind)).OfType<string>().ToList();
        if (read.Count == 0) return text;

        var parts = read.Select(m => (object?)Part(m)).ToList();
        parts.Add(Part(text));
        return parts;
    }

    private static OrderedDictionary<string, object?> Part(string text) => JinjaValues.Mapping([("type", "text"), ("text", text)]);

    /// <summary>JSON from the app — a schema, a call's arguments — as the template's values; text that is not JSON
    /// stays text, for the template to write or refuse.</summary>
    private static object? Json(string json)
    {
        try { return JinjaValues.From(JsonNode.Parse(json)); }
        catch (JsonException) { return json; }
    }
}
