using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>A model's reasoning as an app that asked for it is sent it: from its first word, without the name of the channel it is
/// written in, each piece as it comes once the start is told.</summary>
[TestClass]
[CoversNode("ai-host")]
public class ReasoningStreamTests
{
    private static string[] Sent(string? heading, params string[] pieces)
    {
        var stream = new ReasoningStream(heading);
        return [.. pieces.Select(stream.Take)];
    }

    [TestMethod]
    public void GemmasChannelName_IsLeftOut_HoweverItsTokensSplitIt()
    {
        CollectionAssert.AreEqual(new[] { "", "", "", "The file", " is in Downloads." },
                                  Sent("thought", "th", "ought", "\n", "The file", " is in Downloads."));
    }

    [TestMethod]
    public void ReasoningThatOnlyBeginsLikeTheChannelName_IsSentWhole()
    {
        CollectionAssert.AreEqual(new[] { "", "thoughtful", " answer" }, Sent("thought", "though", "tful", " answer"));
    }

    [TestMethod]
    public void AModelWithNoChannelName_HasOnlyTheWhitespaceBeforeItsFirstWordLeftOut()
    {
        CollectionAssert.AreEqual(new[] { "", "Check", " the bin." }, Sent(null, "\n", "Check", " the bin."));
    }
}
