using System;
using System.IO;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Reads LAME gapless metadata so editor decoders can drop encoder priming and padding.
    /// </summary>
    public static class EditorMp3GaplessInfo
    {
        private const int DecoderDelay = 529;
        private const int HeaderProbeLength = 256;

        public readonly struct Range
        {
            public readonly int InfoFrameSamples;
            public readonly int StartFrame;
            public readonly int FrameCount;

            internal Range(int infoFrameSamples, int startFrame, int frameCount)
            {
                InfoFrameSamples = infoFrameSamples;
                StartFrame = startFrame;
                FrameCount = frameCount;
            }
        }

        /// <summary>
        /// Returns the encoded audio when the decoder emits a silent Info frame followed by encoder and decoder delay.
        /// </summary>
        public static float[] TrimDecodedSamples(string filePath, float[] samples, int channels)
        {
            if (samples == null || !TryResolve(filePath, samples.LongLength / Math.Max(1, channels), samples, channels, out Range range)) return samples;
            float[] trimmed = new float[checked(range.FrameCount * channels)];
            Array.Copy(samples, checked(range.StartFrame * channels), trimmed, 0, trimmed.Length);
            return trimmed;
        }

        /// <summary>Verifies that the declared range fits the decode and that its Info frame is silent.</summary>
        public static bool TryResolve(string filePath, long decodedFrames, float[] leadingSamples, int channels, out Range range)
        {
            range = default;
            if (channels <= 0 || leadingSamples == null || !TryRead(filePath, out Range candidate)) return false;
            if ((long)candidate.StartFrame + candidate.FrameCount > decodedFrames) return false;
            long infoSamples = (long)candidate.InfoFrameSamples * channels;
            if (infoSamples > leadingSamples.LongLength) return false;
            for (long i = 0; i < infoSamples; i++)
            {
                if (leadingSamples[i] != 0f) return false;
            }
            range = candidate;
            return true;
        }

        public static bool TryRead(string filePath, out Range range)
        {
            range = default;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return false;

            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] id3 = new byte[10];
                    if (stream.Read(id3, 0, id3.Length) != id3.Length) return false;

                    long audioStart = 0;
                    if (id3[0] == 'I' && id3[1] == 'D' && id3[2] == '3')
                    {
                        audioStart = 10 + ((id3[6] & 0x7F) << 21 | (id3[7] & 0x7F) << 14 | (id3[8] & 0x7F) << 7 | (id3[9] & 0x7F));
                        if ((id3[5] & 0x10) != 0) audioStart += 10;
                    }

                    stream.Position = audioStart;
                    byte[] frame = new byte[HeaderProbeLength];
                    int length = stream.Read(frame, 0, frame.Length);
                    return TryRead(frame, length, out range);
                }
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static bool TryRead(byte[] frame, int length, out Range range)
        {
            range = default;
            if (frame == null || length < 4 || frame[0] != 0xFF || (frame[1] & 0xE0) != 0xE0) return false;

            int version = (frame[1] >> 3) & 3;
            int layer = (frame[1] >> 1) & 3;
            if (version == 1 || layer != 1) return false;

            bool mpeg1 = version == 3;
            bool mono = (frame[3] >> 6) == 3;
            int samplesPerFrame = mpeg1 ? 1152 : 576;
            int sideInfo = mpeg1 ? (mono ? 17 : 32) : (mono ? 9 : 17);
            int tag = 4 + sideInfo + ((frame[1] & 1) == 0 ? 2 : 0);
            if (!HasAscii(frame, length, tag, "Xing") && !HasAscii(frame, length, tag, "Info")) return false;
            if (tag + 12 > length) return false;

            uint flags = ReadUInt32BigEndian(frame, tag + 4);
            if ((flags & 1) == 0) return false;

            long frames = ReadUInt32BigEndian(frame, tag + 8);
            int extension = tag + 12;
            if ((flags & 2) != 0) extension += 4;
            if ((flags & 4) != 0) extension += 100;
            if ((flags & 8) != 0) extension += 4;
            if (extension + 24 > length) return false;
            if (!HasAscii(frame, length, extension, "LAME") && !HasAscii(frame, length, extension, "Lavc") && !HasAscii(frame, length, extension, "Lavf")) return false;

            int packed = frame[extension + 21] << 16 | frame[extension + 22] << 8 | frame[extension + 23];
            int encoderDelay = packed >> 12;
            int padding = packed & 0xFFF;
            long frameCount = frames * samplesPerFrame - encoderDelay - padding;
            if (frames == 0 || frameCount <= 0 || frameCount > int.MaxValue) return false;

            range = new Range(samplesPerFrame, samplesPerFrame + encoderDelay + DecoderDelay, (int)frameCount);
            return true;
        }

        private static bool HasAscii(byte[] data, int length, int offset, string value)
        {
            if (offset < 0 || offset + value.Length > length) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (data[offset + i] != value[i]) return false;
            }
            return true;
        }

        private static uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            return (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);
        }
    }
}
