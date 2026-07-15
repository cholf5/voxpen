namespace CapsWriterSharp.Core.Transcribe;

/// <summary>
/// 长音频分段器。给定 float32 mono PCM，按 <c>duration</c> 秒分段，相邻段 <c>overlap</c> 秒重叠。
/// 与 CapsWriter-Offline 客户端 <c>File_Transcriber</c> 里那套 offset += duration - overlap 的切片
/// 语义等价，末段自动截到样本尾部。
/// </summary>
public static class AudioSegmenter
{
    /// <summary>音频段：一段 PCM + 段在整段音频里的起始偏移（秒）。</summary>
    public readonly record struct AudioSegment(float[] Samples, double OffsetSeconds);

    /// <summary>
    /// 遍历 <paramref name="samples"/> 产出音频段。
    /// </summary>
    /// <param name="samples">整段 float32 mono PCM。</param>
    /// <param name="sampleRate">采样率，Hz。</param>
    /// <param name="durationSeconds">每段目标时长（秒）。</param>
    /// <param name="overlapSeconds">相邻段重叠（秒）；必须 &lt; durationSeconds。</param>
    /// <exception cref="ArgumentException">参数不合法时抛出。</exception>
    public static IEnumerable<AudioSegment> Segment(
        float[] samples,
        int sampleRate,
        double durationSeconds,
        double overlapSeconds)
    {
        if (samples is null) throw new ArgumentNullException(nameof(samples));
        if (sampleRate <= 0) throw new ArgumentException("sampleRate must be > 0", nameof(sampleRate));
        if (durationSeconds <= 0) throw new ArgumentException("duration must be > 0", nameof(durationSeconds));
        if (overlapSeconds < 0) throw new ArgumentException("overlap must be >= 0", nameof(overlapSeconds));
        if (overlapSeconds >= durationSeconds)
            throw new ArgumentException("overlap must be < duration", nameof(overlapSeconds));

        if (samples.Length == 0) yield break;

        int segmentSize = (int)Math.Round(durationSeconds * sampleRate);
        int strideSize = (int)Math.Round((durationSeconds - overlapSeconds) * sampleRate);
        if (segmentSize <= 0) segmentSize = samples.Length;
        if (strideSize <= 0) strideSize = segmentSize;

        int offset = 0;
        while (offset < samples.Length)
        {
            int take = Math.Min(segmentSize, samples.Length - offset);
            var slice = new float[take];
            Array.Copy(samples, offset, slice, 0, take);
            double offsetSec = (double)offset / sampleRate;
            yield return new AudioSegment(slice, offsetSec);

            // 已经取到末尾，停
            if (offset + take >= samples.Length) yield break;
            offset += strideSize;
        }
    }
}
