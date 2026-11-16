using System;
using System.Collections.Generic;

namespace AudioTool
{
    /// <summary>Integrated loudness in LUFS. Negative infinity means silence below the absolute gate.</summary>
    internal readonly struct AudioLoudness
    {
        internal AudioLoudness(double integrated, bool isShort)
        {
            Integrated = integrated;
            IsShort = isShort;
        }

        internal double Integrated { get; }

        /// <summary>True for clips shorter than one 400 ms gating block, which are measured ungated over their whole length.</summary>
        internal bool IsShort { get; }

        internal bool IsSilent => double.IsNegativeInfinity(Integrated) || double.IsNaN(Integrated);

        internal static AudioLoudness Silent => new AudioLoudness(double.NegativeInfinity, false);
    }

    /// <summary>ITU-R BS.1770-4 integrated loudness: K-weighting, 400 ms blocks with 75 % overlap, absolute and relative gates.</summary>
    internal static class AudioLoudnessMeter
    {
        private const double AbsoluteGate = -70.0;
        private const double RelativeGate = -10.0;
        private const double BlockSeconds = 0.4;
        private const double HopSeconds = 0.1;

        /// <summary>Measures frames [<paramref name="startFrame"/>, <paramref name="endFrame"/>).</summary>
        /// <param name="firstChannelOnly">Measures channel 0 as a single channel, matching a mono export of a buffer that repeats it in every channel.</param>
        internal static AudioLoudness Measure(float[] samples, int channels, int frequency, int startFrame, int endFrame, bool firstChannelOnly = false)
        {
            if (samples == null || channels < 1 || frequency <= 0) return AudioLoudness.Silent;
            int totalFrames = samples.Length / channels;
            startFrame = Math.Max(0, startFrame);
            endFrame = Math.Min(totalFrames, endFrame);
            int frames = endFrame - startFrame;
            if (frames <= 0) return AudioLoudness.Silent;

            int measured = firstChannelOnly ? 1 : channels;
            double[] weights = GetChannelWeights(measured);
            GetKWeighting(frequency, out double[] shelfB, out double[] shelfA, out double[] passB, out double[] passA);

            double hop = frequency * HopSeconds;
            int subBlocks = (int)Math.Floor(frames / hop + 1e-9);
            List<double[]> subBlockSums = new List<double[]>(Math.Max(0, subBlocks));
            List<int> subBlockFrames = new List<int>(Math.Max(0, subBlocks));
            double[] totals = new double[measured];
            double[] current = new double[measured];
            int currentBlock = 0;
            int nextBoundary = subBlocks > 0 ? (int)Math.Round(hop) : int.MaxValue;
            int currentFrames = 0;

            double[] shelfZ1 = new double[measured];
            double[] shelfZ2 = new double[measured];
            double[] passZ1 = new double[measured];
            double[] passZ2 = new double[measured];

            for (int frame = 0; frame < frames; frame++)
            {
                if (frame == nextBoundary && currentBlock < subBlocks)
                {
                    subBlockSums.Add(current);
                    subBlockFrames.Add(currentFrames);
                    current = new double[measured];
                    currentFrames = 0;
                    currentBlock++;
                    nextBoundary = currentBlock < subBlocks ? (int)Math.Round((currentBlock + 1) * hop) : int.MaxValue;
                }

                int index = (startFrame + frame) * channels;
                for (int channel = 0; channel < measured; channel++)
                {
                    double x = samples[index + channel];
                    double shelf = shelfB[0] * x + shelfZ1[channel];
                    shelfZ1[channel] = shelfB[1] * x - shelfA[1] * shelf + shelfZ2[channel];
                    shelfZ2[channel] = shelfB[2] * x - shelfA[2] * shelf;
                    double weighted = passB[0] * shelf + passZ1[channel];
                    passZ1[channel] = passB[1] * shelf - passA[1] * weighted + passZ2[channel];
                    passZ2[channel] = passB[2] * shelf - passA[2] * weighted;
                    double square = weighted * weighted;
                    current[channel] += square;
                    totals[channel] += square;
                }

                currentFrames++;
            }

            if (currentBlock < subBlocks && currentFrames > 0)
            {
                subBlockSums.Add(current);
                subBlockFrames.Add(currentFrames);
            }

            int blockCount = subBlockSums.Count - 3;
            if (frames < frequency * BlockSeconds || blockCount < 1)
            {
                double ungated = Loudness(totals, frames, weights);
                return new AudioLoudness(ungated <= AbsoluteGate ? double.NegativeInfinity : ungated, true);
            }

            double[] blockLoudness = new double[blockCount];
            double[][] blockPower = new double[blockCount][];
            for (int block = 0; block < blockCount; block++)
            {
                double[] power = new double[measured];
                int blockFrames = 0;
                for (int part = 0; part < 4; part++)
                {
                    double[] sums = subBlockSums[block + part];
                    for (int channel = 0; channel < measured; channel++) power[channel] += sums[channel];
                    blockFrames += subBlockFrames[block + part];
                }

                for (int channel = 0; channel < measured; channel++) power[channel] /= Math.Max(1, blockFrames);
                blockPower[block] = power;
                blockLoudness[block] = Loudness(power, 1, weights);
            }

            double relativeThreshold = GatedMean(blockPower, blockLoudness, AbsoluteGate, weights, measured) + RelativeGate;
            if (double.IsNegativeInfinity(relativeThreshold)) return AudioLoudness.Silent;
            double integrated = GatedMean(blockPower, blockLoudness, Math.Max(AbsoluteGate, relativeThreshold), weights, measured);
            return new AudioLoudness(integrated, false);
        }

