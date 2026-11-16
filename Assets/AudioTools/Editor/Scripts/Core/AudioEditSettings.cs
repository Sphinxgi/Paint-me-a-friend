using System.Globalization;
using System.Text;

namespace AudioTool
{
    internal enum AudioSilenceMode
    {
        TrimEdges = 0,
        ShortenPauses = 1,
        SplitClips = 2
    }

    internal enum AudioNormalizeMode
    {
        Peak = 0,
        Loudness = 1
    }

    internal enum AudioLoopShape
    {
        EqualPower = 0,
        Linear = 1
    }

    internal enum AudioOutputChannels
    {
        Keep = 0,
        Mono = 1,
        Left = 2,
        Right = 3
    }

    internal enum AudioOutputFormat
    {
        MatchSource = 0,
        Pcm16 = 1,
        Pcm24 = 2,
        Float32 = 3
    }

    /// <summary>Snapshot of every setting that shapes rendered audio, shared by preview, single-clip export and batch saves.</summary>
    /// <remarks>Times are in seconds, levels are linear amplitudes except <see cref="LoudnessTarget"/> (LUFS), and fade curves are sampled tables.</remarks>
    internal sealed class AudioEditSettings
    {
        internal const int CurveResolution = 512;

        internal bool HasSelection;
        internal float SelectionStart;
        internal float SelectionEnd = 1f;

        internal bool TrimSilence;
        internal AudioSilenceMode SilenceMode;
        internal float SilenceThreshold = 0.01f;
        internal float PaddingBefore = 0.005f;
        internal float PaddingAfter = 0.02f;
        internal float MinimumPause = 0.3f;
        internal float KeptPause = 0.1f;

        internal bool RemoveDcOffset;
        internal bool HighPass;
        internal float HighPassFrequency = 80f;
        internal bool Reverse;

        internal bool Normalize;
        internal AudioNormalizeMode NormalizeMode;
        internal float NormalizeTarget = 0.95f;
        internal float LoudnessTarget = -23f;
        internal float PeakCeiling = 0.8912509f;

        internal bool FadeIn;
        internal float FadeInDuration = 1f;
        internal float[] FadeInCurve;
        internal bool FadeOut;
        internal float FadeOutDuration = 1f;
        internal float[] FadeOutCurve;

        internal bool AdjustVolume;
        internal float Volume = 1f;

        internal bool SeamlessLoop;
        internal float LoopCrossfade = 0.25f;
        internal AudioLoopShape LoopShape;

        internal AudioOutputChannels Channels;
        internal int SampleRate;

        /// <summary>True when rendering changes sample values, so playback needs the processed audio instead of the source.</summary>
        internal bool ChangesSamples => RemoveDcOffset || HighPass || Reverse || Normalize || FadeIn || FadeOut || AdjustVolume || SeamlessLoop
                                        || Channels != AudioOutputChannels.Keep || SampleRate > 0;

        internal bool ShortensPauses => TrimSilence && SilenceMode == AudioSilenceMode.ShortenPauses;
        internal bool SplitsClips => TrimSilence && SilenceMode == AudioSilenceMode.SplitClips;

        internal AudioEditSettings WithSelection(bool hasSelection, float selectionStart, float selectionEnd)
        {
            AudioEditSettings copy = (AudioEditSettings)MemberwiseClone();
            copy.HasSelection = hasSelection;
            copy.SelectionStart = hasSelection ? selectionStart : 0f;
            copy.SelectionEnd = hasSelection ? selectionEnd : 1f;
            return copy;
        }

        internal bool Matches(AudioEditSettings other)
        {
            return other != null && CreateKey() == other.CreateKey();
        }

        /// <summary>Stable text identity of every output-relevant value, used to detect stale previews and already-saved batch entries.</summary>
        internal string CreateKey()
        {
            // Values of disabled features are left out, so changing them neither invalidates a preview nor marks saved files as pending.
            StringBuilder key = new StringBuilder(256);
            Append(key, HasSelection);
            if (HasSelection)
            {
                Append(key, SelectionStart);
                Append(key, SelectionEnd);
            }

            Append(key, TrimSilence);
            if (TrimSilence)
            {
                Append(key, (int)SilenceMode);
                Append(key, SilenceThreshold);
                Append(key, PaddingBefore);
                Append(key, PaddingAfter);
                if (SilenceMode != AudioSilenceMode.TrimEdges) Append(key, MinimumPause);
                if (SilenceMode == AudioSilenceMode.ShortenPauses) Append(key, KeptPause);
            }

            Append(key, RemoveDcOffset);
            Append(key, HighPass);
            if (HighPass) Append(key, HighPassFrequency);
            Append(key, Reverse);
            Append(key, Normalize);
            if (Normalize)
            {
                Append(key, (int)NormalizeMode);
                if (NormalizeMode == AudioNormalizeMode.Peak)
                {
                    Append(key, NormalizeTarget);
                }
                else
                {
                    Append(key, LoudnessTarget);
                    Append(key, PeakCeiling);
                }
            }

            Append(key, FadeIn);
            if (FadeIn)
            {
                Append(key, FadeInDuration);
                AppendCurve(key, FadeInCurve);
            }

            Append(key, FadeOut);
            if (FadeOut)
            {
                Append(key, FadeOutDuration);
                AppendCurve(key, FadeOutCurve);
            }

            Append(key, AdjustVolume);
            if (AdjustVolume) Append(key, Volume);
            Append(key, SeamlessLoop);
            if (SeamlessLoop)
            {
                Append(key, LoopCrossfade);
                Append(key, (int)LoopShape);
            }

            Append(key, (int)Channels);
            Append(key, SampleRate);
            return key.ToString();
        }

        private static void Append(StringBuilder key, bool value)
        {
            key.Append(value ? '1' : '0').Append('|');
        }

        private static void Append(StringBuilder key, int value)
        {
            key.Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');
        }

        private static void Append(StringBuilder key, float value)
        {
            key.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void AppendCurve(StringBuilder key, float[] curve)
        {
            if (curve == null)
            {
                key.Append("-|");
                return;
            }

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < curve.Length; i++) hash = hash * 31 + curve[i].GetHashCode();
                key.Append(curve.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(hash.ToString("X8", CultureInfo.InvariantCulture)).Append('|');
            }
        }
    }
}
