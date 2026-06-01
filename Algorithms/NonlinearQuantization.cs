using System;
using AudioCompressor.Models;

namespace AudioCompressor.Algorithms
{
    /// <summary>
    /// NLQ — تكميم μ-law بـ 4-bit (توفير ~75% من PCM 16-bit).
    /// </summary>
    public class NonlinearQuantization : ICompressionAlgorithm
    {
        public string Name => "Nonlinear Quantization (μ-law 4-bit)";
        public string ShortName => "NLQ";
        public string Description => "μ-law 4-bit — توفير ~75%";

        private const double MU = 255.0;
        private static readonly byte[] Magic = { (byte)'N', (byte)'L', (byte)'Q', 1 };

        private byte EncodeMuLaw4Bit(short sample)
        {
            double x = sample / 32768.0;
            double sign = x < 0 ? -1 : 1;
            double compressed = sign * Math.Log(1 + MU * Math.Abs(x)) / Math.Log(1 + MU);
            byte fullByte = (byte)((compressed + 1.0) / 2.0 * 255);
            return (byte)((fullByte >> 4) & 0xF);
        }

        private short DecodeMuLaw4Bit(byte nibble)
        {
            byte fullByte = (byte)(((nibble & 0xF) << 4) | (nibble & 0xF));
            double y = (fullByte / 255.0) * 2.0 - 1.0;
            double sign = y < 0 ? -1 : 1;
            double expanded = sign * (1.0 / MU) * (Math.Pow(1 + MU, Math.Abs(y)) - 1);
            return (short)(expanded * 32768.0);
        }

        public byte[] Compress(short[] samples, CompressionSettings settings, IProgress<int> progress)
        {
            if (samples == null || samples.Length == 0)
                return Array.Empty<byte>();

            int packed = (samples.Length + 1) / 2;
            byte[] result = new byte[Magic.Length + packed];
            Array.Copy(Magic, 0, result, 0, Magic.Length);

            int reportStep = Math.Max(1, samples.Length / 100);
            int off = Magic.Length;

            for (int i = 0; i < samples.Length; i++)
            {
                byte nibble = EncodeMuLaw4Bit(samples[i]);
                int idx = off + i / 2;

                if (i % 2 == 0)
                    result[idx] = (byte)(nibble << 4);
                else
                    result[idx] = (byte)((result[idx] & 0xF0) | (nibble & 0x0F));

                if (i % reportStep == 0)
                    progress?.Report((int)(i / (double)samples.Length * 100));
            }

            progress?.Report(100);
            return result;
        }

        public short[] Decompress(byte[] data, CompressionSettings settings)
        {
            if (data == null || data.Length < Magic.Length + 1)
                return Array.Empty<short>();

            int start = (data[0] == Magic[0] && data[1] == Magic[1]) ? Magic.Length : 0;
            int expected = settings.ExpectedSampleCount > 0 ? settings.ExpectedSampleCount : int.MaxValue;
            int resultLength = Math.Min((data.Length - start) * 2, expected);
            short[] result = new short[resultLength];

            for (int i = 0; i < data.Length - start && i * 2 < resultLength; i++)
            {
                byte b = data[start + i];
                byte n1 = (byte)((b >> 4) & 0xF);
                byte n2 = (byte)(b & 0x0F);

                if (i * 2 < resultLength)
                    result[i * 2] = DecodeMuLaw4Bit(n1);
                if (i * 2 + 1 < resultLength)
                    result[i * 2 + 1] = DecodeMuLaw4Bit(n2);
            }

            return result;
        }
    }
}
