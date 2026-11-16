using System;
using System.Collections.Generic;

namespace AudioTool
{
    /// <summary>Half-open range of per-channel sample frames.</summary>
    internal readonly struct AudioFrameRange
    {
        internal AudioFrameRange(int start, int end)
        {
            Start = start;
            End = end;
        }

        internal int Start { get; }
        internal int End { get; }
        internal int Length => End - Start;
    }

    /// <summary>Maps a stretch of rendered output frames back to source frames at the source sample rate.</summary>
    internal readonly struct AudioPlaybackSpan
    {
        internal AudioPlaybackSpan(int outputStart, int sourceStart, int length)
        {
            OutputStart = outputStart;
            SourceStart = sourceStart;
            Length = length;
        }

        internal int OutputStart { get; }
        internal int SourceStart { get; }
        internal int Length { get; }
    }

    /// <summary>Finds audible content and pauses with the same amplitude threshold as Select Audible Content: a frame is audible when any channel exceeds it.</summary>
    internal static class AudioSilenceDetector
    {
        internal static bool FindAudibleRange(float[] samples, int channels, int startFrame, int endFrame, float threshold, out AudioFrameRange range)
        {
            range = default;
            int first = -1;
            for (int frame = startFrame; frame < endFrame; frame++)
            {
                if (!IsAudible(samples, channels, frame, threshold)) continue;
                first = frame;
                break;
            }

            if (first < 0) return false;
            for (int frame = endFrame - 1; frame >= first; frame--)
            {
                if (!IsAudible(samples, channels, frame, threshold)) continue;
                range = new AudioFrameRange(first, frame + 1);
                return true;
            }

            return false;
        }

        /// <summary>Splits audible content into regions separated by pauses of at least <paramref name="minimumPauseFrames"/>.</summary>
        internal static List<AudioFrameRange> FindSoundRegions(float[] samples, int channels, int startFrame, int endFrame, float threshold, int minimumPauseFrames)
        {
            List<AudioFrameRange> regions = new List<AudioFrameRange>();
            int regionStart = -1;
            int lastAudible = -1;
            for (int frame = startFrame; frame < endFrame; frame++)
            {
                if (!IsAudible(samples, channels, frame, threshold)) continue;
                if (regionStart < 0) regionStart = frame;
                else if (frame - lastAudible - 1 >= minimumPauseFrames)
                {
                    regions.Add(new AudioFrameRange(regionStart, lastAudible + 1));
                    regionStart = frame;
                }

                lastAudible = frame;
            }

            if (regionStart >= 0) regions.Add(new AudioFrameRange(regionStart, lastAudible + 1));
            return regions;
        }

        /// <summary>Pauses between consecutive sound regions.</summary>
        internal static List<AudioFrameRange> GetPauses(IReadOnlyList<AudioFrameRange> regions)
        {
            List<AudioFrameRange> pauses = new List<AudioFrameRange>();
            for (int i = 1; i < regions.Count; i++) pauses.Add(new AudioFrameRange(regions[i - 1].End, regions[i].Start));
            return pauses;
        }

        /// <summary>Expands regions by padding without letting neighbors overlap or leave the outer range.</summary>
        internal static List<AudioFrameRange> Pad(IReadOnlyList<AudioFrameRange> regions, int paddingBefore, int paddingAfter, int outerStart, int outerEnd)
        {
            List<AudioFrameRange> padded = new List<AudioFrameRange>(regions.Count);
            for (int i = 0; i < regions.Count; i++)
            {
                int lower = i == 0 ? outerStart : (regions[i - 1].End + regions[i].Start) / 2;
                int upper = i == regions.Count - 1 ? outerEnd : (regions[i].End + regions[i + 1].Start) / 2;
                int start = Math.Max(lower, regions[i].Start - Math.Max(0, paddingBefore));
                int end = Math.Min(upper, regions[i].End + Math.Max(0, paddingAfter));
                padded.Add(new AudioFrameRange(start, Math.Max(start + 1, end)));
            }

            return padded;
        }

