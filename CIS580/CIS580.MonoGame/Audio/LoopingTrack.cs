using System;
using System.IO;
using System.Text;
using Microsoft.Xna.Framework.Audio;

namespace CIS580.Audio;

/// <summary>
/// A music track with an intro: it plays from the beginning once, then loops between a loop
/// start and a loop end forever, so the intro is never heard again.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="SoundEffect"/> can only loop the whole sound, so this keeps the track's samples
/// itself and streams them through a <see cref="DynamicSoundEffectInstance"/>, jumping from the
/// loop end back to the loop start at the exact sample. There is no gap at the jump.
/// </para>
/// <para>
/// To hide any click where the loop end meets the loop start, the last few milliseconds before
/// the loop end are cross-faded with the audio just before the loop start (which naturally leads
/// into it). Those samples are only ever heard just before a jump, so nothing else changes.
/// </para>
/// <para>
/// Load one from a 16-bit PCM .wav file with <see cref="FromWav"/>, and play it with
/// <see cref="MusicPlayer.Play(LoopingTrack, TimeSpan?)"/>.
/// </para>
/// </remarks>
public sealed class LoopingTrack
{
    // How much audio is queued ahead of what is playing. Enough to ride out a slow frame.
    private const int BufferMilliseconds = 100;
    private const int BuffersAhead = 3;

    // How long the cross-fade into the loop start is
    private const int SeamMilliseconds = 10;

    private readonly byte[] _pcm;        // 16-bit little-endian samples, channels interleaved
    private readonly int _frameBytes;    // bytes per sample frame (all channels)

    /// <param name="pcm">The track's samples: 16-bit little-endian, channels interleaved</param>
    /// <param name="sampleRate">Samples per second (8000 to 48000)</param>
    /// <param name="channels">Mono or stereo</param>
    /// <param name="loopStart">Where the loop starts (the end of the intro)</param>
    /// <param name="loopEnd">Where the loop ends and jumps back to the loop start (null: the end of the track)</param>
    public LoopingTrack(byte[] pcm, int sampleRate, AudioChannels channels, TimeSpan loopStart, TimeSpan? loopEnd = null)
    {
        _pcm = pcm ?? throw new ArgumentNullException(nameof(pcm));
        SampleRate = sampleRate;
        Channels = channels;
        _frameBytes = 2 * (int)channels;

        int frames = _pcm.Length / _frameBytes;
        LoopStartFrame = Math.Clamp((int)Math.Round(loopStart.TotalSeconds * sampleRate), 0, frames - 1);
        LoopEndFrame = loopEnd == null ? frames
            : Math.Clamp((int)Math.Round(loopEnd.Value.TotalSeconds * sampleRate), LoopStartFrame + 1, frames);

        SmoothSeam();
    }

    /// <summary>Samples per second</summary>
    public int SampleRate { get; }

    /// <summary>Mono or stereo</summary>
    public AudioChannels Channels { get; }

    /// <summary>The sample frame the loop starts at</summary>
    public int LoopStartFrame { get; }

    /// <summary>The sample frame the loop jumps back from (exclusive)</summary>
    public int LoopEndFrame { get; }

    /// <summary>The intro's length</summary>
    public TimeSpan IntroLength => TimeSpan.FromSeconds(LoopStartFrame / (double)SampleRate);

    /// <summary>The loop's length</summary>
    public TimeSpan LoopLength => TimeSpan.FromSeconds((LoopEndFrame - LoopStartFrame) / (double)SampleRate);

    /// <summary>
    /// Loads a track from a 16-bit PCM .wav file
    /// </summary>
    /// <param name="stream">The .wav file's contents (e.g. from TitleContainer.OpenStream)</param>
    /// <param name="loopStart">Where the loop starts (the end of the intro)</param>
    /// <param name="loopEnd">Where the loop ends (null: the end of the file)</param>
    public static LoopingTrack FromWav(Stream stream, TimeSpan loopStart, TimeSpan? loopEnd = null)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        if (ReadTag(reader) != "RIFF")
            throw new InvalidDataException("Not a .wav file (no RIFF header)");
        reader.ReadInt32();
        if (ReadTag(reader) != "WAVE")
            throw new InvalidDataException("Not a .wav file (no WAVE header)");

        int channels = 0, sampleRate = 0, bits = 0;
        byte[] data = null;

