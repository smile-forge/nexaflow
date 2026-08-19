using System;
using System.IO;
using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;
using Nexaflow.IO.Common;

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
        // NAudio needs a real on-disk path; materialise a file that lives inside a disk image / archive first
        // (a real path passes through unchanged). The temp copy keeps the extension, so detection is unaffected.
        var real = RealPath(path);
        return ext switch
        {
            ".wav"  => new WaveFileReader(real),     // pure-managed, no Media Foundation
            ".ogg"  => new VorbisWaveStream(real),
            ".opus" => DecodeOpus(real),
            _       => new MediaFoundationReader(real),
        };
    }

    private static string RealPath(string path)
    {
        try { return VirtualFileSystem.Instance.MaterializeFile(path); }
        catch { return path; }
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
