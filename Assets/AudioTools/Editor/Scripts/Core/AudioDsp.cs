using System;

namespace AudioTool
{
    /// <summary>In-place sample operations on interleaved buffers. Pure math, safe off the main thread.</summary>
    internal static class AudioDsp
    {
        internal const float ClippingLevel = 0.999f;

        // Fourth-order Butterworth as two cascaded second-order sections.
        private const double ButterworthQ1 = 0.54119610014619701;
        private const double ButterworthQ2 = 1.3065629648763766;

        internal static void RemoveDcOffset(float[] samples, int channels)
        {
            int frames = samples.Length / channels;
            if (frames == 0) return;
            for (int channel = 0; channel < channels; channel++)
            {
                double sum = 0;
                for (int frame = 0; frame < frames; frame++) sum += samples[frame * channels + channel];
                float mean = (float)(sum / frames);
                if (mean == 0f) continue;
                for (int frame = 0; frame < frames; frame++) samples[frame * channels + channel] -= mean;
            }
        }

        /// <summary>Removes content below <paramref name="cutoff"/> with a 24 dB per octave Butterworth high-pass.</summary>
        /// <remarks>The filter starts in the steady state of the first sample, so a signal that does not start at zero gains no start transient.</remarks>
        internal static void HighPass(float[] samples, int channels, int frequency, float cutoff)
        {
            int frames = samples.Length / channels;
            if (frames == 0 || frequency <= 0) return;
            double nyquistSafe = frequency * 0.45;
            double f0 = Math.Max(1.0, Math.Min(cutoff, nyquistSafe));
            Biquad first = Biquad.HighPass(frequency, f0, ButterworthQ1);
            Biquad second = Biquad.HighPass(frequency, f0, ButterworthQ2);
            for (int channel = 0; channel < channels; channel++)
            {
                double initial = samples[channel];
                first.PrimeForConstant(initial);
                second.Reset();
                for (int frame = 0; frame < frames; frame++)
                {
                    int index = frame * channels + channel;
                    samples[index] = (float)second.Process(first.Process(samples[index]));
                }
            }
        }

        internal static void Reverse(float[] samples, int channels)
        {
            int frames = samples.Length / channels;
            for (int left = 0, right = frames - 1; left < right; left++, right--)
            {
                int leftIndex = left * channels;
                int rightIndex = right * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    float swap = samples[leftIndex + channel];
                    samples[leftIndex + channel] = samples[rightIndex + channel];
                    samples[rightIndex + channel] = swap;
                }
            }
        }

        /// <summary>Applies a fade over the first (fade in) or last (fade out) frames using a sampled curve table.</summary>
        internal static void ApplyFade(float[] samples, int channels, int requestedFrames, bool fadeIn, float[] curve)
        {
            int totalFrames = samples.Length / channels;
            int frames = Math.Min(Math.Max(0, requestedFrames), totalFrames);
            for (int i = 0; i < frames; i++)
            {
                float t = frames <= 1 ? 1f : (float)i / (frames - 1);
                float curveValue = SampleCurve(curve, t);
                int frame = fadeIn ? i : totalFrames - 1 - i;
                float multiplier = curve == null ? t : fadeIn ? curveValue : SampleCurve(curve, 1f - t);
                for (int channel = 0; channel < channels; channel++) samples[frame * channels + channel] *= multiplier;
            }
        }

        internal static float SampleCurve(float[] values, float t)
        {
            if (values == null || values.Length == 0) return t;
            float position = Clamp(t, 0f, 1f) * (values.Length - 1);
            int first = Math.Max(0, Math.Min(values.Length - 1, (int)Math.Floor(position)));
            int second = Math.Min(first + 1, values.Length - 1);
            float amount = position - first;
            return values[first] + (values[second] - values[first]) * amount;
        }

