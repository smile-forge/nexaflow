namespace Nexaflow.IO.Common.Audio;

/// <summary>
/// The microphone and speakers a spoken conversation is held through, as the platform the app runs on opens them: the microphone
/// cancels the echo of what the speakers play, so the assistant does not hear itself.
/// </summary>
public interface ISpeechDevices
{
    /// <summary>Opens a microphone and the speakers it cancels the echo of. Both are the caller's to dispose.</summary>
    (ISpeechCapture Capture, ISpeechPlayback Playback) Open();
}

/// <summary>Windows' devices, through WASAPI: the default speakers, and the communications microphone cancelling their echo.</summary>
public sealed class WasapiSpeechDevices : ISpeechDevices
{
    public (ISpeechCapture Capture, ISpeechPlayback Playback) Open()
    {
        var playback = new SpeakerPlayback();
        try { return (new CommunicationsCapture(playback.EndpointId), playback); }
        catch
        {
            playback.Dispose();
            throw;
        }
    }
}
