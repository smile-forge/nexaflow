using Nexaflow.Providers.Common;

namespace Nexaflow.Core.AI;

/// <summary>What the workspace asks of a model. Each takes models of one <see cref="ModelKind"/>
/// (<see cref="AiAbilityNames.Kind"/>): the reasoning roles a language model, Speech In a speech-to-text one.</summary>
public enum AiAbility
{
    Disambiguation,
    Conversation,
    ProblemSolving,
    ImageRecognition,
    Analysis,
    SpeechIn,
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
        _                          => a.ToString()
    };

    /// <summary>The kind of model an ability takes — the grid offers it only columns of that kind.</summary>
    public static ModelKind Kind(AiAbility a) => a switch
    {
        AiAbility.SpeechIn => ModelKind.SpeechToText,
        _                  => ModelKind.Language,
    };
}
