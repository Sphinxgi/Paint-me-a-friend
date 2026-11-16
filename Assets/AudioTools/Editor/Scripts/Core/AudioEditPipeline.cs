using System;
using System.Collections.Generic;
using System.Threading;
using ImpossibleRobert.Common;
using UnityEngine;

namespace AudioTool
{
    /// <summary>One exported clip within the source timeline, with what processing did to it.</summary>
    internal sealed class AudioRenderSegment
    {
        internal int Start;
        internal int End;
        /// <summary>Frames at the end folded into the start by Seamless Loop; playback and export stop this many frames before <see cref="End"/>.</summary>
        internal int LoopTailFrames;
        /// <summary>Processed export frames [Start, ExportEnd) before sample-rate conversion, in source channels.</summary>
        internal float[] Processed;
        /// <summary>Pauses that export shortens, in source frames.</summary>
        internal readonly List<AudioFrameRange> Pauses = new List<AudioFrameRange>();
        internal float Gain = 1f;
        internal AudioLoudness LoudnessBefore = AudioLoudness.Silent;
        internal AudioLoudness Loudness = AudioLoudness.Silent;
        internal bool LimitedByCeiling;
        internal float Peak;
        internal int ClippedSampleCount;

        internal int ExportEnd => End - LoopTailFrames;
        internal int ExportFrames => ExportEnd - Start;
    }

    internal sealed class AudioRenderResult
    {
        /// <summary>Full-length preview in source channels and rate: processed segments in place, untouched audio elsewhere.</summary>
        internal float[] Samples;
        internal int Channels;
        internal int Frequency;
        internal readonly List<AudioRenderSegment> Segments = new List<AudioRenderSegment>();
        /// <summary>Source frames before trimming, used to report removed silence.</summary>
        internal int RegionStart;
        internal int RegionEnd;
        internal bool NoAudibleContent;
        internal bool PhaseCancellation;
        internal bool LoopShortened;
        internal bool FadesSkipped;
        internal float Peak;
        internal int ClippedSampleCount;

        internal int Frames => Samples.Length / Channels;
        internal int ExportStart => Segments.Count > 0 ? Segments[0].Start : 0;
        internal int ExportEnd => Segments.Count > 0 ? Segments[Segments.Count - 1].ExportEnd : 0;
        internal bool AnyLimitedByCeiling => Segments.Exists(segment => segment.LimitedByCeiling);
    }

    /// <summary>A rendered file ready to write.</summary>
    internal sealed class AudioOutputClip
    {
        internal float[] Samples;
        internal int Channels;
        internal int Frequency;
        internal int SourceStart;
        internal int Frames => Samples.Length / Channels;
        internal double Duration => Frequency > 0 ? (double)Frames / Frequency : 0d;
    }

    /// <summary>Renders edit settings into preview audio and output clips. Pure math on decoded samples, safe off the main thread.</summary>
    /// <remarks>
    /// Order: clean up (whole clip), resolve the exported spans (selection, then silence), then per span: channel mix, reverse,
    /// normalize, fades, volume and seamless loop. Pause shortening, channel reduction and sample-rate conversion are output steps,
    /// so the preview keeps the source timeline while still sounding like the export.
    /// </remarks>
    internal static class AudioEditPipeline
    {
        internal const float PhaseCancellationCorrelation = -0.2f;
        private const float JoinCrossfadeSeconds = 0.005f;