        // A .wav file is a list of chunks; we need "fmt " (the format) and "data" (the samples)
        while (stream.Position < stream.Length - 8 && data == null)
        {
            string tag = ReadTag(reader);
            int size = reader.ReadInt32();
            if (tag == "fmt ")
            {
                int format = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();    // bytes per second
                reader.ReadInt16();    // block align
                bits = reader.ReadInt16();
                reader.ReadBytes(size - 16);
                if (format != 1 && format != unchecked((short)0xFFFE))
                    throw new InvalidDataException("Only uncompressed (PCM) .wav files are supported");
            }
            else if (tag == "data")
                data = reader.ReadBytes(size);
            else
                reader.ReadBytes(size);

            if (size % 2 == 1 && stream.Position < stream.Length)
                reader.ReadByte();     // Chunks are padded to an even size
        }

        if (data == null || channels == 0)
            throw new InvalidDataException("The .wav file has no format or no samples");
        if (bits != 16 || channels > 2)
            throw new InvalidDataException($"Only 16-bit mono or stereo .wav files are supported (this is {bits}-bit, {channels} channels)");

        return new LoopingTrack(data, sampleRate, (AudioChannels)channels, loopStart, loopEnd);
    }

    /// <summary>
    /// Creates a playing-ready instance that streams the track: the intro once, then the loop
    /// forever. Don't set its IsLooped (it loops by itself).
    /// </summary>
    public SoundEffectInstance CreateInstance()
    {
        var instance = new DynamicSoundEffectInstance(SampleRate, Channels);
        var stream = new Streamer(this, instance);
        instance.BufferNeeded += (_, _) => stream.Fill();
        stream.Fill();
        return instance;
    }

    // Feeds one instance its samples, wrapping from the loop end to the loop start
    private sealed class Streamer
    {
        private readonly LoopingTrack _track;
        private readonly DynamicSoundEffectInstance _instance;
        private readonly int _bufferBytes;
        private int _frame;

        public Streamer(LoopingTrack track, DynamicSoundEffectInstance instance)
        {
            _track = track;
            _instance = instance;
            _bufferBytes = track.SampleRate * BufferMilliseconds / 1000 * track._frameBytes;
        }

        public void Fill()
        {
            if (_instance.IsDisposed)
                return;

            while (_instance.PendingBufferCount < BuffersAhead)
            {
                // A new array for each buffer, so a buffer is never changed while it is queued
                var buffer = new byte[_bufferBytes];
                int written = 0;
                while (written < buffer.Length)
                {
                    int framesLeft = _track.LoopEndFrame - _frame;
                    int bytes = Math.Min(buffer.Length - written, framesLeft * _track._frameBytes);
                    Buffer.BlockCopy(_track._pcm, _frame * _track._frameBytes, buffer, written, bytes);
                    written += bytes;
                    _frame += bytes / _track._frameBytes;

                    if (_frame >= _track.LoopEndFrame)
                        _frame = _track.LoopStartFrame;    // The jump: no gap, mid-buffer
                }
                _instance.SubmitBuffer(buffer);
            }
        }
    }

    /// <summary>
    /// Cross-fades the end of the loop into the audio just before the loop start, so the jump
    /// back doesn't click
    /// </summary>
    private void SmoothSeam()
    {
        int seam = Math.Min(SampleRate * SeamMilliseconds / 1000, LoopStartFrame);
        seam = Math.Min(seam, (LoopEndFrame - LoopStartFrame) / 2);
        if (seam <= 0)
            return;

        int channels = (int)Channels;
        for (int i = 0; i < seam; i++)
        {
            float t = (i + 1) / (float)(seam + 1);     // 0 -> 1 across the seam
            int end = LoopEndFrame - seam + i;          // Leading up to the loop end...
            int start = LoopStartFrame - seam + i;      // ...becomes what leads up to the loop start
            for (int c = 0; c < channels; c++)
            {
                int a = (end * channels + c) * 2, b = (start * channels + c) * 2;
                float mixed = Sample(a) * (1 - t) + Sample(b) * t;
                short value = (short)Math.Clamp(MathF.Round(mixed), short.MinValue, short.MaxValue);
                _pcm[a] = (byte)value;
                _pcm[a + 1] = (byte)(value >> 8);
            }
        }
    }

    private short Sample(int byteIndex) => (short)(_pcm[byteIndex] | (_pcm[byteIndex + 1] << 8));

    private static string ReadTag(BinaryReader reader) => Encoding.ASCII.GetString(reader.ReadBytes(4));
}
