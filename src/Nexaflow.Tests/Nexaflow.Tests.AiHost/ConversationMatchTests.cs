using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>The context log's decisions: when a request continues a conversation the model holds, and what is read.</summary>
[TestClass]
[CoversNode("ai-host")]
public class ConversationMatchTests
{
    /// <summary>Gemma 4's shape: each turn closed by its end token and a newline; the reply's prompt opens the model's turn.</summary>
    private static string Gemma(IReadOnlyList<ChatTurn> turns, bool generationPrompt)
        => "<bos>" + string.Concat(turns.Select(t => $"<|turn>{(t.Role == TurnRole.Assistant ? "model" : t.Role.ToString().ToLowerInvariant())}\n{t.Text.Trim()}<turn|>\n"))
         + (generationPrompt ? "<|turn>model\n" : "");

    private static ChatTurn User(string text, params ChatMedia[] media) => new(TurnRole.User, text, media.Length == 0 ? null : media);
    private static ChatTurn Model(string text) => new(TurnRole.Assistant, text);

    private static readonly ChatTurn System = new(TurnRole.System, "Answer briefly.");

    [TestMethod]
    public void ANewTurnAfterTheReply_ContinuesTheConversation()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];

        Assert.IsTrue(ConversationMatch.Continues(read, [.. read, User("And Germany?")]));
        Assert.IsTrue(ConversationMatch.Continues(read, [System, User("Capital of France?"), Model(" Paris.\n"), User("And Germany?")]),
                      "the app's copy of the reply may differ in whitespace at its ends");
        Assert.IsTrue(ConversationMatch.Continues(read, [.. read, User("What is this?", new ChatMedia(MediaKind.Image, @"C:\shot.png", null))]),
                      "a picture joins the conversation the model holds");
    }

    [TestMethod]
    public void ARequestThatChangesWhatWasRead_DoesNotContinueIt()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];

        Assert.IsFalse(ConversationMatch.Continues(read, [System, User("Capital of Spain?"), Model("Paris."), User("And Germany?")]));
        Assert.IsFalse(ConversationMatch.Continues(read, [System, User("Capital of France?"), Model("Paris, of course."), User("And Germany?")]),
                       "a reply the app edited is not what the model read");
        Assert.IsFalse(ConversationMatch.Continues(read, read), "nothing new to answer");
        Assert.IsFalse(ConversationMatch.Continues([], [System, User("Hi")]));
    }

    [TestMethod]
    public void TheDelta_BeginsWithTheTokenTheReplyStoppedOn_ThenTheNewTurn()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];
        string prompt = Gemma(read[..2], true);

        var delta = ConversationMatch.Delta(read, prompt, "<turn|>", [.. read, User("And Germany?")], Gemma);

        Assert.AreEqual("<turn|>\n<|turn>user\nAnd Germany?<turn|>\n<|turn>model\n", delta,
                        "the token a reply stops on is never read, so the model reads it here");
    }

    [TestMethod]
    public void AGemmaReplyThatEndedInCalls_IsExtendedFromWhereTheModelWaits()
    {
        var call = new ChatToolCall("call_0", "delete_file", "{\"name\":\"test.zip\"}");
        ChatTurn[] read = [User("Delete it."), new(TurnRole.Assistant, "") { ToolCalls = [call] }];
        ChatTurn[] turns = [.. read, new(TurnRole.Tool, "Deleted.") { Answers = call }];

        // As Gemma's template renders them: the call, then the model waiting.
        static string Render(IReadOnlyList<ChatTurn> t, bool prompt)
            => "<|turn>user\nDelete it.<turn|>\n<|turn>model\n"
             + (t.Count > 1 ? "<|tool_call>call:delete_file{name:<|\"|>test.zip<|\"|>}<tool_call|><|tool_response>" : "")
             + (t.Count > 2 ? "response:delete_file{value:<|\"|>Deleted.<|\"|>}<tool_response|>" : "");

        Assert.IsTrue(ConversationMatch.Continues(read, turns));
        Assert.AreEqual("<|tool_response>response:delete_file{value:<|\"|>Deleted.<|\"|>}<tool_response|>",
                        ConversationMatch.Delta(read, Render(read[..1], true), "<|tool_response>", turns, Render));
    }

    [TestMethod]
    public void AQwenReplyThatEndedInCalls_IsExtendedFromTheEndOfItsTurn()
    {
        var call = new ChatToolCall("call_0", "delete_file", "{\"name\":\"test.zip\"}");
        ChatTurn[] read = [User("Delete it."), new(TurnRole.Assistant, "") { ToolCalls = [call] }];
        ChatTurn[] turns = [.. read, new(TurnRole.Tool, "Deleted.") { Answers = call }];

        // As Qwen's template renders them: the call ends the assistant's turn; what came of it is the user's.
        static string Render(IReadOnlyList<ChatTurn> t, bool prompt)
            => "<|im_start|>user\nDelete it.<|im_end|>\n"
             + (t.Count > 1 ? "<|im_start|>assistant\n<think>\n\n</think>\n\n<tool_call>\n<function=delete_file>\n</function>\n</tool_call><|im_end|>\n" : "")
             + (t.Count > 2 ? "<|im_start|>user\n<tool_response>\nDeleted.\n</tool_response><|im_end|>\n" : "")
             + (prompt ? "<|im_start|>assistant\n<think>\n\n</think>\n\n" : "");

        Assert.AreEqual("<|im_end|>\n<|im_start|>user\n<tool_response>\nDeleted.\n</tool_response><|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n",
                        ConversationMatch.Delta(read, Render(read[..1], true), "<|im_end|>", turns, Render));
    }

    [TestMethod]
    public void ATemplateThatRewritesHistory_GivesNoDelta()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];
        string Rewriting(IReadOnlyList<ChatTurn> turns, bool generationPrompt)
            => Gemma(turns, generationPrompt).Replace("<bos>", generationPrompt ? "<bos>" : "<bos><|think|>");

        Assert.IsNull(ConversationMatch.Delta(read, Gemma(read[..2], true), "<turn|>", [.. read, User("And Germany?")], Rewriting));
    }

    [TestMethod]
    public void APromptTheHistoryNoLongerBeginsWith_GivesNoDelta()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];

        Assert.IsNull(ConversationMatch.Delta(read, "<bos><|turn>system\nSomething else<turn|>\n", "<turn|>", [.. read, User("And Germany?")], Gemma),
                      "what the context holds is not what the template now renders");
        Assert.IsNull(ConversationMatch.Delta(read, Gemma(read[..2], true), "", [.. read, User("And Germany?")], Gemma),
                      "a reply that stopped on no token, cut short, is not continued");
    }
}
