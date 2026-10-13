using Nexaflow.Providers.Common;

namespace Nexaflow.Core.AI;

/// <summary>What the workspace asks of a model. Each takes models of one <see cref="ModelKind"/>
/// (<see cref="AiAbilityNames.Kind"/>): the reasoning roles a language model, Speech In a speech-to-text one, Speech
/// Out a text-to-speech one, and the generation rows a model that makes pictures or videos.</summary>
public enum AiAbility
{
    Disambiguation,
    Conversation,
    ProblemSolving,
    ImageRecognition,
    Analysis,
    SpeechIn,
    SpeechOut,
    ImageGeneration,
    HighQualityImageGeneration,
    VideoGeneration,
}

internal static class AiAbilityNames
{
    public static string Friendly(AiAbility a) => a switch
    {
        AiAbility.Disambiguation   => "Disambiguation",
        AiAbility.Conversation     => "Conversation",
        AiAbility.ProblemSolving   => "Problem Solving",
        AiAbility.ImageRecognition => "Image Recognition",
        AiAbility.Analysis         => "Analysis",
        AiAbility.SpeechIn         => "Speech In",
        AiAbility.SpeechOut        => "Speech Out",
        AiAbility.ImageGeneration            => "Image Generation",
        AiAbility.HighQualityImageGeneration => "Image Generation (HQ)",
        AiAbility.VideoGeneration            => "Video Generation",
        _                                    => a.ToString()
    };

    /// <summary>The kind of model an ability takes — the grid offers it only columns of that kind.</summary>
    public static ModelKind Kind(AiAbility a) => a switch
    {
        AiAbility.SpeechIn        => ModelKind.SpeechToText,
        AiAbility.SpeechOut       => ModelKind.TextToSpeech,
        AiAbility.ImageGeneration or AiAbility.HighQualityImageGeneration => ModelKind.ImageGeneration,
        AiAbility.VideoGeneration => ModelKind.VideoGeneration,
        _                         => ModelKind.Language,
    };
}
