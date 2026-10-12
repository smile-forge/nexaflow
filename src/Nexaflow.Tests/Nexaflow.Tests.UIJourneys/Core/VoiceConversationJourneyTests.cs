using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// The input bar in voice mode, on Nexaflow's own models. The mic starts a spoken conversation because the workspace's
/// models can hold one, and the turns are typed into the bar — a turn of the same conversation, answered aloud — so a
/// journey can drive it without a voice. Asked to delete the selected file, the assistant announces it in the bar with
/// Stop beside it: Stop leaves the file where it is, and asked again, Go ahead deletes it. Opening the conversation as
/// a conversation page ends voice mode.
/// <para>
/// <c>NEXAFLOW_TEST_AI_DIR</c> names an AI folder holding the workspace's models as the app keeps them — Gemma 4 E4B,
/// Nemotron with Whisper turbo, Kokoro, each file verified — which the app is started on as its
/// <c>NEXAFLOW_AI_DIR</c>; without it the journey is inconclusive. The app listens through the default microphone and
/// speaks through the speakers, so it wants a quiet room: words heard over an announcement answer it.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[TestCategory("OwnedModel")]
[CoversNode("voice-session")]
public class VoiceConversationJourneyTests : UiJourneyTestBase
{
    private const string AiDirVariable = "NEXAFLOW_TEST_AI_DIR";
    private const string FileName = "test.zip";

    private string _aiDir = "";
    private string _folder = "";

    /// <summary>The first turn loads the language model, and starting the conversation the listening models and the voice.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromMinutes(8);

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    /// <summary>A folder with the file to delete, and the default workspace's abilities on the owned models.</summary>
    protected override void SeedConfig(string configDir)
    {
        var aiDir = Environment.GetEnvironmentVariable(AiDirVariable);
        if (string.IsNullOrEmpty(aiDir) || !Directory.Exists(aiDir))
        {
            Assert.Inconclusive($"{AiDirVariable} names no folder of Nexaflow's own models on this machine.");
            return;
        }
        _aiDir  = aiDir;
        _folder = Directory.CreateTempSubdirectory("nexaflow-voice-").FullName;
        File.WriteAllBytes(Path.Combine(_folder, FileName), [0x50, 0x4B, 0x05, 0x06, .. new byte[18]]);   // an empty zip

        var version = FileVersionInfo.GetVersionInfo(FindAppExe()).FileVersion ?? "1.0.0.0";
        var abilities = Path.Combine(configDir, "Contexts", "Default", "ai-abilities");
        Directory.CreateDirectory(abilities);
        File.WriteAllText(Path.Combine(abilities, $"config_{version}.json"), """
            {
              "ConfigName": "ai-abilities",
              "FriendlyName": "AI",
              "Columns": [
                { "Id": "chat",   "ProviderName": "Nexaflow", "Model": "gemma-4-e4b-it",         "Kind": "Language",     "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" },
                { "Id": "listen", "ProviderName": "Nexaflow", "Model": "nemotron-whisper-turbo", "Kind": "SpeechToText", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" },
                { "Id": "speak",  "ProviderName": "Nexaflow", "Model": "kokoro-82m",             "Kind": "TextToSpeech", "AssemblyFileName": "Nexaflow.Providers.AiHost.dll" }
              ],
              "Assignments": {
                "Conversation":     [ "chat" ],
                "Disambiguation":   [ "chat" ],
                "ProblemSolving":   [ "chat" ],
                "Analysis":         [ "chat" ],
                "ImageRecognition": [ "chat" ],
                "SpeechIn":         [ "listen" ],
                "SpeechOut":        [ "speak" ]
              }
            }
            """);
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { if (_folder.Length > 0 && Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
    }

    [TestMethod]
    public void AnAnnouncedAction_IsStoppedOrDone_FromTheBar()
    {
        var file = Path.Combine(_folder, FileName);
        NavigateFileBrowserTo(_folder);
        Check("the file to delete is selected", () => SelectInFileList(FileName));

        CheckDoes("the mic starts a conversation", "AiBar_Mic",
                  () => WaitForId("AiBar_VoiceOpenConversation", Affordable(120)) is not null);

        bool announced = TypeTurn("Delete the selected file.") && WaitForId("AiBar_VoiceStop", Affordable(240)) is not null;
        CheckDoes("Stop leaves the file where it is", "AiBar_VoiceStop",
                  () => WaitForGone("AiBar_VoiceStop", 10) && !WaitForFs(() => !File.Exists(file), 5));

        bool announcedAgain = TypeTurn("Delete the selected file.") && WaitForId("AiBar_VoiceProceed", Affordable(120)) is not null;
        CheckDoes("Go ahead deletes it", "AiBar_VoiceProceed", () => WaitForFs(() => !File.Exists(file), 30));

        CheckDoes("the conversation opens as a conversation page, and voice mode ends", "AiBar_VoiceOpenConversation",
                  () => WaitForId("Conversation_ToggleContext", 20) is not null && WaitForGone("AiBar_VoiceOpenConversation", 10));

        // Checked last, so a miss can say what the conversation was — it is on disk once it has been opened.
        Check("a typed turn is announced as an action, with Stop beside it" + (announced ? "" : Transcript()), () => announced);
        Check("asked again, it is announced again" + (announcedAgain ? "" : Transcript()), () => announcedAgain);

        AssertJourney();
    }

    /// <summary>Types a turn into the bar and sends it: while talking, a turn of the spoken conversation. The box must be
    /// empty before — the last turn was taken — and is again once this one has been.</summary>
    private bool TypeTurn(string text)
    {
        var bar = WaitForId("AiInputBox", 10);
        if (bar is null || !WaitForFs(() => bar.IsEnabled && bar.AsTextBox().Text.Length == 0, 30)) return false;
        bar.Click();
        Keyboard.Type(text);
        Wait.UntilInputIsProcessed();
        if (bar.AsTextBox().Text != text) return false;
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => bar.AsTextBox().Text.Length == 0, 10);
    }

    /// <summary>The newest conversation saved in the app's config — the voice conversation, once opened as a page.</summary>
    private string Transcript()
    {
        var saved = new DirectoryInfo(Path.Combine(ConfigDir, "Contexts", "Default", "Conversations"));
        var newest = saved.Exists ? saved.EnumerateFiles("conversation.json", SearchOption.AllDirectories).MaxBy(f => f.LastWriteTimeUtc) : null;
        if (newest is null) return " (no conversation was saved)";
        using var json = JsonDocument.Parse(File.ReadAllText(newest.FullName));
        var lines = json.RootElement.GetProperty("Messages").EnumerateArray()
            .Select(m => (m.GetProperty("IsUser").GetBoolean() ? "  user: " : "  assistant: ") + m.GetProperty("Text").GetString());
        return " — the conversation was:" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

}
