using Nexaflow.Providers.Claude;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.Gemini;
using Nexaflow.Providers.Ollama;
using Nexaflow.Providers.OpenAI;
using OllamaSharp.Models.Chat;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// The neutral <see cref="LlmMessage"/> → vendor-SDK request mapping is the most regression-prone
/// provider code and used to be welded to the live network call. Each provider now exposes it as an
/// internal static builder; these tests pin the role mapping, the system-prompt split, and the
/// image-attachment encoding — all without a network.
/// </summary>
[TestClass]
public class RequestMappingTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private static List<LlmMessage> Convo(bool withImage = false) =>
    [
        new(LlmRole.System, "be helpful"),
        new(LlmRole.User, "hi")
        {
            Attachments = withImage ? [new LlmAttachment("shot.png", "image/png", Png)] : null
        },
        new(LlmRole.Assistant, "yo"),
        new(LlmRole.User, "bye"),
    ];

    // ── Claude ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Claude_SplitsSystem_AndMapsRoles()
    {
        var (system, messages) = ClaudeLlmProvider.BuildRequest(Convo());
        Assert.AreEqual("be helpful", system);
        Assert.AreEqual(3, messages.Count);   // system excluded from turns
    }

    [TestMethod]
    public void Claude_ImageAttachment_BecomesVisionBlock()
    {
        var (_, messages) = ClaudeLlmProvider.BuildRequest(Convo(withImage: true));
        // The user turn with an image carries block content (text + image), not a plain string.
        Assert.IsTrue(messages[0].Content.TryPickContentBlockParams(out var blocks) && blocks!.Count == 2,
            "Expected a text block + an image block for the image-carrying user turn.");
    }

    [TestMethod]
    public void Claude_MaxOutputTokens_ScalesByFamily()
    {
        Assert.AreEqual(4096,  ClaudeLlmProvider.DefaultMaxOutputTokens("claude-3-opus-20240229"));
        Assert.AreEqual(8192,  ClaudeLlmProvider.DefaultMaxOutputTokens("claude-3-5-sonnet-20241022"));
        Assert.AreEqual(32_000, ClaudeLlmProvider.DefaultMaxOutputTokens("claude-opus-4-8"));
        Assert.AreEqual(32_000, ClaudeLlmProvider.DefaultMaxOutputTokens("claude-fable-5"));
    }

    // ── OpenAI ────────────────────────────────────────────────────────────

    [TestMethod]
    public void OpenAI_MapsRoles_InOrder()
    {
        var messages = OpenAILlmProvider.BuildChatMessages(Convo());
        Assert.AreEqual(4, messages.Count);   // system stays inline for OpenAI
        Assert.IsInstanceOfType<global::OpenAI.Chat.SystemChatMessage>(messages[0]);
        Assert.IsInstanceOfType<global::OpenAI.Chat.UserChatMessage>(messages[1]);
        Assert.IsInstanceOfType<global::OpenAI.Chat.AssistantChatMessage>(messages[2]);
    }

    [TestMethod]
    public void OpenAI_ImageAttachment_BecomesVisionPart()
    {
        var messages = OpenAILlmProvider.BuildChatMessages(Convo(withImage: true));
        var user = (global::OpenAI.Chat.UserChatMessage)messages[1];
        Assert.AreEqual(2, user.Content.Count, "Expected a text part + an image part.");
    }

    [TestMethod]
    public void OpenAI_VisionHeuristic_ExcludesSpecialisedModels()
    {
        Assert.IsTrue(OpenAILlmProvider.ModelSupportsVision("gpt-4o"));
        Assert.IsTrue(OpenAILlmProvider.ModelSupportsVision("gpt-4.1-mini"));
        Assert.IsFalse(OpenAILlmProvider.ModelSupportsVision("text-embedding-3-large"));
        Assert.IsFalse(OpenAILlmProvider.ModelSupportsVision("gpt-3.5-turbo"));
        Assert.IsFalse(OpenAILlmProvider.ModelSupportsVision(""));
    }

    // ── Gemini ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Gemini_SplitsSystem_AndMapsRoles()
    {
        var (system, contents) = GeminiLlmProvider.BuildRequest(Convo());
        Assert.IsNotNull(system);
        Assert.AreEqual(3, contents.Count);
        Assert.AreEqual("user",  contents[0].Role);
        Assert.AreEqual("model", contents[1].Role);
    }

    [TestMethod]
    public void Gemini_ImageAttachment_BecomesInlineDataPart()
    {
        var (_, contents) = GeminiLlmProvider.BuildRequest(Convo(withImage: true));
        Assert.AreEqual(2, contents[0].Parts!.Count, "Expected a text part + an inline-data part.");
        Assert.IsNotNull(contents[0].Parts![1].InlineData);
    }

    // ── Ollama ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Ollama_KeepsSystemInline_AndMapsRoles()
    {
        var messages = OllamaLlmProvider.BuildMessages(Convo());
        Assert.AreEqual(4, messages.Count);
        Assert.AreEqual(ChatRole.System,    messages[0].Role);
        Assert.AreEqual(ChatRole.User,      messages[1].Role);
        Assert.AreEqual(ChatRole.Assistant, messages[2].Role);
    }

    [TestMethod]
    public void Ollama_VisionHeuristic_MatchesKnownFamilies()
    {
        Assert.IsTrue(OllamaLlmProvider.ModelSupportsVision("llava:13b"));
        Assert.IsTrue(OllamaLlmProvider.ModelSupportsVision("llama3.2-vision"));
        Assert.IsFalse(OllamaLlmProvider.ModelSupportsVision("llama3.1:8b"));
    }
}
