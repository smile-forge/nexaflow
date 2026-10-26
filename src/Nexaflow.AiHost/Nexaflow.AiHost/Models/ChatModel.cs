using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.AiHost.Jinja;
using Nexaflow.AiHost.Llama;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// A language model llama.cpp runs: the GGUF and, where it sees or hears, its projector; the chat template the GGUF carries,
/// which every prompt is rendered with; what its vocabulary says about how it writes tool calls and reasons; and the lanes its
/// conversations are read and written in (<see cref="ChatLane"/>). Its own lane holds one cache its conversations share. Where its
/// spec allows one, a lane for work nobody is watching — a document translated passage by passage, summarised section by
/// section — is made when such work comes and its GPU has the room free, and let go once none has come for a while: many replies
/// at once are written there at the pace of few, and what it holds is the GPU's again the rest of the time.
/// </summary>
internal sealed class ChatModel : IHostModel
{
    /// <summary>How long the lane for work nobody is watching is kept once nothing is written in it: long enough for a document's
    /// next passages, short enough that what it holds is soon back.</summary>
    internal static readonly TimeSpan BackgroundIdle = TimeSpan.FromSeconds(30);

    private readonly object _lanes = new();
    private readonly Timer _letGo;
    private ChatLane? _background;
    private int _inBackground;
    private DateTime _backgroundUsed;

    private ChatModel(ModelSpec spec, LlamaModel model, MediaProjector? projector, JinjaTemplate template, string templateSource)
    {
        Spec      = spec;
        Model     = model;
        Projector = projector;
        Template  = template;
        Shared    = new ChatLane(this, spec.ContextTokens, spec.Sequences, spec.BatchTokens, perConversation: false);
        ToolCalls = ToolCallSyntax.Of(model.SpecialToken, templateSource);
        Thinking  = ThinkingSyntax.Of(model.SpecialToken);
        _letGo    = new Timer(_ => LetGoIdle(), null, BackgroundIdle, BackgroundIdle);
    }

    public ModelSpec Spec { get; }

    public LlamaModel Model { get; }

    /// <summary>Its own lane: one cache its conversations share, as long as its spec gives it.</summary>
    public ChatLane Shared { get; }

    /// <summary>What reads its pictures and recordings; null for a model of text alone.</summary>
    public MediaProjector? Projector { get; }

    /// <summary>The model's own chat template — a new model needs no harness of its own.</summary>
    public JinjaTemplate Template { get; }

    /// <summary>How the model writes its tool calls; null when its vocabulary holds no syntax for them.</summary>
    public ToolCallSyntax? ToolCalls { get; }

    /// <summary>Where the model reasons, if it does.</summary>
    public ThinkingSyntax? Thinking { get; }

    /// <summary>
    /// The lane a request is answered in, held until the returned use is disposed: the lane holding the conversation
    /// <paramref name="turns"/> continue; else, for work nobody is watching whose prompt (<paramref name="promptTokens"/>, null
    /// where it holds a medium) leaves a quarter of a slice for its reply, the lane for such work — made now, where the spec allows
    /// one and its GPU has the room free; else its own.
    /// </summary>
    public LaneUse Enter(IReadOnlyList<ChatTurn> turns, RequestPriority priority, Func<int?> promptTokens)
    {
        lock (_lanes)
        {
            if (_background is { } held && held.Conversations.Continues(turns)) return Use(held);
            if (Shared.Conversations.Continues(turns)) return new LaneUse(Shared, null);
            if (priority is not (RequestPriority.Background or RequestPriority.Job) || Spec.BackgroundSequences == 0
                || promptTokens() is not { } tokens || tokens > Spec.BackgroundTokens * 3 / 4)
                return new LaneUse(Shared, null);
            return Background() is { } lane ? Use(lane) : new LaneUse(Shared, null);
        }
    }

    /// <summary>The lane for work nobody is watching — made where its GPU has the room it holds free now; null where it has not,
    /// or llama.cpp would not make it. Called under the lanes' gate.</summary>
    private ChatLane? Background()
    {
        if (_background is not null) return _background;
        if (Spec.Device is not { } device || LlamaBackends.FreeBytes(device) < Spec.BackgroundBytes) return null;
        try
        {
            _background = new ChatLane(this, Spec.BackgroundSequences * Spec.BackgroundTokens, Spec.BackgroundSequences, LlamaContext.DefaultBatch,
                                       perConversation: true);
        }
        catch (HostRequestException)
        {
            return null;   // its work is done in the model's own lane, as it would be with no room for this one
        }
        return _background;
    }

    private LaneUse Use(ChatLane lane)
    {
        _inBackground++;
        return new LaneUse(lane, () =>
        {
            lock (_lanes)
            {
                _inBackground--;
                _backgroundUsed = DateTime.UtcNow;
            }
        });
    }

    /// <summary>Lets the lane for work nobody is watching go once nothing has been written in it for <see cref="BackgroundIdle"/>.</summary>
    private void LetGoIdle()
    {
        ChatLane? idle = null;
        lock (_lanes)
        {
            if (_background is null || _inBackground > 0 || DateTime.UtcNow - _backgroundUsed < BackgroundIdle) return;
            idle = _background;
            _background = null;
        }
        idle.Dispose();
    }

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
        var model = LlamaModel.Load(Path.Combine(spec.Folder, spec.File), device, spec.GpuLayers);
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
        _letGo.Dispose();
        lock (_lanes)
        {
            _background?.Dispose();
            _background = null;
        }
        Shared.Dispose();
        Projector?.Dispose();
        Model.Dispose();
    }
}

/// <summary>A request's use of a lane, until it is disposed.</summary>
internal sealed class LaneUse(ChatLane lane, Action? done) : IDisposable
{
    private int _done;

    public ChatLane Lane { get; } = lane;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _done, 1) == 0) done?.Invoke();
    }
}

/// <summary>Language models llama.cpp runs, chatted with through their own templates.</summary>
internal sealed class ChatModelRunner : IModelRunner
{
    public string Id => "llama-chat";

    public IHostModel Load(ModelSpec spec) => ChatModel.Load(spec);
}