        private static double GatedMean(double[][] blockPower, double[] blockLoudness, double gate, double[] weights, int channels)
        {
            double[] mean = new double[channels];
            int count = 0;
            for (int block = 0; block < blockLoudness.Length; block++)
            {
                if (blockLoudness[block] <= gate) continue;
                for (int channel = 0; channel < channels; channel++) mean[channel] += blockPower[block][channel];
                count++;
            }

            if (count == 0) return double.NegativeInfinity;
            return Loudness(mean, count, weights);
        }

        private static double Loudness(double[] sums, double divisor, double[] weights)
        {
            double total = 0;
            for (int channel = 0; channel < sums.Length; channel++) total += weights[channel] * sums[channel] / divisor;
            return total <= 0 ? double.NegativeInfinity : -0.691 + 10.0 * Math.Log10(total);
        }

        private static double[] GetChannelWeights(int channels)
        {
            double[] weights = new double[channels];
            for (int channel = 0; channel < channels; channel++) weights[channel] = 1.0;
            if (channels == 5)
            {
                weights[3] = 1.41;
                weights[4] = 1.41;
            }
            else if (channels == 6)
            {
                weights[3] = 0.0; // LFE
                weights[4] = 1.41;
                weights[5] = 1.41;
            }

            return weights;
        }

        // Pre-filter (high shelf) and RLB high-pass from BS.1770, derived for any sample rate by bilinear transform.
        private static void GetKWeighting(int frequency, out double[] shelfB, out double[] shelfA, out double[] passB, out double[] passA)
        {
            double f0 = 1681.974450955533;
            double gain = 3.999843853973347;
            double q = 0.7071752369554196;
            double k = Math.Tan(Math.PI * f0 / frequency);
            double vh = Math.Pow(10.0, gain / 20.0);
            double vb = Math.Pow(vh, 0.4996667741545416);
            double a0 = 1.0 + k / q + k * k;
            shelfB = new[] { (vh + vb * k / q + k * k) / a0, 2.0 * (k * k - vh) / a0, (vh - vb * k / q + k * k) / a0 };
            shelfA = new[] { 1.0, 2.0 * (k * k - 1.0) / a0, (1.0 - k / q + k * k) / a0 };

            f0 = 38.13547087602444;
            q = 0.5003270373238773;
            k = Math.Tan(Math.PI * f0 / frequency);
            double passA0 = 1.0 + k / q + k * k;
            passB = new[] { 1.0, -2.0, 1.0 };
            passA = new[] { 1.0, 2.0 * (k * k - 1.0) / passA0, (1.0 - k / q + k * k) / passA0 };
        }
    }
}
