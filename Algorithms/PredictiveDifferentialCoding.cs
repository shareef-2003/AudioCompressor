using System;
using System.Collections.Generic;
using AudioCompressor.Models;

namespace AudioCompressor.Algorithms
{
    /// <summary>
    /// Predictive Differential Coding (PDC)
    /// تنبؤ خطي + ترميز بقايا 2-bit (أصغر وأسرع من DPCM 4-bit).
    /// </summary>
    public class PredictiveDifferentialCoding : ICompressionAlgorithm
    {
        public string Name => "Predictive Differential Coding";
        public string ShortName => "PDC";
        public string Description => "تنبؤ خطي + بقايا 2-bit — توفير ~87%";

        private static readonly byte[] Magic = { (byte)'P', (byte)'D', (byte)'C', 1 };

        private const int Order = 4;
        private static readonly double[] Coeffs = { 1.3, -0.5, 0.15, -0.05 };

        private static int Predict(short[] ring, int ringIdx, int filled)
        {
            double pred = 0;
            for (int k = 0; k < Order && k < filled; k++)
            {
                int idx = (ringIdx - 1 - k + Order) % Order;
                pred += Coeffs[k] * ring[idx];
            }
            return (int)Math.Round(pred);
        }

        private static void PushSample(short[] ring, ref int ringIdx, ref int filled, short sample)
        {
            ring[ringIdx] = sample;
            ringIdx = (ringIdx + 1) % Order;
            if (filled < Order) filled++;
        }

        public byte[] Compress(short[] samples, CompressionSettings settings, IProgress<int> progress)
        {
            if (samples == null || samples.Length == 0)
                return Array.Empty<byte>();

            var result = new List<byte>(Magic.Length + samples.Length / 4 + Order * 2 + 8);
            foreach (byte b in Magic) result.Add(b);

            short[] ring = new short[Order];
            int ringIdx = 0;
            int filled = 0;

            int prime = Math.Min(Order, samples.Length);
            for (int i = 0; i < prime; i++)
            {
                result.Add((byte)(samples[i] & 0xFF));
                result.Add((byte)((samples[i] >> 8) & 0xFF));
                PushSample(ring, ref ringIdx, ref filled, samples[i]);
            }

            int shift = settings.QuantizationShift;
            int reportStep = Math.Max(1, samples.Length / 100);
            int bitBuffer = 0;
            int bitCount = 0;

            for (int i = Order; i < samples.Length; i++)
            {
                int prediction = Predict(ring, ringIdx, filled);
                int error = samples[i] - prediction;
                error = Math.Max(-2, Math.Min(1, error >> shift));
                int code = error + 2; // 0..3

                int reconstructed = prediction + (error << shift);
                reconstructed = Math.Max(short.MinValue, Math.Min(short.MaxValue, reconstructed));
                PushSample(ring, ref ringIdx, ref filled, (short)reconstructed);

                bitBuffer = (bitBuffer << 2) | code;
                bitCount += 2;
                if (bitCount == 8)
                {
                    result.Add((byte)bitBuffer);
                    bitBuffer = 0;
                    bitCount = 0;
                }

                if (i % reportStep == 0)
                    progress?.Report((int)(i / (double)samples.Length * 100));
            }

            if (bitCount > 0)
                result.Add((byte)(bitBuffer << (8 - bitCount)));

            progress?.Report(100);
            return result.ToArray();
        }

        public short[] Decompress(byte[] data, CompressionSettings settings)
        {
            if (data == null || data.Length < Magic.Length + Order * 2)
                return Array.Empty<short>();

            var result = new List<short>(settings.ExpectedSampleCount > 0 ? settings.ExpectedSampleCount : data.Length * 4);
            int idx = (data[0] == Magic[0] && data[1] == Magic[1]) ? Magic.Length : 0;
            int shift = settings.QuantizationShift;
            short[] ring = new short[Order];
            int ringIdx = 0;
            int filled = 0;

            for (int i = 0; i < Order && idx + 1 < data.Length; i++, idx += 2)
            {
                short s = (short)(data[idx] | (data[idx + 1] << 8));
                result.Add(s);
                PushSample(ring, ref ringIdx, ref filled, s);
            }

            int expected = settings.ExpectedSampleCount > 0 ? settings.ExpectedSampleCount : int.MaxValue;
            int bitPos = 0;
            int currentByte = idx < data.Length ? data[idx] : 0;

            while (result.Count < expected)
            {
                if (idx >= data.Length && bitPos >= 8)
                    break;

                if (bitPos >= 8)
                {
                    idx++;
                    if (idx >= data.Length) break;
                    currentByte = data[idx];
                    bitPos = 0;
                }

                int code = (currentByte >> (6 - bitPos)) & 0x3;
                bitPos += 2;
                int error = code - 2;

                int prediction = Predict(ring, ringIdx, filled);
                int reconstructed = prediction + (error << shift);
                reconstructed = Math.Max(short.MinValue, Math.Min(short.MaxValue, reconstructed));
                short sample = (short)reconstructed;
                result.Add(sample);
                PushSample(ring, ref ringIdx, ref filled, sample);
            }

            return result.ToArray();
        }
    }
}
