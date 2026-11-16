using System;
using System.Threading;
using System.Threading.Tasks;

namespace ImpossibleRobert.Common
{
    /// <summary>Band-limited sample-rate conversion with a Kaiser-windowed sinc kernel. Pure math, safe off the main thread.</summary>
    public static class EditorAudioResampler
    {
        private const int HalfWidth = 16; // zero crossings per side at the lower rate: 32 taps at least
        private const int TableResolution = 512; // kernel samples per source-sample unit
        private const double KaiserBeta = 9.0;
        private const int Chunk = 8192;

        /// <summary>Resamples mono samples to <paramref name="dstRate"/>, cutting off at 95% of the lower Nyquist frequency.</summary>
        /// <returns>The source array itself when both rates match, otherwise <c>src.Length * dstRate / srcRate</c> samples.</returns>
        public static float[] Resample(float[] src, int srcRate, int dstRate, CancellationToken token = default)
        {
            if (src == null || srcRate <= 0 || dstRate <= 0 || src.Length == 0) throw new ArgumentException("A valid source and sample rate are required.");
            token.ThrowIfCancellationRequested();
            if (srcRate == dstRate) return src;
            int outputLength = checked((int)((long)src.Length * dstRate / srcRate));
            return ResampleCore(src, 1, src.Length, outputLength, (double)srcRate / dstRate, Math.Min(1.0, (double)dstRate / srcRate), false, token);
        }

        /// <summary>Resamples interleaved frames to an exact output frame count.</summary>
        /// <param name="samples">Interleaved source samples.</param>
        /// <param name="channels">Channels per frame.</param>
        /// <param name="inputFrames">Source frames to read from the start of <paramref name="samples"/>.</param>
        /// <param name="outputFrames">Frames to produce; the conversion ratio is <paramref name="inputFrames"/> to this count.</param>
        /// <param name="cyclic">Treats the input as one period of a loop, so a seamless loop stays seamless after conversion.</param>
        /// <param name="token">Cancels the conversion.</param>
        /// <returns>A new interleaved array with <paramref name="outputFrames"/> frames.</returns>
        public static float[] ResampleInterleaved(float[] samples, int channels, int inputFrames, int outputFrames, bool cyclic, CancellationToken token = default)
        {
            if (samples == null || channels < 1 || inputFrames < 1 || outputFrames < 1 || (long)inputFrames * channels > samples.LongLength)
                throw new ArgumentException("Valid interleaved samples and frame counts are required.");
            token.ThrowIfCancellationRequested();
            if (inputFrames == outputFrames)
            {
                float[] copy = new float[inputFrames * channels];
                Array.Copy(samples, copy, copy.Length);
                return copy;
            }

            double ratio = (double)inputFrames / outputFrames;
            return ResampleCore(samples, channels, inputFrames, outputFrames, ratio, Math.Min(1.0, 1.0 / ratio), cyclic, token);
        }

        private static float[] ResampleCore(float[] src, int channels, int inputFrames, int outputFrames, double ratio, double bandwidth, bool cyclic, CancellationToken token)
        {
            double scale = bandwidth * 0.95; // cut-off relative to the source rate
            double span = HalfWidth / scale; // kernel half-width in source samples
            float[] kernel = BuildKernel(scale, span, out int tableLength, token);
            int halfTaps = (int)Math.Ceiling(span);
            float[] dst = new float[checked(outputFrames * channels)];
            int chunks = (outputFrames + Chunk - 1) / Chunk;
            Parallel.For(0, chunks, new ParallelOptions { CancellationToken = token }, c =>
            {
                int from = c * Chunk;
                int to = Math.Min(outputFrames, from + Chunk);
                for (int o = from; o < to; o++)
                {
                    if ((o & 511) == 0) token.ThrowIfCancellationRequested();
                    double t = o * ratio;
                    int center = (int)Math.Floor(t);
                    int n0 = center - halfTaps + 1;
                    int n1 = center + halfTaps;
                    for (int channel = 0; channel < channels; channel++)
                    {
                        double acc = 0;
                        for (int n = n0; n <= n1; n++)
                        {
                            int index = n;
                            if (index < 0 || index >= inputFrames)
                            {
                                if (!cyclic) continue;
                                index %= inputFrames;
                                if (index < 0) index += inputFrames;
                            }

                            double position = Math.Abs(n - t) * TableResolution;
                            int k = (int)position;
                            if (k >= tableLength) continue;
                            float fraction = (float)(position - k);
                            acc += src[index * channels + channel] * (kernel[k] + (kernel[k + 1] - kernel[k]) * fraction);
                        }

                        dst[o * channels + channel] = (float)acc;
                    }
                }
            });
            return dst;
        }

        private static float[] BuildKernel(double scale, double span, out int tableLength, CancellationToken token)
        {
            tableLength = (int)Math.Ceiling(span * TableResolution) + 2;
            float[] kernel = new float[tableLength + 1];
            double i0Beta = I0(KaiserBeta);
            for (int i = 0; i <= tableLength; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                double x = (double)i / TableResolution; // distance in source samples
                double arg = Math.PI * x * scale;
                double sinc = Math.Abs(arg) < 1e-12 ? 1.0 : Math.Sin(arg) / arg;
                double r = x / span;
                double window = r >= 1 ? 0 : I0(KaiserBeta * Math.Sqrt(1 - r * r)) / i0Beta;
                kernel[i] = (float)(sinc * window * scale);
            }

            return kernel;
        }

        private static double I0(double x)
        {
            double sum = 1, term = 1, hx = x / 2;
            for (int k = 1; k < 40; k++)
            {
                term *= hx / k;
                sum += term * term;
                if (term * term < 1e-12 * sum) break;
            }

            return sum;
        }
    }
}
