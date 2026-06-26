namespace Nexaflow.Features.Common;

/// <summary>
/// An image produced by a client tool for the model to look at — e.g. the web view's
/// "capture this page" tool returning a screenshot. The agent harness forwards it to the next model
/// turn as a vision attachment when the conversation model can process images; otherwise it's dropped.
/// </summary>
/// <param name="Bytes">The encoded image content.</param>
/// <param name="MimeType">The image MIME type (e.g. <c>"image/png"</c>).</param>
/// <param name="Label">Optional short description (e.g. the page URL) for naming/logging.</param>
public sealed record ContextImage(byte[] Bytes, string MimeType, string? Label = null);
