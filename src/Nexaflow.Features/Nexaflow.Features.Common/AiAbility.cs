namespace Nexaflow.Features.Common;

/// <summary>What the workspace asks of a model. Each row of the workspace's ability grid takes models of one kind: the
/// reasoning roles a language model, Speech In a speech-to-text one, Speech Out a text-to-speech one, and the generation
/// rows a model that makes pictures or videos. Saved by name, so the order here is free to change.</summary>
/// <remarks><see cref="GeneralIntelligence"/> holds the conversation and every one-shot piece of thinking the app asks for —
/// solving a problem, analysing or condensing a conversation. Disambiguation routes requests and Image Recognition looks at
/// pictures; each takes its own model, the smaller and cheaper the better.</remarks>
public enum AiAbility
{
    Disambiguation,
    GeneralIntelligence,
    ImageRecognition,
    SpeechIn,
    SpeechOut,
    ImageGeneration,
    HighQualityImageGeneration,
    VideoGeneration,
    HighQualityVideoGeneration,
}

/// <summary>Where an ability stands in the workspace now.</summary>
public enum AiAbilityState
{
    /// <summary>No model is assigned to it in the workspace's ability grid: what needs it is disabled, and the assistant is
    /// not offered tools that need it.</summary>
    Unassigned,

    /// <summary>Models are assigned, but none of them can be reached — a service with no key, say. What needs it is
    /// disabled, as for <see cref="Unassigned"/>.</summary>
    Unavailable,

    /// <summary>A model of Nexaflow's own is assigned and not downloaded yet: the first use offers its download.</summary>
    NeedsDownload,

    /// <summary>The model assigned is downloading; it is ready when the download ends.</summary>
    Downloading,

    /// <summary>A model serves it now.</summary>
    Ready,
}

/// <summary>An ability's state, and for one waiting on a download, how much is left to fetch and how far it has got.</summary>
public readonly record struct AiAbilityStatus(AiAbilityState State, long DownloadBytes = 0, int DownloadPercent = 0)
{
    /// <summary>Something can be done with it now or once a download lands: what needs it is enabled.</summary>
    public bool IsUsable => State is AiAbilityState.Ready or AiAbilityState.NeedsDownload or AiAbilityState.Downloading;

    public static AiAbilityStatus Unassigned { get; } = new(AiAbilityState.Unassigned);
    public static AiAbilityStatus Ready { get; } = new(AiAbilityState.Ready);
}
