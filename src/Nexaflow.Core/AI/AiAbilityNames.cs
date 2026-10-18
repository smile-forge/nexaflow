using Nexaflow.Providers.Common;

namespace Nexaflow.Core.AI;

internal static class AiAbilityNames
{
    public static string Friendly(AiAbility a) => a switch
    {
        AiAbility.Disambiguation   => "Disambiguation",
        AiAbility.GeneralIntelligence => "General Intelligence",
        AiAbility.ImageRecognition => "Image Recognition",
        AiAbility.SpeechIn         => "Speech In",
        AiAbility.SpeechOut        => "Speech Out",
        AiAbility.ImageGeneration            => "Image Generation",
        AiAbility.HighQualityImageGeneration => "Image Generation (HQ)",
        AiAbility.VideoGeneration            => "Video Generation",
        AiAbility.HighQualityVideoGeneration => "Video Generation (HQ)",
        _                                    => a.ToString()
    };

    /// <summary>The kind of model an ability takes — the grid offers it only columns of that kind.</summary>
    public static ModelKind Kind(AiAbility a) => a switch
    {
        AiAbility.SpeechIn        => ModelKind.SpeechToText,
        AiAbility.SpeechOut       => ModelKind.TextToSpeech,
        AiAbility.ImageGeneration or AiAbility.HighQualityImageGeneration => ModelKind.ImageGeneration,
        AiAbility.VideoGeneration or AiAbility.HighQualityVideoGeneration => ModelKind.VideoGeneration,
        _                         => ModelKind.Language,
    };
}
