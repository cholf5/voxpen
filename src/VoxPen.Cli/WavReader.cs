using System.Buffers.Binary;

namespace VoxPen.Cli;

/// <summary>
/// 极简 WAV 读取器：仅支持 PCM 8/16 位 单/多声道，会转为 float32 mono @ 16kHz。
/// 用于 CLI --file 模式对 ASR 引擎做单独回归测试。
/// </summary>
internal static class WavReader
{
    public static (float[] Samples, int SampleRate) ReadAsFloatMono(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> header = stackalloc byte[12];
        if (fs.Read(header) != 12 ||
            header[0] != (byte)'R' || header[1] != (byte)'I' ||
            header[2] != (byte)'F' || header[3] != (byte)'F' ||
            header[8] != (byte)'W' || header[9] != (byte)'A' ||
            header[10] != (byte)'V' || header[11] != (byte)'E')
        {
            throw new InvalidDataException("Not a RIFF/WAVE file.");
        }

        short audioFormat = 0, numChannels = 0, bitsPerSample = 0;
        int sampleRate = 0;
        byte[]? pcm = null;

        Span<byte> chunkHeader = stackalloc byte[8];
        while (fs.Read(chunkHeader) == 8)
        {
            int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(chunkHeader.Slice(4, 4));
            string chunkId = System.Text.Encoding.ASCII.GetString(chunkHeader.Slice(0, 4));

            if (chunkId == "fmt ")
            {
                var fmt = new byte[chunkSize];
                fs.ReadExactly(fmt);
                audioFormat = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(0, 2));
                numChannels = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(2, 2));
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4, 4));
                bitsPerSample = BinaryPrimitives.ReadInt16LittleEndian(fmt.AsSpan(14, 2));
            }
            else if (chunkId == "data")
            {
                pcm = new byte[chunkSize];
                fs.ReadExactly(pcm);
                break;
            }
            else
            {
                fs.Seek(chunkSize, SeekOrigin.Current);
            }
        }

        if (pcm is null || audioFormat != 1)
        {
            throw new InvalidDataException("Only uncompressed PCM WAV is supported.");
        }

        int bytesPerSample = bitsPerSample / 8;
        int frameCount = pcm.Length / (bytesPerSample * numChannels);
        var mono = new float[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            float sum = 0f;
            for (int ch = 0; ch < numChannels; ch++)
            {
                int offset = (i * numChannels + ch) * bytesPerSample;
                float s = bitsPerSample switch
                {
                    16 => BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(offset, 2)) / 32768f,
                    8 => (pcm[offset] - 128) / 128f,
                    _ => throw new NotSupportedException($"Unsupported bits/sample: {bitsPerSample}")
                };
                sum += s;
            }
            mono[i] = sum / numChannels;
        }

        return (mono, sampleRate);
    }
}
