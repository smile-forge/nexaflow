namespace Nexaflow.Providers.Aria
{
    /// <summary>
    /// A simple chat message sent to or received from Aria via the named-pipe API.
    /// Attachments are referenced by their path on disk rather than by content.
    /// </summary>
    public class SimpleMessage
    {
        /// <summary>The text body of the message. May be null when only attachments are sent.</summary>
        public string? Text { get; set; }

        /// <summary>
        /// Absolute paths to files on disk that should be delivered alongside the text.
        /// The server will load the files and route them through the appropriate processors.
        /// </summary>
        public List<string> AttachmentPaths { get; set; } = [];
    }
}
