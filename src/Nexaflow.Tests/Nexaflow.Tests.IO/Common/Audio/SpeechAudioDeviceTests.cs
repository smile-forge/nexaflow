using Nexaflow.IO.Common.Audio;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Audio;

/// <summary>
/// The real microphone and speakers through WASAPI — dev machines only. Playback is of silence, so nothing is heard.
/// </summary>
[TestClass]
[TestCategory("Interactive")]
[DoNotParallelize]
[CoversNode("voice-session")]
public class SpeechAudioDeviceTests
{
    [TestMethod]
    public async Task Playback_CountsWhatIsHeard_AndSaysWhenItIsDone()
    {
        using var speakers = new SpeakerPlayback();
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        speakers.Drained += (_, _) => drained.TrySetResult();

        speakers.Enqueue(new byte[ISpeechPlayback.SampleRate / 4 * 2]);   // a quarter of a second
        speakers.Enqueue(new byte[ISpeechPlayback.SampleRate / 4 * 2]);
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(ISpeechPlayback.SampleRate / 2, speakers.QueuedSamples);
        Assert.AreEqual(speakers.QueuedSamples, speakers.PlayedSamples);
    }

    [TestMethod]
    public async Task Playback_StoppedPartWay_HasHeardOnlyPartOfIt()
    {
        using var speakers = new SpeakerPlayback();
        speakers.Enqueue(new byte[ISpeechPlayback.SampleRate * 2 * 2]);   // two seconds
        await Task.Delay(300);

        speakers.Stop();
        await Task.Delay(100);

        Assert.IsTrue(speakers.PlayedSamples is > 0 and < ISpeechPlayback.SampleRate * 2, $"{speakers.PlayedSamples} samples heard");
        Assert.AreEqual(speakers.PlayedSamples, speakers.QueuedSamples, "what was not heard is dropped");
    }

    [TestMethod]
    public async Task TheMicrophone_DeliversAudio_ThroughTheCommunicationsPath()
    {
        using var speakers = new SpeakerPlayback();
        using var microphone = new CommunicationsCapture(speakers.EndpointId);
        try { await microphone.StartAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (System.Runtime.InteropServices.COMException ex) { Assert.Inconclusive($"no microphone here: {ex.Message}"); }

        long bytes = 0;
        using var enough = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await foreach (var pcm in microphone.Audio.ReadAllAsync(enough.Token)) bytes += pcm.Length; }
        catch (OperationCanceledException) { }

        Assert.IsTrue(bytes > ISpeechCapture.SampleRate, $"{bytes} bytes in a second");   // more than half a second of 16-bit audio
        Console.WriteLine($"echo cancellation reported: {microphone.CancelsEcho}");
    }
}
