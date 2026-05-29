using Nexaflow.Features.Common;

namespace Nexaflow.Core;

public enum WhisperModelSize { Base, Small, Medium, LargeV3 }
public enum WhisperLanguage  { EnglishOnly, Multilingual }

/// <summary>
/// General (non-WorkContext) voice-input settings. Registered manually in App.xaml.cs; surfaces in
/// the Options panel as a "Voice" section via <see cref="Controls.VoiceConfigControl"/>, which adds
/// a model download-status indicator alongside the two enum selectors.
/// </summary>
[CustomControl(typeof(Controls.VoiceConfigControl))]
public sealed class VoiceConfig : IFeatureConfig
{
    public string ConfigName   => "voice";
    public string FriendlyName => "Voice";

    [ConfigDisplayName("Model Size")]
    public WhisperModelSize ModelSize { get; set; } = WhisperModelSize.Base;

    [ConfigDisplayName("Language")]
    public WhisperLanguage Language { get; set; } = WhisperLanguage.EnglishOnly;
}

/// <summary>
/// Maps a <see cref="VoiceConfig"/> selection to the ggml model file and its
/// download URL on Hugging Face.
/// </summary>
public static class WhisperModelCatalog
{
    private const string BaseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

    /// <summary>ggml file name for the selection. LargeV3 has no English-only build.</summary>
    public static string FileName(WhisperModelSize size, WhisperLanguage language)
    {
        bool en = language == WhisperLanguage.EnglishOnly;
        return size switch
        {
            WhisperModelSize.Base    => en ? "ggml-base.en.bin"   : "ggml-base.bin",
            WhisperModelSize.Small   => en ? "ggml-small.en.bin"  : "ggml-small.bin",
            WhisperModelSize.Medium  => en ? "ggml-medium.en.bin" : "ggml-medium.bin",
            // No large-v3.en exists — always the multilingual file.
            WhisperModelSize.LargeV3 => "ggml-large-v3.bin",
            _                        => "ggml-base.en.bin",
        };
    }

    public static string FileName(VoiceConfig cfg) => FileName(cfg.ModelSize, cfg.Language);

    public static string DownloadUrl(VoiceConfig cfg) => BaseUrl + FileName(cfg);
}
