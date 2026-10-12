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
/// Stop beside it: Stop leaves the file where it is, and asked again, Go ahead deletes it. Asked to delete a folder,
/// and told over the announcement to create another as well, it holds the delete — Abort alone — and does both once
/// the user has been quiet a while. Opening the conversation as a conversation page ends voice mode.
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
    private string _typing = "";

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
        Directory.CreateDirectory(Path.Combine(_folder, "old"));

        var version = FileVersionInfo.GetVersionInfo(FindAppExe()).FileVersion ?? "1.0.0.0";

        // The workspace opens on the folder, so nothing has to be typed to get there.
        var workspaces = Path.Combine(configDir, "workcontexts");
        Directory.CreateDirectory(workspaces);
        File.WriteAllText(Path.Combine(workspaces, $"config_{version}.json"), $$"""
            {
              "ConfigName": "workcontexts",
              "FriendlyName": "Workspaces",
              "Contexts": [
                {
                  "DefaultTabs": [
                    { "PageKind": "FileSystem", "PageParams": { "mode": "path", "path": {{JsonSerializer.Serialize(_folder)}} }, "Pane": 0, "Title": "voice", "IsActive": true }
                  ],
                  "LastSessionTabs": [],
                  "Name": "Default",
                  "Color": "#5B8CFF",
                  "Icon": "\u2B21"
                }
              ]
            }
            """);
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
    Check("the file to delete is selected", () => InFront() && SelectInFileList(FileName));

        // The mic converses once the workspace's speech models are confirmed — until then it would dictate, and every turn
        // below would go to the assistant as a typed request instead.
        bool converses = WaitForFs(() => WaitForId("AiBar_Mic", 1)?.Properties.ItemStatus.ValueOrDefault == bool.TrueString, Affordable(120));
        Check("the mic offers a conversation once the speech models are confirmed", () => converses);
        bool started = false;
        if (converses)
            CheckDoes("the mic starts a conversation", "AiBar_Mic",
                      () => started = WaitForId("AiBar_VoiceOpenConversation", Affordable(60)) is not null);
        if (!started)
        {
            AssertJourney();   // nothing below means anything without a conversation
            return;
        }

    bool sent = TypeTurn("Delete the selected file.");
    // Shown above the box for a while after it is taken, then faded — so looked for at once.
    Check("the turn shows above the box, as the assistant was given it",
          () => sent && WaitForId("AiBar_VoiceTurn", 5)?.Name == "Delete the selected file.");
    bool announced = sent && WaitForId("AiBar_VoiceStop", Affordable(90)) is not null;
    Check("the assistant's line says what it will do", () => WaitForId("AiBar_VoiceReply", 5)?.Name is { Length: > 0 });
        CheckDoes("Stop leaves the file where it is", "AiBar_VoiceStop",
                  () => WaitForGone("AiBar_VoiceStop", 10) && !WaitForFs(() => !File.Exists(file), 5));

        bool announcedAgain = TypeTurn("Delete the selected file.") && WaitForId("AiBar_VoiceProceed", Affordable(60)) is not null;
    CheckDoes("Go ahead deletes it", "AiBar_VoiceProceed", () => WaitForFs(() => !File.Exists(file), 30));

        // More to do, typed over an announced action: added to the work, the action held — Abort alone, in red — and both
        // done once the user has been quiet a while.
        bool held = TypeTurn("Delete the folder named old.") && WaitForId("AiBar_VoiceStop", Affordable(60)) is not null
                 && TypeTurn("Also create a folder called archive.")
                 && WaitForGone("AiBar_VoiceProceed", Affordable(60)) && WaitForId("AiBar_VoiceStop", 5) is not null;
        bool bothDone = held && WaitForFs(() => !Directory.Exists(Path.Combine(_folder, "old")) && Directory.Exists(Path.Combine(_folder, "archive")),
                                          Affordable(90));

        CheckDoes("the conversation opens as a conversation page, and voice mode ends", "AiBar_VoiceOpenConversation",
                  () => WaitForId("Conversation_ToggleContext", 20) is not null && WaitForGone("AiBar_VoiceOpenConversation", 10));

    // Checked last, so a miss can say what the conversation was — it is on disk once it has been opened.
        Check("every turn was typed and sent" + _typing, () => _typing.Length == 0);
        Check("a typed turn is announced as an action, with Stop beside it" + (announced ? "" : Transcript()), () => announced);
    Check("asked again, it is announced again" + (announcedAgain ? "" : Transcript()), () => announcedAgain);
        Check("more to do over an announcement holds it, with Abort alone" + (held ? "" : Transcript()), () => held);
        Check("once the user is quiet, the held action and what was added are both done" + (bothDone || !held ? "" : Transcript()),
              () => bothDone);

        AssertJourney();
    }

    /// <summary>Types a turn into the bar and sends it with Enter, as a user does: while talking, a turn of the spoken
    /// conversation. Nothing is typed unless the app is in front. The box must be empty before — the last turn was taken —
    /// and is again once this one has been. A miss is recorded in <see cref="_typing"/>, for the check that reports it.</summary>
    private bool TypeTurn(string text)
    {
        var bar = WaitForId("AiInputBox", 10);
        if (bar is null) return Missed(text, "the box is not on screen");
        if (!WaitForFs(() => bar.IsEnabled && bar.AsTextBox().Text.Length == 0, 30))
            return Missed(text, $"the box was not free (enabled: {bar.IsEnabled}, holding \"{bar.AsTextBox().Text}\")");
        if (!InFront()) return Missed(text, "the app would not come to the front, so nothing was typed");
        bar.Click();
        Keyboard.Type(text);
        Wait.UntilInputIsProcessed();
        if (bar.AsTextBox().Text is var typed && typed != text) return Missed(text, $"the box held \"{typed}\" once typed into");
        if (!InFront()) return Missed(text, "the app lost the front before Enter, so it was not pressed");
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => bar.AsTextBox().Text.Length == 0, 10)
            || Missed(text, $"the box still held \"{bar.AsTextBox().Text}\" once sent");
    }

    private bool Missed(string text, string why)
    {
        _typing += $"{Environment.NewLine}  typing \"{text}\": {why}";
        return false;
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