        /// <summary>Shortens each pause to about <paramref name="keptFrames"/>, joining the kept halves with a short crossfade.</summary>
        /// <param name="samples">Interleaved samples of one clip.</param>
        /// <param name="channels">Channels per frame.</param>
        /// <param name="pauses">Sorted pauses in clip-local frames.</param>
        /// <param name="keptFrames">Pause length to keep.</param>
        /// <param name="crossfadeFrames">Maximum crossfade at each join.</param>
        /// <param name="sourceOffset">Source frame of the clip's first frame, used for <paramref name="map"/>.</param>
        /// <param name="map">Receives the output-to-source mapping of the kept pieces.</param>
        internal static float[] ShortenPauses(float[] samples, int channels, IReadOnlyList<AudioFrameRange> pauses, int keptFrames, int crossfadeFrames, int sourceOffset, List<AudioPlaybackSpan> map)
        {
            int frames = samples.Length / channels;
            List<AudioFrameRange> pieces = new List<AudioFrameRange>();
            List<int> joins = new List<int>();
            int pieceStart = 0;
            int kept = Math.Max(0, keptFrames);
            for (int i = 0; i < pauses.Count; i++)
            {
                AudioFrameRange pause = pauses[i];
                int crossfade = Math.Max(0, Math.Min(crossfadeFrames, kept / 2));
                int half = (kept + crossfade + 1) / 2;
                if (pause.Length <= kept + crossfade || pause.Start < pieceStart || pause.End > frames) continue;
                pieces.Add(new AudioFrameRange(pieceStart, pause.Start + half));
                joins.Add(crossfade);
                pieceStart = pause.End - half;
            }

            pieces.Add(new AudioFrameRange(pieceStart, frames));
            map?.Clear();
            if (pieces.Count == 1)
            {
                map?.Add(new AudioPlaybackSpan(0, sourceOffset, frames));
                float[] copy = new float[samples.Length];
                Array.Copy(samples, copy, samples.Length);
                return copy;
            }

            int outputFrames = 0;
            for (int i = 0; i < pieces.Count; i++) outputFrames += pieces[i].Length - (i > 0 ? joins[i - 1] : 0);
            float[] output = new float[outputFrames * channels];
            int written = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                AudioFrameRange piece = pieces[i];
                int overlap = i > 0 ? Math.Min(joins[i - 1], Math.Min(written, piece.Length)) : 0;
                int outputStart = written - overlap;
                for (int frame = 0; frame < overlap; frame++)
                {
                    float t = (frame + 1f) / (overlap + 1f);
                    int outputIndex = (outputStart + frame) * channels;
                    int inputIndex = (piece.Start + frame) * channels;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        output[outputIndex + channel] = output[outputIndex + channel] * (1f - t) + samples[inputIndex + channel] * t;
                    }
                }

                int remaining = piece.Length - overlap;
                Array.Copy(samples, (piece.Start + overlap) * channels, output, written * channels, remaining * channels);
                map?.Add(new AudioPlaybackSpan(outputStart, sourceOffset + piece.Start, piece.Length));
                written += remaining;
            }

            return output;
        }

        /// <summary>Maps an output frame of a shortened clip back to its source frame.</summary>
        internal static int MapToSource(IReadOnlyList<AudioPlaybackSpan> map, int outputFrame)
        {
            if (map == null || map.Count == 0) return outputFrame;
            for (int i = map.Count - 1; i >= 0; i--)
            {
                if (outputFrame < map[i].OutputStart) continue;
                return map[i].SourceStart + Math.Min(outputFrame - map[i].OutputStart, map[i].Length);
            }

            return map[0].SourceStart;
        }

        private static bool IsAudible(float[] samples, int channels, int frame, float threshold)
        {
            int index = frame * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                if (Math.Abs(samples[index + channel]) > threshold) return true;
            }

            return false;
        }
    }
}
