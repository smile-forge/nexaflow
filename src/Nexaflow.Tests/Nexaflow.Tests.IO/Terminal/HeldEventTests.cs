using Nexaflow.IO.Common.Terminal;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Terminal;

/// <summary>What a transport raises before anyone listens is held, in order, for the first listener — kept as it was even
/// when the raiser reuses its buffer — and a later listener hears only what comes after it subscribed.</summary>
[TestClass]
[CoversNode("ioterm-transport")]
public sealed class HeldEventTests
{
    [TestMethod]
    public void WhatWasRaisedBeforeAnyoneListenedReachesTheFirstListenerInOrder()
    {
        var held = new HeldEvent<int>();
        held.Raise(1);
        held.Raise(2);

        var first = new List<int>();
        held.Add(first.Add);
        held.Raise(3);

        var later = new List<int>();
        held.Add(later.Add);
        held.Raise(4);

        CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, first);
        CollectionAssert.AreEqual(new[] { 4 }, later);
    }

    [TestMethod]
    public void AHeldBufferIsKeptAsItWasWhenRaised()
    {
        var held   = new HeldEvent<ReadOnlyMemory<byte>>(chunk => chunk.ToArray());
        var buffer = new byte[] { 1, 2, 3 };
        held.Raise(buffer);
        buffer[0] = 9;   // the raiser reuses its buffer for the next read

        byte[]? seen = null;
        held.Add(chunk => seen = chunk.ToArray());

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, seen);
    }
}
