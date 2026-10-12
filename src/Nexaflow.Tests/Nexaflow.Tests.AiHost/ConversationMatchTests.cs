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
        => "<bos>" + string.Concat(turns.Select(t => $"<|turn>{t.Role.ToString().ToLowerInvariant()}\n{t.Text.Trim()}<turn|>\n"))
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
    public void NewMedia_StartsTheConversationAfresh()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];
        var image = new ChatMedia(MediaKind.Image, @"C:\shot.png", null);

        Assert.IsFalse(ConversationMatch.Continues(read, [.. read, User("What is this?", image)]),
                       "onnxruntime-genai drops media given to a continuing generator");
    }

    [TestMethod]
    public void TheDelta_IsTheReplysWholeClosing_ThenTheNewTurn()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];

        var delta = ConversationMatch.Delta(read, [.. read, User("And Germany?")], Gemma);

        Assert.AreEqual("<turn|>\n<|turn>user\nAnd Germany?<turn|>\n<|turn>model\n", delta,
                        "onnxruntime-genai never keeps the end token a reply stopped on, so the model reads it here");
    }

    [TestMethod]
    public void ATemplateThatRewritesHistory_GivesNoDelta()
    {
        ChatTurn[] read = [System, User("Capital of France?"), Model("Paris.")];
        string Rewriting(IReadOnlyList<ChatTurn> turns, bool generationPrompt)
            => Gemma(turns, generationPrompt).Replace("<bos>", generationPrompt ? "<bos>" : "<bos><|think|>");

        Assert.IsNull(ConversationMatch.Delta(read, [.. read, User("And Germany?")], Rewriting));
    }
}
