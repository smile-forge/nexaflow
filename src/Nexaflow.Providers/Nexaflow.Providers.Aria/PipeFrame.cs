namespace Nexaflow.Providers.Aria
{
    /// <summary>
    /// The type of a <see cref="PipeFrame"/> travelling on the wire.
    /// Values are stable integers so both sides can evolve independently.
    /// </summary>
    public enum PipeFrameType
    {
        /// <summary>Client → server: a new chat message.</summary>
        UserMessage = 1,

        /// <summary>Server → client: a text reply from Aria (may be unsolicited).</summary>
        AssistantMessage = 2,

        /// <summary>Server → client: Aria has started composing.</summary>
        TypingStarted = 3,

        /// <summary>Server → client: Aria has finished composing.</summary>
        TypingStopped = 4,
    }

    /// <summary>
    /// Envelope that wraps every message exchanged on the persistent named-pipe connection.
    /// </summary>
    public class PipeFrame
    {
        /// <summary>Discriminator — tells both sides how to interpret <see cref="Payload"/>.</summary>
        public required PipeFrameType Type { get; set; }

        /// <summary>
        /// The Windows identity of the connected user (e.g. <c>DOMAIN\Username</c>).
        /// Always set by the client on outbound frames so the server can identify the sender.
        /// Echoed back by the server on all frames sent to that client.
        /// </summary>
        public required string WindowsUserId { get; set; }

        /// <summary>
        /// The message payload. Present for <see cref="PipeFrameType.UserMessage"/> and
        /// <see cref="PipeFrameType.AssistantMessage"/>; null for typing-indicator frames.
        /// </summary>
        public SimpleMessage? Payload { get; set; }
    }
}
