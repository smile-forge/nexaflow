namespace Nexaflow.Media.Jpeg2000;

/// <summary>Data that is not JPEG 2000, or is damaged past decoding — with the decoder's reason.</summary>
public sealed class Jpeg2000Exception(string message) : Exception(message);
