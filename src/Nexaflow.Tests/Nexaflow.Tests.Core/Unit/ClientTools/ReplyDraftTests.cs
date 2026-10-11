using Nexaflow.Core.Services.Agent;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.ClientTools;

[TestClass]
[CoversNode("chat-interface")]
public class ReplyDraftTests
{
    [TestMethod]
    public void TextShowsAsItArrives()
    {
        var draft = new ReplyDraft();

        draft.Append("The build ");
        Assert.AreEqual("The build", draft.Text);

        draft.Append("failed.\nThe tes");
        Assert.AreEqual("The build failed.\nThe tes", draft.Text);
    }

    [TestMethod]
    public void ABlockTheAgentActsOn_NeverShows_WhereverTheStreamCutsIt()
    {
        const string reply = "Looking.\n```client_tool\n{\"tool\":\"get_x\",\"arguments\":{}}\n```\nThen more.";

        for (var size = 1; size <= reply.Length; size++)
        {
            var draft = new ReplyDraft();
            for (var i = 0; i < reply.Length; i += size)
            {
                draft.Append(reply.Substring(i, Math.Min(size, reply.Length - i)));
                Assert.IsFalse(draft.Text.Contains('`') || draft.Text.Contains("get_x"),
                    $"pieces of {size}: the draft showed '{draft.Text}'");
            }
            Assert.AreEqual("Looking.\nThen more.", draft.Text, $"pieces of {size}");
        }
    }

    [TestMethod]
    public void AnotherBlock_ShowsWhole_OnceItIsClosed()
    {
        var draft = new ReplyDraft();

        draft.Append("Here:\n```mermaid\nflowchart LR\n  a --> b\n");
        Assert.AreEqual("Here:", draft.Text, "a half-written diagram has nothing true to draw");

        draft.Append("```\nDone.");
        Assert.AreEqual("Here:\n```mermaid\nflowchart LR\n  a --> b\n```\nDone.", draft.Text);
    }

    [TestMethod]
    public void TheDraftOfAWholeReply_IsWhatTheParserShows()
    {
        const string reply = "First.\r\n\r\n```json\n{\"a\":1}\n```\n```client_plan\n{\"title\":\"p\"}\n```\nLast line";

        var draft = new ReplyDraft();
        draft.Append(reply);

        Assert.AreEqual(ClientBlockParser.Parse(reply).ExplanationMarkdown, draft.Text);
    }
}
