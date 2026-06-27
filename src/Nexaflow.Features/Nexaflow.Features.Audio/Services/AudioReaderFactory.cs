using System;
using System.IO;
using Concentus;
using Concentus.Oggfile;
using NAudio.Vorbis;
using NAudio.Wave;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// The single seam that knows how to turn an audio file into a NAudio <see cref="WaveStream"/> the
/// player and the waveform analyser both consume. MediaFoundation covers mp3/wav/m4a/aac/wma/flac;
/// Vorbis (.ogg) and Opus (.opus) need their own decoders because MediaFoundation can't read them.
/// </summary>
public static class AudioReaderFactory
{
    /// <summary>
    /// Opens <paramref name="path"/> as a seekable <see cref="WaveStream"/>. The caller owns disposal.
    /// Throws if the format has no decoder.
    /// </summary>
    public static WaveStream CreateReader(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".wav"  => new WaveFileReader(path),     // pure-managed, no Media Foundation
            ".ogg"  => new VorbisWaveReader(path),
            ".opus" => DecodeOpus(path),
            _       => new MediaFoundationReader(path),
        };
    }

    /// <summary>
    /// Decodes a whole Opus-in-Ogg file to in-memory 48 kHz / 16-bit / stereo PCM and wraps it in a
    /// seekable <see cref="RawSourceWaveStream"/>. Decode-to-memory keeps the player and seek logic
    /// uniform (a normal WaveStream) at the cost of holding the track's PCM — fine for song-length files.
    /// </summary>
    private static WaveStream DecodeOpus(string path)
    {
        const int sampleRate = 48000, channels = 2;
        var decoder = OpusCodecFactory.CreateDecoder(sampleRate, channels);

        var pcm = new MemoryStream();
        using (var file = File.OpenRead(path))
        {
            var ogg = new OpusOggReadStream(decoder, file);
            while (ogg.HasNextPacket)
            {
                short[]? packet = ogg.DecodeNextPacket();
                if (packet is not { Length: > 0 }) continue;

                var bytes = new byte[packet.Length * 2];
                Buffer.BlockCopy(packet, 0, bytes, 0, bytes.Length);
                pcm.Write(bytes, 0, bytes.Length);
            }
        }

        pcm.Position = 0;
        return new RawSourceWaveStream(pcm, new WaveFormat(sampleRate, 16, channels));
    }
}
