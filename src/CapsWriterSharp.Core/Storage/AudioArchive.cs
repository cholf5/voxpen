using System.Text.RegularExpressions;

namespace CapsWriterSharp.Core.Storage;

/// <summary>
/// 录音归档：按 年/月/assets/ 目录结构存放 WAV + 同名 .txt 侧车文件。
/// 对应原项目：<c>save_audio=True</c> + 文件名前缀取识别结果前 N 字。
/// </summary>
public sealed class AudioArchive
{
    private static readonly Regex InvalidFileNameChars =
        new("[\\\\/:*?\"<>|\\r\\n\\t]+", RegexOptions.Compiled);

    private readonly string _rootDir;
    private readonly int _nameLen;

    public AudioArchive(string rootDir, int nameLength = 20)
    {
        _rootDir = rootDir;
        _nameLen = Math.Max(0, nameLength);
    }

    /// <summary>把一段音频与识别结果存档；失败静默（归档不应影响主流程）。</summary>
    public async Task SaveAsync(
        ReadOnlyMemory<float> samples,
        string recognizedText,
        DateTime timestampLocal,
        int sampleRate = 16000,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var yearMonth = Path.Combine(_rootDir,
                timestampLocal.Year.ToString("D4"),
                timestampLocal.Month.ToString("D2"),
                "assets");
            Directory.CreateDirectory(yearMonth);

            var stem = BuildFileStem(recognizedText, timestampLocal);
            var wavPath = Path.Combine(yearMonth, stem + ".wav");
            var txtPath = Path.Combine(yearMonth, stem + ".txt");

            await WavWriter.SaveMono16kAsync(wavPath, samples, sampleRate, cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrEmpty(recognizedText))
            {
                await File.WriteAllTextAsync(txtPath, recognizedText, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // 归档失败不能影响用户主流程
        }
    }

    private string BuildFileStem(string text, DateTime ts)
    {
        var timePart = ts.ToString("yyyyMMdd-HHmmss");
        var slug = Sanitize(text);
        if (slug.Length == 0) return timePart;

        if (slug.Length > _nameLen && _nameLen > 0)
        {
            slug = slug[.._nameLen];
        }
        return $"{timePart}_{slug}";
    }

    private static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = InvalidFileNameChars.Replace(text, "_").Trim();
        // 再去掉 Windows 保留字符（保险）
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(ch, '_');
        }
        return cleaned;
    }
}
