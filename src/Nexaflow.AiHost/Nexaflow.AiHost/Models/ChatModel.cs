using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Jinja;
using Nexaflow.AiHost.Llama;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// A language model llama.cpp runs: the GGUF and, where it sees or hears, its projector; one KV cache its conversations
/// share; the chat template the GGUF carries, which every prompt is rendered with; and what its vocabulary says about
/// how it writes tool calls and reasons.
/// </summary>
internal sealed class ChatModel : IHostModel
{
    private ChatModel(ModelSpec spec, LlamaModel model, MediaProjector? projector, JinjaTemplate template, string templateSource)
    {
        Spec      = spec;
        Model     = model;
        Projector = projector;
        Template  = template;
        Context   = new LlamaContext(model, spec.ContextTokens, ConversationLog.Sequences);
        Rounds    = new TokenRounds(Context);
        ToolCalls = ToolCallSyntax.Of(model.SpecialToken, templateSource);
        Thinking  = ThinkingSyntax.Of(model.SpecialToken);
        Conversations = new ConversationLog(this);
    }

    public ModelSpec Spec { get; }

    public LlamaModel Model { get; }

    public LlamaContext Context { get; }

    /// <summary>The replies being written in its conversations, each token of every one read together.</summary>
    public TokenRounds Rounds { get; }

    /// <summary>What reads its pictures and recordings; null for a model of text alone.</summary>
    public MediaProjector? Projector { get; }

    /// <summary>The model's own chat template — a new model needs no harness of its own.</summary>
    public JinjaTemplate Template { get; }

    /// <summary>How the model writes its tool calls; null when its vocabulary holds no syntax for them.</summary>
    public ToolCallSyntax? ToolCalls { get; }

    /// <summary>Where the model reasons, if it does.</summary>
    public ThinkingSyntax? Thinking { get; }

    public ConversationLog Conversations { get; }

    /// <summary>What a prompt holds where a medium of <paramref name="kind"/> is read; null when the model cannot.</summary>
    public string? MarkerFor(MediaKind kind) => Projector switch
    {
        { SeesImages: true } when kind == MediaKind.Image => Projector.Marker,
        { HearsAudio: true } when kind == MediaKind.Audio => Projector.Marker,
        _ => null,
    };

    /// <summary>A prompt ready to read: its text as tokens, split where its media are read.</summary>
    public PreparedPrompt Prepare(string text, IReadOnlyList<byte[]> media)
        => media.Count == 0 ? PreparedPrompt.Text(Model.Tokenize(text)) : Projector!.Prepare(text, media);

    /// <exception cref="HostRequestException">A file would not load, or the GGUF carries no chat template the host reads.</exception>
    public static ChatModel Load(ModelSpec spec)
    {
        if (spec.File is null) throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id} names no GGUF to load.");
        IntPtr? device = spec.Device is { } number ? LlamaBackends.Gpus[number].Device : null;
        var model = LlamaModel.Load(Path.Combine(spec.Folder, spec.File), device);
        MediaProjector? projector = null;
        try
        {
            string source = model.ChatTemplate
                ?? throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id}'s GGUF carries no chat template.");
            JinjaTemplate template;
            try { template = JinjaTemplate.Parse(source); }
            catch (JinjaException ex) { throw new HostRequestException(HostErrorKind.Invalid, $"{spec.Id}'s chat template cannot be read: {ex.Message}"); }
            if (spec.Projector is { } file) projector = MediaProjector.Load(Path.Combine(spec.Folder, file), model, device);
            return new ChatModel(spec, model, projector, template, source);
        }
        catch
        {
            projector?.Dispose();
            model.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Conversations.Dispose();
        Context.Dispose();
        Projector?.Dispose();
        Model.Dispose();
    }
}

/// <summary>Language models llama.cpp runs, chatted with through their own templates.</summary>
internal sealed class ChatModelRunner : IModelRunner
{
    public string Id => "llama-chat";

    public IHostModel Load(ModelSpec spec) => ChatModel.Load(spec);
}
