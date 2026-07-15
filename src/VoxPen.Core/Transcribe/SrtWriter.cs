using System.Globalization;
using System.Text;

namespace VoxPen.Core.Transcribe;

/// <summary>SRT 字幕文件写入器（标准 <c>HH:MM:SS,mmm</c> 格式）。</summary>
public static class SrtWriter
{
    /// <summary>把字幕列表格式化为 SRT 字符串（不写文件）。</summary>
    public static string Compose(IReadOnlyList<SubtitleEntry> subtitles)
    {
        if (subtitles == null || subtitles.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var s in subtitles)
        {
            sb.Append(s.Index).Append('\n');
            sb.Append(FormatTime(s.Start)).Append(" --> ").Append(FormatTime(s.End)).Append('\n');
            sb.Append(s.Content ?? string.Empty).Append("\n\n");
        }
        return sb.ToString();
    }

    /// <summary>把字幕列表写入 <paramref name="filePath"/>（UTF-8，无 BOM）。</summary>
    public static async Task WriteAsync(
        string filePath,
        IReadOnlyList<SubtitleEntry> subtitles,
        CancellationToken cancellationToken = default)
    {
        var content = Compose(subtitles);
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>SRT 时间格式：<c>HH:MM:SS,mmm</c>。负数与超长自动 clamp。</summary>
    internal static string FormatTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var totalMs = (long)Math.Round(t.TotalMilliseconds);
        var hours = totalMs / 3_600_000;
        totalMs %= 3_600_000;
        var minutes = totalMs / 60_000;
        totalMs %= 60_000;
        var seconds = totalMs / 1000;
        var millis = totalMs % 1000;
        return string.Format(CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2},{3:D3}", hours, minutes, seconds, millis);
    }
}
