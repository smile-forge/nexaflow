using System.Text.Json.Nodes;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>What the chat template is given: the turns, and the media a fresh read takes in.</summary>
[TestClass]
[CoversNode("ai-host")]
public class ChatPromptTests
{
    private static readonly ChatMedia Shot   = new(MediaKind.Image, @"C:\shot.png", null);
    private static readonly ChatMedia Photo  = new(MediaKind.Image, @"C:\photo.jpg", null);
    private static readonly ChatMedia Speech = new(MediaKind.Audio, null, [1, 2, 3]);

    private static readonly ChatTurn[] Conversation =
    [
        new(TurnRole.System, "Answer briefly."),
        new(TurnRole.User, "What failed?", [Shot]),
        new(TurnRole.Assistant, "Unit tests."),
        new(TurnRole.User, "And here?", [Photo]),
        new(TurnRole.Assistant, "A bear."),
        new(TurnRole.User, "Answer this.", [Speech]),
    ];

    [TestMethod]
    public void AFreshRead_TakesTheNewestImageAndRecording_WhereverTheyAre()
    {
        var plan = MediaPlan.Of(Conversation);

        Assert.AreSame(Photo, plan.Image);
        Assert.AreSame(Speech, plan.Audio);
    }

    [TestMethod]
    public void MediaRead_HasAPlaceholder_AndMediaNotRead_ANote()
    {
        var messages = JsonNode.Parse(ChatPrompt.Messages(Conversation, MediaPlan.Of(Conversation)))!.AsArray();

        Assert.AreEqual("system", (string)messages[0]!["role"]!);
        Assert.AreEqual("[image]\nWhat failed?", (string)messages[1]!["content"]!, "an image not read stays as a note");
        Assert.AreEqual("image", (string)messages[3]!["content"]![0]!["type"]!);
        Assert.AreEqual("And here?", (string)messages[3]!["content"]![1]!["text"]!);
        Assert.AreEqual("assistant", (string)messages[4]!["role"]!);
        Assert.AreEqual("audio", (string)messages[5]!["content"]![0]!["type"]!);
    }

    [TestMethod]
    public void WithNoMediaRead_EveryTurnIsText()
    {
        var messages = JsonNode.Parse(ChatPrompt.Messages(Conversation, MediaPlan.None))!.AsArray();

        Assert.IsTrue(messages.All(m => m!["content"] is JsonValue));
        Assert.AreEqual("[recording]\nAnswer this.", (string)messages[5]!["content"]!);
    }
}
