using System;
using System.Collections.Generic;
using AudioCompressor.Models;

namespace AudioCompressor.Algorithms
{
    /// <summary>
    /// ADM — 1-bit مع خطوة متكيفة + جدول خطوات (أكبر قليلاً من DM، جودة أفضل).
    /// </summary>
    public class AdaptiveDeltaModulation : ICompressionAlgorithm
    {
        public string Name => "Adaptive Delta Modulation";
        public string ShortName => "ADM";
        public string Description => "1-bit متكيف + جدول خطوات — ~85%";

        private const double Alpha = 1.5;
        private const double Beta = 0.85;
        private const int MinStep = 16;
        private const int MaxStep = 4096;
        private const int StepLogInterval = 256;

        private static readonly byte[] Magic = { (byte)'A', (byte)'D', (byte)'M', 1 };
        private static readonly byte[] Footer = { (byte)'S', (byte)'T', (byte)'P' };

        public byte[] Compress(short[] samples, CompressionSettings settings, IProgress<int> progress)
        {
            if (samples == null || samples.Length == 0)
                return Array.Empty<byte>();

            var result = new List<byte>(Magic.Length + 4 + samples.Length / 8 + samples.Length / StepLogInterval * 2);
            foreach (byte b in Magic) result.Add(b);

            double step = settings.DeltaStep;
            result.Add((byte)((int)step & 0xFF));
            result.Add((byte)(((int)step >> 8) & 0xFF));
            result.Add((byte)(samples[0] & 0xFF));
            result.Add((byte)((samples[0] >> 8) & 0xFF));

            var stepLog = new List<ushort>();
            int approx = samples[0];
            int? prevBit = null;
            int bitBuffer = 0;
            int bitCount = 0;
            int reportStep = Math.Max(1, samples.Length / 100);

            for (int i = 1; i < samples.Length; i++)
            {
                int bit = samples[i] >= approx ? 1 : 0;
                approx += bit == 1 ? (int)step : -(int)step;
                approx = Math.Max(short.MinValue, Math.Min(short.MaxValue, approx));

                if (prevBit.HasValue)
                {
                    if (bit == prevBit.Value)
                        step = Math.Min(step * Alpha, MaxStep);
                    else
                        step = Math.Max(step * Beta, MinStep);
                }

                prevBit = bit;

                if (i % StepLogInterval == 0)
                    stepLog.Add((ushort)Math.Round(step));

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

            foreach (byte b in Footer) result.Add(b);
            result.Add((byte)(stepLog.Count & 0xFF));
            result.Add((byte)((stepLog.Count >> 8) & 0xFF));
            foreach (ushort s in stepLog)
            {
                result.Add((byte)(s & 0xFF));
                result.Add((byte)(s >> 8));
            }

            progress?.Report(100);
            return result.ToArray();
        }

        public short[] Decompress(byte[] data, CompressionSettings settings)
        {
            if (data == null || data.Length < 6)
                return Array.Empty<short>();

            int footerIdx = FindFooter(data);
            if (footerIdx < 0)
                footerIdx = data.Length;

            int idx = 0;
            double step = settings.DeltaStep;

            if (data[0] == Magic[0] && data[1] == Magic[1])
            {
                step = data[2] | (data[3] << 8);
                idx = 4;
            }

            if (footerIdx < idx + 2)
                return Array.Empty<short>();

            var stepLog = ParseStepLog(data, footerIdx);
            int stepLogIdx = 0;

            var result = new List<short>();
            short first = (short)(data[idx] | (data[idx + 1] << 8));
            result.Add(first);
            int approx = first;
            idx += 2;

            int? prevBit = null;
            int expected = settings.ExpectedSampleCount > 0 ? settings.ExpectedSampleCount : int.MaxValue;

            for (int sampleIdx = 1; idx < footerIdx && result.Count < expected; idx++)
            {
                byte b = data[idx];
                for (int bitPos = 7; bitPos >= 0 && result.Count < expected; bitPos--)
                {
                    if (sampleIdx % StepLogInterval == 0 && stepLogIdx < stepLog.Count)
                        step = stepLog[stepLogIdx++];

                    int bit = (b >> bitPos) & 1;
                    approx += bit == 1 ? (int)step : -(int)step;
                    approx = Math.Max(short.MinValue, Math.Min(short.MaxValue, approx));

                    if (prevBit.HasValue)
                    {
                        if (bit == prevBit.Value)
                            step = Math.Min(step * Alpha, MaxStep);
                        else
                            step = Math.Max(step * Beta, MinStep);
                    }

                    prevBit = bit;
                    result.Add((short)approx);
                    sampleIdx++;
                }
            }

            return result.ToArray();
        }

        private static int FindFooter(byte[] data)
        {
            for (int i = data.Length - 3; i >= 4; i--)
            {
                if (data[i] == Footer[0] && data[i + 1] == Footer[1] && data[i + 2] == Footer[2])
                    return i;
            }
            return -1;
        }

        private static List<ushort> ParseStepLog(byte[] data, int footerIdx)
        {
            var log = new List<ushort>();
            if (footerIdx < 0 || footerIdx + 5 > data.Length)
                return log;

            int count = data[footerIdx + 3] | (data[footerIdx + 4] << 8);
            int pos = footerIdx + 5;
            for (int i = 0; i < count && pos + 1 < data.Length; i++, pos += 2)
                log.Add((ushort)(data[pos] | (data[pos + 1] << 8)));
            return log;
        }
    }
}
