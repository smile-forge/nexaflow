using System.Text.Json.Nodes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Models;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// A conversation as the model reads it: rendered by the model's own chat template, so a new model needs no harness
/// of its own.
/// </summary>
internal static class ChatPrompt
{
    public static string Render(LoadedModel model, IReadOnlyList<ChatTurn> turns, bool generationPrompt, MediaPlan media)
        => model.Tokenizer.ApplyChatTemplate(template_str: model.Template, messages: Messages(turns, media), tools: "",
                                             add_generation_prompt: generationPrompt);

    /// <summary>The template's input: the turns as chat messages, with placeholders for the media read and a note
    /// in place of any other.</summary>
    internal static string Messages(IReadOnlyList<ChatTurn> turns, MediaPlan media)
    {
        var messages = new JsonArray();
        foreach (var turn in turns)
            messages.Add(new JsonObject
            {
                ["role"]    = turn.Role switch { TurnRole.System => "system", TurnRole.Assistant => "assistant", _ => "user" },
                ["content"] = Content(turn, media),
            });
        return messages.ToJsonString();
    }

    private static JsonNode Content(ChatTurn turn, MediaPlan media)
    {
        if (turn.Media is not { Count: > 0 } all) return turn.Text;

        var read  = all.Where(media.Reads).ToList();
        var notes = all.Except(read).Select(m => m.Kind == MediaKind.Image ? "[image]" : "[recording]").ToList();
        string text = notes.Count == 0 ? turn.Text : string.Join(" ", notes) + "\n" + turn.Text;
        if (read.Count == 0) return text;

        var content = new JsonArray();
        foreach (var item in read)
            content.Add(new JsonObject { ["type"] = item.Kind == MediaKind.Image ? "image" : "audio" });
        content.Add(new JsonObject { ["type"] = "text", ["text"] = text });
        return content;
    }
}

/// <summary>
/// The media a conversation read afresh takes in: its newest image and its newest recording, wherever they are.
/// One of each is what onnxruntime-genai reads together (two images in one prompt fail); media anywhere else stays in
/// the conversation as a note of what was there.
/// </summary>
internal sealed record MediaPlan(ChatMedia? Image, ChatMedia? Audio)
{
    public static MediaPlan None { get; } = new(null, null);

    public static MediaPlan Of(IReadOnlyList<ChatTurn> turns)
    {
        var all = turns.SelectMany(t => t.Media ?? []).ToList();
        return new MediaPlan(all.LastOrDefault(m => m.Kind == MediaKind.Image), all.LastOrDefault(m => m.Kind == MediaKind.Audio));
    }

    public bool IsEmpty => Image is null && Audio is null;

    public bool Reads(ChatMedia media) => ReferenceEquals(media, Image) || ReferenceEquals(media, Audio);
}
