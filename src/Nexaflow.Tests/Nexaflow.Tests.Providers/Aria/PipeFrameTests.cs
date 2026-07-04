using System.Text.Json;
using Nexaflow.Providers.Aria;

namespace Nexaflow.Tests.Providers.Aria;

/// <summary>
/// Wire-contract tests for the Aria named-pipe envelope (<see cref="PipeFrame"/> + <see cref="SimpleMessage"/>).
/// The client serialises frames with case-insensitive, default-enum (numeric) JSON; these assert the DTOs
/// survive that round-trip both ways.
/// </summary>
[TestClass]
public class PipeFrameTests
{
    // Mirrors AriaNamedPipeClient.JsonOptions.
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [TestMethod]
    public void UserMessageFrame_RoundTrips_WithPayload()
    {
        var frame = new PipeFrame
        {
            Type          = PipeFrameType.UserMessage,
            WindowsUserId = @"CONTOSO\jdoe",
            Payload       = new SimpleMessage { Text = "hello", AttachmentPaths = { @"C:\a.png", @"C:\b.txt" } }
        };

        var rt = JsonSerializer.Deserialize<PipeFrame>(JsonSerializer.Serialize(frame, Options), Options)!;

        Assert.AreEqual(PipeFrameType.UserMessage, rt.Type);
        Assert.AreEqual(@"CONTOSO\jdoe", rt.WindowsUserId);
        Assert.IsNotNull(rt.Payload);
        Assert.AreEqual("hello", rt.Payload!.Text);
        CollectionAssert.AreEqual(new[] { @"C:\a.png", @"C:\b.txt" }, rt.Payload.AttachmentPaths);
    }

    [TestMethod]
    public void TypingFrame_RoundTrips_WithNullPayload()
    {
        var frame = new PipeFrame { Type = PipeFrameType.TypingStarted, WindowsUserId = "u" };

        var rt = JsonSerializer.Deserialize<PipeFrame>(JsonSerializer.Serialize(frame, Options), Options)!;

        Assert.AreEqual(PipeFrameType.TypingStarted, rt.Type);
        Assert.IsNull(rt.Payload);
    }

    [TestMethod]
    public void Deserialize_IsCaseInsensitive_AndEnumsAreNumeric()
    {
        // Lowercased keys + numeric discriminator, as the server emits them.
        const string json = """
            { "type": 2, "windowsUserId": "u", "payload": { "text": "hi", "attachmentPaths": [] } }
            """;

        var rt = JsonSerializer.Deserialize<PipeFrame>(json, Options)!;

        Assert.AreEqual(PipeFrameType.AssistantMessage, rt.Type);   // 2 == AssistantMessage
        Assert.AreEqual("hi", rt.Payload!.Text);
        Assert.AreEqual(0, rt.Payload.AttachmentPaths.Count);
    }

    [TestMethod]
    public void SimpleMessage_AttachmentPaths_DefaultsToEmpty_NotNull()
        => Assert.AreEqual(0, new SimpleMessage().AttachmentPaths.Count);
}
