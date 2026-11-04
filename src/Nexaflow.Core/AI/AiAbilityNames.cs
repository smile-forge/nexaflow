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
        AiAbility.Documents                  => Str.Get("Shell.Ability.Documents"),
        AiAbility.SpeechIn                   => Str.Get("Shell.Ability.SpeechIn"),
        AiAbility.SpeechOut                  => Str.Get("Shell.Ability.SpeechOut"),
        AiAbility.ImageGeneration            => Str.Get("Shell.Ability.ImageGeneration"),
        AiAbility.HighQualityImageGeneration => Str.Get("Shell.Ability.ImageGenerationHq"),
        AiAbility.ImageEditing               => Str.Get("Shell.Ability.ImageEditing"),
        AiAbility.VideoGeneration            => Str.Get("Shell.Ability.VideoGeneration"),
        AiAbility.HighQualityVideoGeneration => Str.Get("Shell.Ability.VideoGenerationHq"),
        _                                    => a.ToString()
    };

    /// <summary>The kind of model an ability takes — the grid offers it only columns of that kind.</summary>
    public static ModelKind Kind(AiAbility a) => a switch
    {
        AiAbility.SpeechIn        => ModelKind.SpeechToText,
        AiAbility.SpeechOut       => ModelKind.TextToSpeech,
        AiAbility.ImageGeneration or AiAbility.HighQualityImageGeneration or AiAbility.ImageEditing => ModelKind.ImageGeneration,
        AiAbility.VideoGeneration or AiAbility.HighQualityVideoGeneration => ModelKind.VideoGeneration,
        _                         => ModelKind.Language,
    };

    /// <summary>
    /// Whether <paramref name="model"/> — one of the provider whose capability instance is <paramref name="provider"/> — serves
    /// <paramref name="ability"/> beyond being of its <see cref="Kind"/>: Image Editing takes only a model that works from
    /// pictures. A model whose provider is not known cannot be judged, so it serves only rows that ask no more than its kind.
    /// </summary>
    public static bool Serves(AiAbility ability, IAiProvider? provider, string model) => ability switch
    {
        AiAbility.ImageEditing => provider is IImageGenerationProvider images && images.MaxSourcesOf(model) > 0,
        AiAbility.ImageGeneration or AiAbility.HighQualityImageGeneration => provider is not IImageGenerationProvider drawing || drawing.DrawsFromPromptsOf(model),
        _                      => true,
    };
}
