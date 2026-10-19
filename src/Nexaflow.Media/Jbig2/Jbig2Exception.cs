namespace Nexaflow.Media.Jbig2;

/// <summary>Data that is not JBIG2, holds no page, or would take more memory to decode than it may use — with the reason.</summary>
public sealed class Jbig2Exception(string message) : Exception(message);
