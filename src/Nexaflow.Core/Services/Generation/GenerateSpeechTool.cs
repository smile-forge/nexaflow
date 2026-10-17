using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Core.Services.Voice;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>
/// Reads text aloud into a WAV file with the workspace's Speech Out voice — a sentence at a time, as a reply is spoken —
/// then saves and opens it.
/// </summary>
internal sealed class GenerateSpeechTool(ITextToSpeechProvider voice, GeneratedFiles files) : IClientTool
{
    public string Name => "generate_speech";

    public string Description => "Reads text aloud into an audio file (WAV), saves it and opens it. Not for talking to the user.";

    public IReadOnlyList<ClientToolParameter> Parameters =>
    [
        new("text", "What to read aloud, written out as it should be said."),
        new("language", "The text's language as an ISO 639-1 code (en, fr, de…), where it is not the app's own.", Required: false),
        new("path", GenerationTools.SavedWhere, Required: false),
    ];

    public ToolSafety Safety => ToolSafety.SafeOperation;

    public bool Parallelizable => false;

    public async Task<ToolResult> InvokeAsync(JsonObject arguments, CancellationToken ct)
    {
        var text = ToolArgs.Str(arguments, "text");
        if (string.IsNullOrWhiteSpace(text)) return ToolResult.Error("no text", "Give the 'text' to read aloud.");

        if (files.Refusal(ToolArgs.Str(arguments, "path")) is { } refused) return ToolResult.Error("not saved there", refused);

        var language = ToolArgs.Str(arguments, "language")?.Trim().ToLowerInvariant();
        if (language is { Length: > 0 } && !voice.Speaks(language))
            return ToolResult.Error("no voice for it", $"The voice has no '{language}' speaker.");

        byte[] pcm;
        try
        {
            pcm = await SpeakAsync(text, language is { Length: > 0 } ? language : null, ct);
        }
        catch (LlmProviderException ex)
        {
            return ToolResult.Error("could not read it out", ex.Message);
        }
        if (pcm.Length == 0) return ToolResult.Error("nothing was said", "The voice made no sound from that text.");

        var wav   = new MediaBytes(WavAudio.Write(pcm, ITextToSpeechSession.SampleRate), "audio/wav");
        var saved = await files.KeepAsync(wav, MadeKind.Recording, ToolArgs.Str(arguments, "path"), text, ct);
        return GeneratedFiles.Made(saved, null, null);
    }

    /// <summary>The whole text spoken, as 16-bit mono PCM at the session's rate.</summary>
    private async Task<byte[]> SpeakAsync(string text, string? language, CancellationToken ct)
    {
        await using var session = await voice.StartSessionAsync(new TextToSpeechOptions(), ct);
        var heard = CollectAsync(session, ct);

        foreach (var part in new ReplySegmenter(language).Finish(text))
        {
            var segment = part switch
            {
                SpokenSegment spoken => spoken,
                ShownBlock shown     => shown.Reference,
                _                    => null,
            };
            if (segment is { Text.Length: > 0 }) await session.SpeakAsync(segment.Text, segment.Language ?? language, ct);
        }
        await session.CompleteAsync(ct);
        return await heard;
    }

    private static async Task<byte[]> CollectAsync(ITextToSpeechSession session, CancellationToken ct)
    {
        using var pcm = new MemoryStream();
        await foreach (var made in session.ReadAudioAsync(ct))
            if (made is SpeechAudioChunk chunk) pcm.Write(chunk.Pcm);
        return pcm.ToArray();
    }
}
