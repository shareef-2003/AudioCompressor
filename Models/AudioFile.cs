using System;

namespace AudioCompressor.Models
{
    /// <summary>معلومات الملف الصوتي المُحمَّل</summary>
    public class AudioFile
    {
        public string FilePath { get; set; }
        public string FileName => System.IO.Path.GetFileName(FilePath);
        public long FileSize { get; set; }
        public double Duration { get; set; }       // بالثواني
        public int SampleRate { get; set; }        // Hz
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public string Encoding { get; set; }
        public short[] Samples { get; set; }       // PCM raw samples
        public int BitRate =>
        Duration > 0 ? (int)(FileSize * 8.0 / Duration / 1000) : 0;// kbps

        public string FileSizeFormatted => FormatSize(FileSize);
        public string DurationFormatted => TimeSpan.FromSeconds(Duration).ToString(@"mm\:ss\.f");

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / 1048576.0).ToString("F2") + " MB";
        }
    }

    /// <summary>إعدادات عملية الضغط</summary>
    public class CompressionSettings
    {
        public string AlgorithmKey { get; set; } = "DPCM";
        public int TargetSampleRate { get; set; } = 22050;
        public int QuantizationLevels { get; set; } = 256;
        public int TargetBitRate { get; set; } = 128;
        public int DeltaStep { get; set; } = 512;
        public int ExpectedSampleCount { get; set; } = 0;

        // تحويل مستويات التكميم إلى shift
        public int QuantizationShift =>
            QuantizationLevels > 0
                ? Math.Max(0,
                    (int)Math.Log(32768.0 / QuantizationLevels, 2))
                : 0;
    }

    /// <summary>نتيجة عملية الضغط</summary>
    public class CompressionResult
    {
        public long OriginalSize { get; set; }
        public long CompressedSize { get; set; }
        public double CompressionRatio =>
            OriginalSize > 0 && CompressedSize > 0
                ? (double)OriginalSize / CompressedSize
                : 1; public double SavingPercent => OriginalSize > 0 ? (1.0 - (double)CompressedSize / OriginalSize) * 100 : 0;
        public double ElapsedSeconds { get; set; }
        public CompressionSettings Settings { get; set; }
        public byte[] CompressedData { get; set; }

        public string OriginalSizeFormatted => FormatSize(OriginalSize);
        public string CompressedSizeFormatted => FormatSize(CompressedSize);

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / 1048576.0).ToString("F2") + " MB";
        }
    }
}
