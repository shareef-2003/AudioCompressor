using System;
using System.Collections.Generic;
using AudioCompressor.Models;

namespace AudioCompressor.Algorithms
{
    /// <summary>
    /// DM — بت واحد لكل عينة، خطوة ثابتة (أقصى ضغط ~87%).
    /// </summary>
    public class DeltaModulation : ICompressionAlgorithm
    {
        public string Name => "Delta Modulation";
        public string ShortName => "DM";
        public string Description => "1-bit خطوة ثابتة — توفير ~87%";

        private static readonly byte[] Magic = { (byte)'D', (byte)'M', 1, 0 };

        public byte[] Compress(short[] samples, CompressionSettings settings, IProgress<int> progress)
        {
            if (samples == null || samples.Length == 0)
                return Array.Empty<byte>();

            int step = settings.DeltaStep;
            var result = new List<byte>(Magic.Length + 4 + samples.Length / 8);

            foreach (byte b in Magic) result.Add(b);
            result.Add((byte)(step & 0xFF));
            result.Add((byte)((step >> 8) & 0xFF));
            result.Add((byte)(samples[0] & 0xFF));
            result.Add((byte)((samples[0] >> 8) & 0xFF));

            int approx = samples[0];
            int bitBuffer = 0;
            int bitCount = 0;
            int reportStep = Math.Max(1, samples.Length / 100);

            for (int i = 1; i < samples.Length; i++)
            {
                int bit = samples[i] >= approx ? 1 : 0;
                approx += bit == 1 ? step : -step;
                approx = Math.Max(short.MinValue, Math.Min(short.MaxValue, approx));

                bitBuffer = (bitBuffer << 1) | bit;
                bitCount++;
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
            if (data == null || data.Length < 4)
                return Array.Empty<short>();

            int idx = 0;
            int step = settings.DeltaStep;

            if (data[0] == Magic[0] && data[1] == Magic[1])
            {
                step = data[2] | (data[3] << 8);
                idx = 4;
            }

            if (data.Length < idx + 2)
                return Array.Empty<short>();

            var result = new List<short>();
            short first = (short)(data[idx] | (data[idx + 1] << 8));
            result.Add(first);
            int approx = first;
            idx += 2;

            int expected = settings.ExpectedSampleCount > 0 ? settings.ExpectedSampleCount : int.MaxValue;

            for (; idx < data.Length && result.Count < expected; idx++)
            {
                byte b = data[idx];
                for (int bit = 7; bit >= 0 && result.Count < expected; bit--)
                {
                    if (((b >> bit) & 1) == 1) approx += step;
                    else approx -= step;
                    approx = Math.Max(short.MinValue, Math.Min(short.MaxValue, approx));
                    result.Add((short)approx);
                }
            }

            return result.ToArray();
        }
    }
}
