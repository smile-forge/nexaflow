using Nexaflow.AiHost;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// What a conversation log keeps for each client — kept conversations, openings, prompts read lately — as <see cref="ClientShares{T}"/>
/// keeps it: a client finds only its own, a lone client fills the whole capacity, and beside others room is made first from the client
/// holding the most beyond its share.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class ClientSharesTests
{
    private static readonly ClientIdentity Alice = new("S-1-5-21-1", "nexaflow");
    private static readonly ClientIdentity Bob = new("S-1-5-21-2", "nexaflow");

    /// <summary>The same account, another app: a client of its own.</summary>
    private static readonly ClientIdentity AlicesOtherApp = new("S-1-5-21-1", "scribe");

    /// <summary>Adds <paramref name="item"/> for <paramref name="client"/> as a log keeps a conversation: past capacity, the one owed
    /// least is let go, and given back.</summary>
    private static string? Keep(ClientShares<string> shares, ClientIdentity client, string item)
    {
        shares.Add(client, item);
        return shares.Count > shares.Capacity ? shares.Evict(adding: null, beyondShare: true) : null;
    }

    [TestMethod]
    public void AClient_FindsItsOwnEntries_AndNeverAnothers()
    {
        var shares = new ClientShares<string>(4);
        shares.Add(Alice, "alice's conversation");

        Assert.IsFalse(shares.Of(Bob).Any(), "another account finds none of hers");
        Assert.IsFalse(shares.Of(AlicesOtherApp).Any(), "another app of the same account finds none of hers");
        Assert.IsFalse(shares.Contains(Bob, "alice's conversation"));
        Assert.IsNull(shares.Use(Bob, _ => true), "another client's turns that match hers do not make it theirs");
        Assert.IsNull(shares.Take(Bob, _ => true), "nor take it");
        Assert.IsFalse(shares.Remove(Bob, "alice's conversation"));

        Assert.AreEqual("alice's conversation", shares.Take(Alice, _ => true), "her own request finds it");
        Assert.AreEqual(0, shares.Count);
    }

    [TestMethod]
    public void AClientsEntries_AreFoundMostRecentlyUsedFirst()
    {
        var shares = new ClientShares<string>(4);
        shares.Add(Alice, "first");
        shares.Add(Bob, "bob's");
        shares.Add(Alice, "second");

        CollectionAssert.AreEqual(new[] { "second", "first" }, shares.Of(Alice).ToArray());
        Assert.AreEqual("first", shares.Use(Alice, item => item == "first"));
        CollectionAssert.AreEqual(new[] { "first", "second" }, shares.Of(Alice).ToArray(), "what is used again is the most recently used");
    }

    [TestMethod]
    public void ALoneClient_FillsTheWholeCapacity_ItsLeastRecentlyUsedLetGoPastIt()
    {
        var shares = new ClientShares<string>(4);
        foreach (var item in new[] { "a1", "a2", "a3", "a4" }) Assert.IsNull(Keep(shares, Alice, item));

        Assert.AreEqual(4, shares.Count, "alone, she holds all four");
        Assert.AreEqual("a1", Keep(shares, Alice, "a5"), "past capacity her least recently used goes");
        CollectionAssert.AreEqual(new[] { "a5", "a4", "a3", "a2" }, shares.Of(Alice).ToArray());
    }

    [TestMethod]
    public void ABusyClient_LetsItsOwnGoBeyondItsShare_BeforeAnothers()
    {
        var shares = new ClientShares<string>(4);
        Keep(shares, Alice, "a1");
        Keep(shares, Alice, "a2");
        Keep(shares, Bob, "b1");
        Keep(shares, Bob, "b2");

        Assert.AreEqual("b1", Keep(shares, Bob, "b3"), "three of four is beyond his share of two: his own oldest goes, not her older ones");
        Assert.AreEqual("b2", Keep(shares, Bob, "b4"));
        CollectionAssert.AreEqual(new[] { "a2", "a1" }, shares.Of(Alice).ToArray(), "within her share, hers stay however busy he is");
    }

    [TestMethod]
    public void AClientArriving_TakesItsShareFromTheOneHoldingMore()
    {
        var shares = new ClientShares<string>(4);
        foreach (var item in new[] { "a1", "a2", "a3", "a4" }) Keep(shares, Alice, item);

        Assert.AreEqual("a1", Keep(shares, Bob, "b1"), "alone she held all four; beside him she is beyond her share");
        Assert.AreEqual("a2", Keep(shares, Bob, "b2"));
        Assert.AreEqual("b1", Keep(shares, Bob, "b3"), "at two each, his third is beyond his share");
        CollectionAssert.AreEqual(new[] { "a4", "a3" }, shares.Of(Alice).ToArray());
        CollectionAssert.AreEqual(new[] { "b3", "b2" }, shares.Of(Bob).ToArray());
    }

    [TestMethod]
    public void RoomMadeForAClient_ComesFromItsOwnFirst_WhereItWouldHoldAsManyAsAnother()
    {
        var shares = new ClientShares<string>(4);
        shares.Add(Bob, "b1");
        shares.Add(Alice, "a1");

        Assert.AreEqual("b1", shares.Evict(adding: Bob), "with the room he is to take he holds the most: his own goes, though hers is newer");
    }

    [TestMethod]
    public void RoomMade_ComesFromTheClientHoldingTheMost_ThenTheLeastRecentlyUsed()
    {
        var shares = new ClientShares<string>(4);
        shares.Add(Alice, "a1");
        shares.Add(Alice, "a2");
        shares.Add(Alice, "a3");
        shares.Add(Bob, "b1");

        Assert.AreEqual("a1", shares.Evict(adding: Bob), "she holds three to his two with the room he takes");
        Assert.AreEqual("a2", shares.Evict(adding: Bob), "two each: hers was used least recently");
        Assert.AreEqual("b1", shares.Evict(adding: Bob), "his two to her one");
    }

    [TestMethod]
    public void WhatIsInUse_IsPassedOver_AndAClientWithinItsShareGivesNothingUp()
    {
        var shares = new ClientShares<string>(2);
        shares.Add(Alice, "alice's opening");
        shares.Add(Bob, "bob's opening, being read");

        Assert.IsNull(shares.Evict(adding: Bob, spare: item => !item.EndsWith("being read"), beyondShare: true),
                      "his own is in use, and hers is within her share: none is let go for his next");
        Assert.AreEqual("alice's opening", shares.Evict(adding: Bob, spare: item => !item.EndsWith("being read")),
                        "where room must be made, what is spare goes");
    }

    [TestMethod]
    public void AClientNotYetPresent_IsCountedInTheShares()
    {
        var shares = new ClientShares<string>(2);
        shares.Add(Alice, "a1");
        shares.Add(Alice, "a2");

        Assert.AreEqual("a1", shares.Evict(adding: Bob, beyondShare: true), "beside him her share is one");
        Assert.IsNull(shares.Evict(adding: Bob, beyondShare: true), "at one she is within it");
    }

    [TestMethod]
    public void TheEvictionOrder_IsTheOrderEntriesWouldBeLetGo()
    {
        var shares = new ClientShares<string>(4);
        shares.Add(Alice, "a1");
        shares.Add(Bob, "b1");
        shares.Add(Alice, "a2");
        shares.Add(Alice, "a3");

        CollectionAssert.AreEqual(new[] { "a1", "b1", "a2", "a3" }, shares.InEvictionOrder(adding: Bob),
                                  "she holds three to his two, then two each and his the older, then hers alone");

        var alone = new ClientShares<string>(4);
        foreach (var item in new[] { "1", "2", "3" }) alone.Add(Alice, item);
        CollectionAssert.AreEqual(new[] { "1", "2", "3" }, alone.InEvictionOrder(adding: Alice), "a lone client's go least recently used first");
    }

    [TestMethod]
    public void AClientHoldingNone_IsNoLongerPresent()
    {
        var shares = new ClientShares<string>(2);
        shares.Add(Bob, "b1");
        Assert.IsTrue(shares.Remove(Bob, "b1"));
        shares.Add(Alice, "a1");
        shares.Add(Alice, "a2");

        Assert.AreEqual(2, shares.Count);
        Assert.AreEqual("a1", Keep(shares, Alice, "a3"), "with him gone she is alone, and may hold the whole capacity");
        CollectionAssert.AreEqual(new[] { "a3", "a2" }, shares.All.ToArray());
    }

    [TestMethod]
    [DataRow(3, 0, 2, true, DisplayName = "free sequences beyond every turn still to answer")]
    [DataRow(2, 0, 2, false, DisplayName = "one fewer: a turn still to answer would find none")]
    [DataRow(2, 1, 2, true, DisplayName = "a kept conversation gives one up for it")]
    [DataRow(0, 4, 0, false, DisplayName = "none free: an opening takes no kept conversation's")]
    public void AnOpening_TakesAFreeSequence_OnlyWhereEveryTurnStillToAnswerFindsOne(int free, int reclaimable, int unanswered, bool fits)
        => Assert.AreEqual(fits, ConversationLog.OpeningFits(free, reclaimable, unanswered));
}
