using System.Buffers.Binary;

namespace CapsWriterSharp.Core.Storage;

/// <summary>
/// 极简 WAV 写入器。仅支持 16 kHz PCM_S16LE 单声道（够 MVP 用）。
/// 输入的 float32 值域 [-1, 1]，会做限幅并转 int16。
/// </summary>
public static class WavWriter
{
    public static Task SaveMono16kAsync(
        string path,
        ReadOnlyMemory<float> samples,
        int sampleRate = 16000,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = BuildFile(samples.Span, sampleRate);
        return WriteAsync(path, payload, cancellationToken);
    }

    private static byte[] BuildFile(ReadOnlySpan<float> samples, int sampleRate)
    {
        int frameCount = samples.Length;
        int dataBytes = frameCount * 2;
        int fileSize = 44 + dataBytes;
        var buf = new byte[fileSize];
        var span = buf.AsSpan();

        // RIFF header
        WriteAscii(span[..4], "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4, 4), 36 + dataBytes);
        WriteAscii(span.Slice(8, 4), "WAVE");

        // fmt chunk
        WriteAscii(span.Slice(12, 4), "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(20, 2), 1);              // PCM
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(22, 2), 1);              // mono
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(28, 4), sampleRate * 2); // byte rate
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(32, 2), 2);              // block align
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(34, 2), 16);             // bits/sample

        // data chunk
        WriteAscii(span.Slice(36, 4), "data");
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(40, 4), dataBytes);

        var pcm = span.Slice(44, dataBytes);
        for (int i = 0; i < frameCount; i++)
        {
            float x = samples[i];
            if (x > 1f) x = 1f; else if (x < -1f) x = -1f;
            short s = (short)(x * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.Slice(i * 2, 2), s);
        }

        return buf;
    }

    private static void WriteAscii(Span<byte> dest, string ascii)
    {
        for (int i = 0; i < ascii.Length; i++)
        {
            dest[i] = (byte)ascii[i];
        }
    }

    private static async Task WriteAsync(string path, byte[] payload, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        await using var fs = new FileStream(
            path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await fs.WriteAsync(payload, ct).ConfigureAwait(false);
    }
}