        /// <summary>Scales samples so their peak reaches <paramref name="targetPeak"/>; silent input stays unchanged.</summary>
        /// <returns>The applied factor.</returns>
        internal static float NormalizePeak(float[] samples, float targetPeak)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++) peak = Math.Max(peak, Math.Abs(samples[i]));
            if (peak <= 0f) return 1f;
            float factor = targetPeak / peak;
            for (int i = 0; i < samples.Length; i++) samples[i] *= factor;
            return factor;
        }

        internal static void ApplyGain(float[] samples, float gain)
        {
            if (gain == 1f) return;
            for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
        }

        /// <summary>Scales samples and keeps them inside the WAV range.</summary>
        internal static void ApplyVolume(float[] samples, float volume)
        {
            for (int i = 0; i < samples.Length; i++) samples[i] = Clamp(samples[i] * volume, -1f, 1f);
        }

        internal static float GetPeak(float[] samples)
        {
            return GetPeak(samples, out _);
        }

        internal static float GetPeak(float[] samples, out int clipped)
        {
            float peak = 0f;
            clipped = 0;
            if (samples == null) return 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float value = Math.Abs(samples[i]);
                if (value > peak) peak = value;
                if (value >= ClippingLevel) clipped++;
            }

            return peak;
        }

        /// <summary>Folds the last frames into the first frames so the remaining audio repeats without a seam.</summary>
        /// <returns>The folded tail length in frames; playback and export end that many frames earlier.</returns>
        internal static int ApplyLoopCrossfade(float[] samples, int channels, int requestedFrames, AudioLoopShape shape)
        {
            int frames = samples.Length / channels;
            int crossfade = Math.Min(Math.Max(0, requestedFrames), frames / 2);
            if (crossfade < 1) return 0;
            int tailStart = frames - crossfade;
            for (int i = 0; i < crossfade; i++)
            {
                // At i = 0 the head is pure tail continuation, so the end of the loop flows into its start sample for sample.
                float t = (float)i / crossfade;
                float headWeight;
                float tailWeight;
                if (shape == AudioLoopShape.Linear)
                {
                    headWeight = t;
                    tailWeight = 1f - t;
                }
                else
                {
                    headWeight = (float)Math.Sin(t * Math.PI * 0.5);
                    tailWeight = (float)Math.Cos(t * Math.PI * 0.5);
                }

                int head = i * channels;
                int tail = (tailStart + i) * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    samples[head + channel] = samples[head + channel] * headWeight + samples[tail + channel] * tailWeight;
                }
            }

            return crossfade;
        }

        /// <summary>Writes the chosen mono signal into every channel so playback keeps the source layout.</summary>
        internal static void ConvertChannels(float[] samples, int channels, AudioOutputChannels mode)
        {
            if (mode == AudioOutputChannels.Keep || channels < 2) return;
            int frames = samples.Length / channels;
            for (int frame = 0; frame < frames; frame++)
            {
                int index = frame * channels;
                float value;
                switch (mode)
                {
                    case AudioOutputChannels.Left:
                        value = samples[index];
                        break;
                    case AudioOutputChannels.Right:
                        value = samples[index + 1];
                        break;
                    default:
                        float sum = 0f;
                        for (int channel = 0; channel < channels; channel++) sum += samples[index + channel];
                        value = sum / channels;
                        break;
                }

                for (int channel = 0; channel < channels; channel++) samples[index + channel] = value;
            }
        }

        /// <summary>Correlation of the first two channels from -1 (opposite phase) to 1 (identical); 1 for silent or non-stereo input.</summary>
        internal static float ChannelCorrelation(float[] samples, int channels)
        {
            if (channels < 2) return 1f;
            double crossSum = 0;
            double leftSum = 0;
            double rightSum = 0;
            int frames = samples.Length / channels;
            for (int frame = 0; frame < frames; frame++)
            {
                double left = samples[frame * channels];
                double right = samples[frame * channels + 1];
                crossSum += left * right;
                leftSum += left * left;
                rightSum += right * right;
            }

            if (leftSum <= 1e-12 || rightSum <= 1e-12) return 1f;
            return (float)(crossSum / Math.Sqrt(leftSum * rightSum));
        }

        internal static float[] ExtractChannel(float[] samples, int channels, int channel)
        {
            int frames = samples.Length / channels;
            float[] mono = new float[frames];
            for (int frame = 0; frame < frames; frame++) mono[frame] = samples[frame * channels + channel];
            return mono;
        }

        internal static float Clamp(float value, float minimum, float maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        private struct Biquad
        {
            private double _b0;
            private double _b1;
            private double _b2;
            private double _a1;
            private double _a2;
            private double _z1;
            private double _z2;

            internal static Biquad HighPass(int sampleRate, double cutoff, double q)
            {
                double omega = 2.0 * Math.PI * cutoff / sampleRate;
                double cos = Math.Cos(omega);
                double alpha = Math.Sin(omega) / (2.0 * q);
                double a0 = 1.0 + alpha;
                return new Biquad
                {
                    _b0 = (1.0 + cos) * 0.5 / a0,
                    _b1 = -(1.0 + cos) / a0,
                    _b2 = (1.0 + cos) * 0.5 / a0,
                    _a1 = -2.0 * cos / a0,
                    _a2 = (1.0 - alpha) / a0
                };
            }

            internal void Reset()
            {
                _z1 = 0;
                _z2 = 0;
            }

            // A high-pass outputs zero for a constant input; these are the matching transposed direct form II states.
            internal void PrimeForConstant(double value)
            {
                _z2 = _b2 * value;
                _z1 = (_b1 + _b2) * value;
            }

            internal double Process(double input)
            {
                double output = _b0 * input + _z1;
                _z1 = _b1 * input - _a1 * output + _z2;
                _z2 = _b2 * input - _a2 * output;
                return output;
            }
        }
    }
}
