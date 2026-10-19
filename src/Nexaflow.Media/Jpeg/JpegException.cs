namespace Nexaflow.Media.Jpeg;

/// <summary>Data that is not JPEG, or is damaged past decoding, or that libjpeg-turbo's 8-bit decoder refuses — with libjpeg's reason.</summary>
public sealed class JpegException(string message) : Exception(message);