        internal static AudioRenderResult Render(float[] source, int channels, int frequency, AudioEditSettings settings, CancellationToken token = default)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));

            float[] buffer = new float[source.Length];
            Array.Copy(source, buffer, source.Length);
            int totalFrames = source.Length / channels;
            if (settings.RemoveDcOffset) AudioDsp.RemoveDcOffset(buffer, channels);
            if (settings.HighPass) AudioDsp.HighPass(buffer, channels, frequency, settings.HighPassFrequency);
            token.ThrowIfCancellationRequested();

            int regionStart = settings.HasSelection ? ClampToInt((int)Math.Round(settings.SelectionStart * totalFrames), 0, totalFrames) : 0;
            int regionEnd = settings.HasSelection ? ClampToInt((int)Math.Round(settings.SelectionEnd * totalFrames), regionStart, totalFrames) : totalFrames;
            AudioRenderResult result = new AudioRenderResult
            {
                Samples = buffer,
                Channels = channels,
                Frequency = frequency,
                RegionStart = regionStart,
                RegionEnd = regionEnd
            };

            List<AudioFrameRange> spans = ResolveSpans(buffer, channels, frequency, regionStart, regionEnd, settings, out bool noAudibleContent);
            result.NoAudibleContent = noAudibleContent;
            float peak = 0f;
            int clipped = 0;
            foreach (AudioFrameRange span in spans)
            {
                token.ThrowIfCancellationRequested();
                AudioRenderSegment segment = RenderSegment(buffer, channels, frequency, span, settings, result, token);
                result.Segments.Add(segment);
                peak = Math.Max(peak, segment.Peak);
                clipped += segment.ClippedSampleCount;
            }

            result.Peak = peak;
            result.ClippedSampleCount = clipped;
            return result;
        }

        /// <summary>Builds the files to write: pause shortening, channel reduction and sample-rate conversion on each processed segment.</summary>
        internal static List<AudioOutputClip> RenderOutputs(AudioRenderResult render, AudioEditSettings settings, CancellationToken token = default)
        {
            List<AudioOutputClip> outputs = new List<AudioOutputClip>(render.Segments.Count);
            foreach (AudioRenderSegment segment in render.Segments)
            {
                token.ThrowIfCancellationRequested();
                if (segment.ExportFrames <= 0) continue;
                float[] samples = segment.Processed;
                int channels = render.Channels;
                if (segment.Pauses.Count > 0)
                {
                    samples = AudioSilenceDetector.ShortenPauses(samples, channels, ToLocal(segment.Pauses, segment.Start),
                        SecondsToFrames(settings.KeptPause, render.Frequency), SecondsToFrames(JoinCrossfadeSeconds, render.Frequency), segment.Start, null);
                }

                if (settings.Channels != AudioOutputChannels.Keep && channels > 1)
                {
                    samples = AudioDsp.ExtractChannel(samples, channels, 0);
                    channels = 1;
                }

                int frequency = render.Frequency;
                if (settings.SampleRate > 0 && settings.SampleRate != frequency)
                {
                    int inputFrames = samples.Length / channels;
                    int outputFrames = Math.Max(1, (int)Math.Round((double)inputFrames * settings.SampleRate / frequency));
                    samples = EditorAudioResampler.ResampleInterleaved(samples, channels, inputFrames, outputFrames, settings.SeamlessLoop, token);
                    frequency = settings.SampleRate;
                }

                outputs.Add(new AudioOutputClip { Samples = samples, Channels = channels, Frequency = frequency, SourceStart = segment.Start });
            }

            return outputs;
        }

        /// <summary>Preview of a segment with its pauses shortened, at the source rate and channel layout.</summary>
        internal static float[] RenderShortenedPreview(AudioRenderResult render, AudioEditSettings settings, int segmentIndex, List<AudioPlaybackSpan> map)
        {
            AudioRenderSegment segment = render.Segments[segmentIndex];
            float[] samples = new float[segment.ExportFrames * render.Channels];
            Array.Copy(render.Samples, segment.Start * render.Channels, samples, 0, samples.Length);
            return AudioSilenceDetector.ShortenPauses(samples, render.Channels, ToLocal(segment.Pauses, segment.Start),
                SecondsToFrames(settings.KeptPause, render.Frequency), SecondsToFrames(JoinCrossfadeSeconds, render.Frequency), segment.Start, map);
        }

        private static List<AudioFrameRange> ResolveSpans(float[] buffer, int channels, int frequency, int regionStart, int regionEnd, AudioEditSettings settings, out bool noAudibleContent)
        {
            noAudibleContent = false;
            List<AudioFrameRange> spans = new List<AudioFrameRange>();
            AudioFrameRange region = new AudioFrameRange(regionStart, regionEnd);
            if (!settings.TrimSilence || regionEnd <= regionStart)
            {
                spans.Add(region);
                return spans;
            }

            int paddingBefore = SecondsToFrames(settings.PaddingBefore, frequency);
            int paddingAfter = SecondsToFrames(settings.PaddingAfter, frequency);
            if (settings.SilenceMode == AudioSilenceMode.SplitClips)
            {
                int minimumPause = Math.Max(1, SecondsToFrames(settings.MinimumPause, frequency));
                List<AudioFrameRange> regions = AudioSilenceDetector.FindSoundRegions(buffer, channels, regionStart, regionEnd, settings.SilenceThreshold, minimumPause);
                if (regions.Count == 0)
                {
                    noAudibleContent = true;
                    spans.Add(region);
                    return spans;
                }

                spans.AddRange(AudioSilenceDetector.Pad(regions, paddingBefore, paddingAfter, regionStart, regionEnd));
                return spans;
            }

            if (!AudioSilenceDetector.FindAudibleRange(buffer, channels, regionStart, regionEnd, settings.SilenceThreshold, out AudioFrameRange audible))
            {
                noAudibleContent = true;
                spans.Add(region);
                return spans;
            }

            spans.Add(new AudioFrameRange(Math.Max(regionStart, audible.Start - paddingBefore), Math.Min(regionEnd, audible.End + paddingAfter)));
            return spans;
        }

        private static AudioRenderSegment RenderSegment(float[] buffer, int channels, int frequency, AudioFrameRange span, AudioEditSettings settings, AudioRenderResult result, CancellationToken token)
        {
            AudioRenderSegment segment = new AudioRenderSegment { Start = span.Start, End = span.End };
            int length = span.Length;
            float[] working = new float[length * channels];
            Array.Copy(buffer, span.Start * channels, working, 0, working.Length);

            List<AudioFrameRange> localPauses = null;
            if (settings.ShortensPauses && length > 0)
            {
                // Pauses are found on the cleaned source, so the threshold means the same as for trimming the edges.
                int minimumPause = Math.Max(1, SecondsToFrames(settings.MinimumPause, frequency));
                localPauses = AudioSilenceDetector.GetPauses(AudioSilenceDetector.FindSoundRegions(working, channels, 0, length, settings.SilenceThreshold, minimumPause));
            }

            bool mixesChannels = settings.Channels != AudioOutputChannels.Keep && channels > 1;
            if (mixesChannels)
            {
                if (settings.Channels == AudioOutputChannels.Mono && AudioDsp.ChannelCorrelation(working, channels) < PhaseCancellationCorrelation) result.PhaseCancellation = true;
                AudioDsp.ConvertChannels(working, channels, settings.Channels);
            }

            if (settings.Reverse)
            {
                AudioDsp.Reverse(working, channels);
                if (localPauses != null) localPauses = Mirror(localPauses, length);
            }

            if (settings.Normalize && working.Length > 0)
            {
                if (settings.NormalizeMode == AudioNormalizeMode.Loudness) NormalizeLoudness(working, channels, frequency, settings, segment, mixesChannels);
                else segment.Gain = AudioDsp.NormalizePeak(working, settings.NormalizeTarget);
            }

            if (settings.SeamlessLoop)
            {
                if (settings.FadeIn || settings.FadeOut) result.FadesSkipped = true;
            }
            else
            {
                if (settings.FadeIn) AudioDsp.ApplyFade(working, channels, Mathf.RoundToInt(settings.FadeInDuration * frequency), true, settings.FadeInCurve);
                if (settings.FadeOut) AudioDsp.ApplyFade(working, channels, Mathf.RoundToInt(settings.FadeOutDuration * frequency), false, settings.FadeOutCurve);
            }

            if (settings.AdjustVolume) AudioDsp.ApplyVolume(working, settings.Volume);

            if (settings.SeamlessLoop)
            {
                int requested = SecondsToFrames(settings.LoopCrossfade, frequency);
                segment.LoopTailFrames = AudioDsp.ApplyLoopCrossfade(working, channels, requested, settings.LoopShape);
                if (segment.LoopTailFrames < requested) result.LoopShortened = true;
            }

            int exportFrames = length - segment.LoopTailFrames;
            segment.Processed = new float[exportFrames * channels];
            Array.Copy(working, segment.Processed, segment.Processed.Length);
            if (localPauses != null)
            {
                foreach (AudioFrameRange pause in localPauses)
                {
                    // The start now carries the blended loop tail, so shortening must not cut into it.
                    int start = Math.Max(pause.Start, segment.LoopTailFrames);
                    int end = Math.Min(pause.End, exportFrames);
                    if (end > start) segment.Pauses.Add(new AudioFrameRange(span.Start + start, span.Start + end));
                }
            }

            if (settings.SampleRate > 0 && settings.SampleRate != frequency && exportFrames > 1)
            {
                // Down and back up again: the preview keeps the source timeline but loses what the lower rate cannot carry.
                int converted = Math.Max(1, (int)Math.Round((double)exportFrames * settings.SampleRate / frequency));
                float[] down = EditorAudioResampler.ResampleInterleaved(segment.Processed, channels, exportFrames, converted, settings.SeamlessLoop, token);
                float[] up = EditorAudioResampler.ResampleInterleaved(down, channels, converted, exportFrames, settings.SeamlessLoop, token);
                Array.Copy(up, 0, working, 0, up.Length);
            }

            Array.Copy(working, 0, buffer, span.Start * channels, working.Length);
            segment.Peak = AudioDsp.GetPeak(segment.Processed, out int clipped);
            segment.ClippedSampleCount = clipped;
            segment.Loudness = AudioLoudnessMeter.Measure(segment.Processed, channels, frequency, 0, exportFrames, mixesChannels);
            return segment;
        }

        private static void NormalizeLoudness(float[] working, int channels, int frequency, AudioEditSettings settings, AudioRenderSegment segment, bool firstChannelOnly)
        {
            int frames = working.Length / channels;
            segment.LoudnessBefore = AudioLoudnessMeter.Measure(working, channels, frequency, 0, frames, firstChannelOnly);
            if (segment.LoudnessBefore.IsSilent) return;
            float gain = (float)Math.Pow(10.0, (settings.LoudnessTarget - segment.LoudnessBefore.Integrated) / 20.0);
            float peak = AudioDsp.GetPeak(working);
            if (peak > 0f && peak * gain > settings.PeakCeiling)
            {
                gain = settings.PeakCeiling / peak;
                segment.LimitedByCeiling = true;
            }

            AudioDsp.ApplyGain(working, gain);
            segment.Gain = gain;
        }

        private static List<AudioFrameRange> Mirror(List<AudioFrameRange> ranges, int length)
        {
            List<AudioFrameRange> mirrored = new List<AudioFrameRange>(ranges.Count);
            for (int i = ranges.Count - 1; i >= 0; i--) mirrored.Add(new AudioFrameRange(length - ranges[i].End, length - ranges[i].Start));
            return mirrored;
        }

        private static List<AudioFrameRange> ToLocal(List<AudioFrameRange> ranges, int offset)
        {
            List<AudioFrameRange> local = new List<AudioFrameRange>(ranges.Count);
            foreach (AudioFrameRange range in ranges) local.Add(new AudioFrameRange(range.Start - offset, range.End - offset));
            return local;
        }

        internal static int SecondsToFrames(float seconds, int frequency)
        {
            return Math.Max(0, Mathf.RoundToInt(seconds * frequency));
        }

        private static int ClampToInt(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
