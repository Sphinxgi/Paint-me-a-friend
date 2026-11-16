using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AudioTool
{
    /// <summary>Sample encodings for exported WAV files.</summary>
    public enum WavSampleFormat
    {
        /// <summary>16-bit integer PCM, rounded to the nearest step.</summary>
        Pcm16 = 0,

        /// <summary>24-bit integer PCM, rounded to the nearest step.</summary>
        Pcm24 = 1,

        /// <summary>32-bit IEEE float, written without clamping.</summary>
        Float32 = 2
    }

    internal static class AudioWavWriter
    {
        private const int ChunkSamples = 65536;

        internal static void Write(string path, float[] samples, int channels, int frequency, WavSampleFormat format)
        {
            if (samples == null || samples.Length == 0) throw new ArgumentException("No audio samples to export");
            if (channels < 1 || channels > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(channels));
            if (frequency < 1) throw new ArgumentOutOfRangeException(nameof(frequency));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            bool isFloat = format == WavSampleFormat.Float32;
            int bytesPerSample = format == WavSampleFormat.Pcm16 ? 2 : format == WavSampleFormat.Pcm24 ? 3 : 4;
            long dataSize = (long)samples.Length * bytesPerSample;
            int pad = (int)(dataSize & 1);
            int formatSize = isFloat ? 18 : 16;
            int factSize = isFloat ? 12 : 0;
            long riffSize = 4 + 8 + formatSize + factSize + 8 + dataSize + pad;
            if (riffSize > uint.MaxValue) throw new InvalidOperationException("The audio is too long for a WAV file. Export a shorter selection.");

            int blockAlign = channels * bytesPerSample;
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write((uint)riffSize);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));

                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(formatSize);
                writer.Write((short)(isFloat ? 3 : 1));
                writer.Write((short)channels);
                writer.Write(frequency);
                writer.Write(frequency * blockAlign);
                writer.Write((short)blockAlign);
                writer.Write((short)(bytesPerSample * 8));
                if (isFloat)
                {
                    writer.Write((short)0);
                    writer.Write(Encoding.ASCII.GetBytes("fact"));
                    writer.Write(4);
                    writer.Write((uint)(samples.Length / channels));
                }

                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write((uint)dataSize);
                writer.Flush();
                WriteSamples(stream, samples, format, bytesPerSample);
                if (pad != 0) stream.WriteByte(0);
            }
        }

        /// <summary>Resolves Match Source from the source header: 24-bit stays 24-bit, 32-bit becomes float, everything else 16-bit.</summary>
        internal static WavSampleFormat Resolve(AudioOutputFormat format, string sourcePath)
        {
            switch (format)
            {
                case AudioOutputFormat.Pcm24:
                    return WavSampleFormat.Pcm24;
                case AudioOutputFormat.Float32:
                    return WavSampleFormat.Float32;
                case AudioOutputFormat.Pcm16:
                    return WavSampleFormat.Pcm16;
            }

            int bits = 0;
            try
            {
                if (!string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath)) bits = AudioHeaderReader.ReadHeader(sourcePath)?.BitsPerSample ?? 0;
            }
            catch (IOException)
            {
                bits = 0;
            }
            catch (UnauthorizedAccessException)
            {
                bits = 0;
            }

            if (bits >= 32) return WavSampleFormat.Float32;
            return bits > 16 ? WavSampleFormat.Pcm24 : WavSampleFormat.Pcm16;
        }

        internal static string Describe(WavSampleFormat format)
        {
            switch (format)
            {
                case WavSampleFormat.Pcm24:
                    return "24-bit PCM";
                case WavSampleFormat.Float32:
                    return "32-bit float";
                default:
                    return "16-bit PCM";
            }
        }

        private static void WriteSamples(Stream stream, float[] samples, WavSampleFormat format, int bytesPerSample)
        {
            byte[] buffer = new byte[Math.Min(samples.Length, ChunkSamples) * bytesPerSample];
            for (int offset = 0; offset < samples.Length; offset += ChunkSamples)
            {
                int count = Math.Min(ChunkSamples, samples.Length - offset);
                int position = 0;
                for (int i = 0; i < count; i++)
                {
                    float sample = samples[offset + i];
                    if (float.IsNaN(sample)) sample = 0f;
                    switch (format)
                    {
                        case WavSampleFormat.Pcm16:
                        {
                            int value = (int)Math.Max(-32768.0, Math.Min(32767.0, Math.Round(sample * 32768.0)));
                            buffer[position++] = (byte)value;
                            buffer[position++] = (byte)(value >> 8);
                            break;
                        }
                        case WavSampleFormat.Pcm24:
                        {
                            int value = (int)Math.Max(-8388608.0, Math.Min(8388607.0, Math.Round(sample * 8388608.0)));
                            buffer[position++] = (byte)value;
                            buffer[position++] = (byte)(value >> 8);
                            buffer[position++] = (byte)(value >> 16);
                            break;
                        }
                        default:
                        {
                            FloatBits bits = default;
                            bits.Float = sample;
                            int value = bits.Int;
                            buffer[position++] = (byte)value;
                            buffer[position++] = (byte)(value >> 8);
                            buffer[position++] = (byte)(value >> 16);
                            buffer[position++] = (byte)(value >> 24);
                            break;
                        }
                    }
                }

                stream.Write(buffer, 0, position);
            }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatBits
        {
            [FieldOffset(0)] internal float Float;
            [FieldOffset(0)] internal int Int;
        }
    }
}
