using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.AI;

internal static class AiAbilityNames
{
    public static string Friendly(AiAbility a) => a switch
    {
        AiAbility.Disambiguation             => Str.Get("Shell.Ability.Disambiguation"),
        AiAbility.GeneralIntelligence        => Str.Get("Shell.Ability.GeneralIntelligence"),
        AiAbility.ImageRecognition           => Str.Get("Shell.Ability.ImageRecognition"),
        AiAbility.SpeechIn                   => Str.Get("Shell.Ability.SpeechIn"),
        AiAbility.SpeechOut                  => Str.Get("Shell.Ability.SpeechOut"),
        AiAbility.ImageGeneration            => Str.Get("Shell.Ability.ImageGeneration"),
        AiAbility.HighQualityImageGeneration => Str.Get("Shell.Ability.ImageGenerationHq"),
        AiAbility.VideoGeneration            => Str.Get("Shell.Ability.VideoGeneration"),
        AiAbility.HighQualityVideoGeneration => Str.Get("Shell.Ability.VideoGenerationHq"),
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
