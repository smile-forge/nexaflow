namespace Nexaflow.Core.AI;

public enum AiAbility
{
    Disambiguation,
    Conversation,
    ProblemSolving,
    ImageRecognition,
    ImageGeneration,
    AudioRecognition,
    Analysis,
}

internal static class AiAbilityNames
{
    public static string Friendly(AiAbility a) => a switch
    {
        AiAbility.Disambiguation   => "Disambiguation",
        AiAbility.Conversation     => "Conversation",
        AiAbility.ProblemSolving   => "Problem Solving",
        AiAbility.ImageRecognition => "Image Recognition",
        AiAbility.ImageGeneration  => "Image Generation",
        AiAbility.AudioRecognition => "Audio Recognition",
        AiAbility.Analysis         => "Analysis",
        _                          => a.ToString()
    };
}
