using System.Buffers.Binary;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Audio;

/// <summary>Recordings as speech models read them: 16 kHz, mono, 16-bit.</summary>
[TestClass]
[CoversNode("ai-host")]
public class WavAudioTests
{
    /// <summary>A WAV of <paramref name="frames"/> frames, each channel's sample written by <paramref name="sample"/>.</summary>
    private static byte[] Wav(int rate, int channels, int bits, int format, int frames, Action<Span<byte>, int, int> sample)
    {
        int bytes = bits / 8, data = frames * channels * bytes;
        var wav = new byte[44 + data];
        "RIFF"u8.CopyTo(wav); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + data); "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), (short)format);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), (short)channels); BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), rate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), rate * channels * bytes); BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), (short)(channels * bytes));
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), (short)bits); "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), data);
        for (int f = 0; f < frames; f++)
            for (int c = 0; c < channels; c++)
                sample(wav.AsSpan(44 + (f * channels + c) * bytes, bytes), f, c);
        return wav;
    }

    private static short[] Samples(byte[] wav) => [.. Enumerable.Range(0, (wav.Length - 44) / 2).Select(i => BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + i * 2)))];

    [TestMethod]
    public void AStereoRecording_IsDownmixed_ToTheAverageOfItsChannels()
    {
        var stereo = Wav(16_000, 2, 16, 1, 4, (s, f, c) => BinaryPrimitives.WriteInt16LittleEndian(s, (short)(c == 0 ? 1000 : 3000)));

        CollectionAssert.AreEqual(new short[] { 2000, 2000, 2000, 2000 }, Samples(WavAudio.ToMono(stereo)));
    }

    [TestMethod]
    public void ALowerRate_IsBroughtUpToSixteenKilohertz()
    {
        var narrow = Wav(8_000, 1, 16, 1, 800, (s, f, c) => BinaryPrimitives.WriteInt16LittleEndian(s, (short)(f * 10)));

        var wide = WavAudio.ToMono(narrow);

        Assert.AreEqual(16_000, BinaryPrimitives.ReadInt32LittleEndian(wide.AsSpan(24)));
        Assert.AreEqual(1598, Samples(wide).Length, "twice the samples, up to the last");
        Assert.AreEqual(5, Samples(wide)[1], "halfway between the first two");
    }

    [TestMethod]
    public void FloatAndEightBitRecordings_ReadAsSixteenBit()
    {
        var floats = Wav(16_000, 1, 32, 3, 2, (s, f, c) => BinaryPrimitives.WriteSingleLittleEndian(s, f == 0 ? 0.5f : -0.5f));
        var bytes  = Wav(16_000, 1, 8, 1, 2, (s, f, c) => s[0] = (byte)(f == 0 ? 192 : 64));

        CollectionAssert.AreEqual(new short[] { 16384, -16384 }, Samples(WavAudio.ToMono(floats)));
        CollectionAssert.AreEqual(new short[] { 16384, -16384 }, Samples(WavAudio.ToMono(bytes)));
    }

    [TestMethod]
    public void WhatIsNotAWav_IsRefused()
        => Assert.ThrowsExactly<InvalidDataException>(() => WavAudio.ToMono("ID3\u0004 an mp3"u8));
}
