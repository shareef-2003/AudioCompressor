using System;


namespace AudioCompressor.Algorithms
{
    /// <summary>
    /// واجهة مشتركة لجميع خوارزميات الضغط الصوتي
    /// </summary>
    public interface ICompressionAlgorithm
    {
        string Name { get; }
        string ShortName { get; }
        string Description { get; }

        /// <summary>ضغط البيانات الصوتية الخام</summary>
        byte[] Compress(short[] samples, Models.CompressionSettings settings, IProgress<int> progress);

        /// <summary>فك ضغط البيانات</summary>
        short[] Decompress(byte[] data, Models.CompressionSettings settings);
    }
}
