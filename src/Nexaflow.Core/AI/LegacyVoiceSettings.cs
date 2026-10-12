using System.IO;
using System.Text.Json;

namespace Nexaflow.Core.AI;

/// <summary>
/// The Whisper model an earlier version's Voice settings chose, read from what they left on disk — so dictation
/// keeps the model its user picked when it moves onto the ability grid.
/// </summary>
internal static class LegacyVoiceSettings
{
    /// <summary>The model id the newest <c>voice\config_*.json</c> under <paramref name="configRoot"/> names, or
    /// <see cref="AiConfig.DefaultSpeechModel"/> when there is none or it cannot be read.</summary>
    public static string WhisperModel(string configRoot)
    {
        try
        {
            var file = new DirectoryInfo(Path.Combine(configRoot, "voice")) is { Exists: true } dir
                ? dir.GetFiles("config_*.json").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                : null;
            if (file is null) return AiConfig.DefaultSpeechModel;

            using var json = JsonDocument.Parse(File.ReadAllText(file.FullName));
            var size     = Read(json, "ModelSize", ["base", "small", "medium", "large-v3"]);
            var language = Read(json, "Language", ["english", "multilingual"]);
            if (size is null) return AiConfig.DefaultSpeechModel;

            // No English-only build of large-v3 exists; every other size has one.
            return size == "large-v3" || language == "multilingual" ? size : size + ".en";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return AiConfig.DefaultSpeechModel;
        }
    }

    /// <summary>An enum the settings saved as its name or its number, as the matching entry of
    /// <paramref name="values"/>; null when absent or unknown.</summary>
    private static string? Read(JsonDocument json, string property, string[] values)
    {
        if (!json.RootElement.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= 0 && n < values.Length)
            return values[n];
        if (value.ValueKind != JsonValueKind.String) return null;

        var name = value.GetString()?.ToLowerInvariant();
        return name switch
        {
            "largev3"     => "large-v3",
            "englishonly" => "english",
            _             => values.Contains(name) ? name : null,
        };
    }
}
