using System;
using System.Collections.Generic;
using AudioCompressor.Models;

namespace AudioCompressor.Algorithms
{
    /// <summary>
    /// DPCM — فروق 8-bit بين العينات (توفير ~50%، مختلف عن NLQ 4-bit).
    /// </summary>
    public class DPCM : ICompressionAlgorithm
    {
        public string Name => "Differential Pulse Code Modulation";
        public string ShortName => "DPCM";
        public string Description => "فروق 8-bit — توفير ~50% (جودة أعلى من NLQ)";

        private static readonly byte[] Magic = { (byte)'D', (byte)'P', (byte)'C', (byte)'M' };

        public byte[] Compress(short[] samples, CompressionSettings settings, IProgress<int> progress)
        {
            if (samples == null || samples.Length == 0)
                return Array.Empty<byte>();

            var result = new List<byte>(4 + 2 + samples.Length);
            foreach (byte b in Magic) result.Add(b);

            result.Add((byte)(samples[0] & 0xFF));
            result.Add((byte)((samples[0] >> 8) & 0xFF));

            int shift = settings.QuantizationShift;
            int reportStep = Math.Max(1, samples.Length / 100);
            short prev = samples[0];

            for (int i = 1; i < samples.Length; i++)
            {
                int diff = samples[i] - prev;
                diff = diff >> shift;
                diff = Math.Max(-128, Math.Min(127, diff));

                result.Add((byte)(sbyte)diff);

                prev = (short)(prev + (diff << shift));
                prev = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, (int)prev));

                if (i % reportStep == 0)
                    progress?.Report((int)(i / (double)samples.Length * 100));
            }

            progress?.Report(100);
            return result.ToArray();
        }

        public short[] Decompress(byte[] data, CompressionSettings settings)
        {
            if (data == null || data.Length < 6)
                return Array.Empty<short>();

            int start = (data[0] == Magic[0] && data[1] == Magic[1]) ? 4 : 0;
            if (data.Length < start + 2)
                return Array.Empty<short>();

            int shift = settings.QuantizationShift;
            int expected = settings.ExpectedSampleCount > 0 ? settings.ExpectedSampleCount : int.MaxValue;
            var result = new List<short>(Math.Min(expected, data.Length - start));

            short prev = (short)(data[start] | (data[start + 1] << 8));
            result.Add(prev);

            for (int i = start + 2; i < data.Length && result.Count < expected; i++)
            {
                int diff = (sbyte)data[i];
                int reconstructed = prev + (diff << shift);
                reconstructed = Math.Max(short.MinValue, Math.Min(short.MaxValue, reconstructed));
                prev = (short)reconstructed;
                result.Add(prev);
            }

            return result.ToArray();
        }
    }
}
