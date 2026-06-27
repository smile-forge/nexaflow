using System;

namespace Nexaflow.Features.WindowsFileSystem
{
    /// <summary>
    /// A copy/move failure whose <see cref="Exception.Message"/> is already a friendly,
    /// user-facing explanation (built by <see cref="FileOperationErrors"/>). The shell shows
    /// the message verbatim instead of routing the fault through the generic crash handler.
    /// </summary>
    public sealed class FileOperationException : Exception
    {
        public FileOperationException(string message) : base(message) { }
    }
}
