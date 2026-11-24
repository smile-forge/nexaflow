using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// What a session sends its client: replies queue behind what keeps the connection going, a full queue holds back replies alone,
/// and what waits on a reply being written is told once it is — or once the session ends without it.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class SessionOutboxTests
{
    /// <summary>A pipe the client has stopped reading: every write waits until it is let go, then lands in memory.</summary>
    private sealed class HeldStream : Stream
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _writing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly MemoryStream Written = new();
        public bool Broken { get; init; }

        /// <summary>Completes once a write is waiting on the held pipe.</summary>
        public Task Writing => _writing.Task;

        public void Open() => _open.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            _writing.TrySetResult();
            await _open.Task.WaitAsync(ct);
            if (Broken) throw new IOException("The pipe is broken.");
            lock (Written) Written.Write(buffer.Span);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count, default).GetAwaiter().GetResult();
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static async Task<List<HostMessage>> ReadAllAsync(MemoryStream written)
    {
        var read = new HostChannel(new MemoryStream(written.ToArray()));
        var messages = new List<HostMessage>();
        while (await read.ReceiveAsync() is { } message) messages.Add(message);
        return messages;
    }

    [TestMethod]
    public async Task AFullReplyQueue_HoldsBackReplies_WhileWhatKeepsTheConnectionGoingGoesAheadOfThem()
    {
        var stream = new HeldStream();
        var outbox = new SessionOutbox(new HostChannel(stream), new HostLog(null));

        var first = outbox.SendAsync(new TextDelta(1, "0"));
        await stream.Writing;   // the first reply is in the pipe's hands, held there
        var queued = Enumerable.Range(1, SessionOutbox.MostReplies).Select(i => outbox.SendAsync(new TextDelta(1, i.ToString()))).ToList();
        var waiting = outbox.SendAsync(new TextDelta(1, "past the queue"));

        Assert.IsTrue(first.IsCompleted && queued.All(t => t.IsCompleted), "replies queue at once while there is room");
        Assert.IsFalse(waiting.IsCompleted, "one past the queue waits for room");
        outbox.Tell(new Pong(42));   // never waits

        stream.Open();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        await outbox.DisposeAsync();

        var messages = await ReadAllAsync(stream.Written);
        Assert.IsInstanceOfType<Pong>(messages[1], "the heartbeat's answer goes ahead of every reply that was waiting");
        CollectionAssert.AreEqual(Enumerable.Range(0, SessionOutbox.MostReplies + 1).Select(i => i.ToString()).Append("past the queue").ToList(),
                                  messages.OfType<TextDelta>().Select(d => d.Text).ToList(), "and the replies keep their order");
    }

    [TestMethod]
    public async Task WhatWaitsOnAReply_IsToldOnceItIsWritten_OrOnceTheSessionEndsWithoutIt()
    {
        var stream = new HeldStream();
        var outbox = new SessionOutbox(new HostChannel(stream), new HostLog(null));
        int written = 0;

        await outbox.SendAsync(new Completed(1), () => Interlocked.Increment(ref written));
        await stream.Writing;
        Assert.AreEqual(0, written, "not while it waits on the pipe");
        stream.Open();
        await outbox.DisposeAsync();
        Assert.AreEqual(1, written);

        var broken = new HeldStream { Broken = true };
        var gone = new SessionOutbox(new HostChannel(broken), new HostLog(null));
        int letGo = 0;
        await gone.SendAsync(new Completed(1), () => Interlocked.Increment(ref letGo));
        await gone.SendAsync(new Completed(2), () => Interlocked.Increment(ref letGo));
        broken.Open();
        await gone.DisposeAsync();
        Assert.AreEqual(2, letGo, "a pipe that broke lets go of what it was to write, and of what was behind it");
        Assert.IsTrue(gone.SendAsync(new Completed(3)).IsCompleted, "and a reply sent after the session ended waits on nothing");
    }
}
