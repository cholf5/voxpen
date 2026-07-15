using System.Text;

namespace VoxPen.Core.Storage;

/// <summary>
/// Markdown 日记写入：把每次识别结果追写到 <c>{Root}/YYYY/MM/DD.md</c>。
/// 端口自原项目 <c>diary_writer.py</c>：首次创建文件时写入正则替换 Tip 头，
/// 每条追加 <c>[HH:MM:SS](assets/相对路径.wav) 文本\n\n</c>（无音频时省略链接）。
/// </summary>
public sealed class DiaryWriter
{
    /// <summary>MD 文件头部（保留原项目的“音频链接 ↔ HTML 控件”正则替换 Tip）。</summary>
    public const string HeaderMd =
        "```txt\n" +
        "正则表达式 Tip\n" +
        "\n" +
        "匹配到音频文件链接：\\[(.+)\\]\\((.{10,})\\)[\\s]*\n" +
        "替换为 HTML 控件：<audio controls><source src=\"$2\" type=\"audio/mpeg\">$1</audio>\\n\\n\n" +
        "\n" +
        "匹配 HTML 控件：<audio controls><source src=\"(.+)\" type=\"audio/mpeg\">(.+)</audio>\\n\\n\n" +
        "替换为文件链接：[$2]($1) \n" +
        "```\n" +
        "\n" +
        "\n";

    private readonly string _rootDir;

    public DiaryWriter(string rootDir)
    {
        _rootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
    }

    /// <summary>写入一条日记；返回目标 MD 文件路径。失败会向上抛。</summary>
    public string Write(string text, DateTime timestampLocal, string? audioPath = null)
    {
        var folder = Path.Combine(_rootDir,
            timestampLocal.Year.ToString("D4"),
            timestampLocal.Month.ToString("D2"));
        Directory.CreateDirectory(folder);

        var mdPath = Path.Combine(folder, timestampLocal.Day.ToString("D2") + ".md");
        var isNew = !File.Exists(mdPath);
        if (isNew)
        {
            File.WriteAllText(mdPath, HeaderMd, Encoding.UTF8);
        }

        var hms = timestampLocal.ToString("HH:mm:ss");
        var rel = BuildAudioLink(mdPath, audioPath);
        var line = rel.Length > 0
            ? $"[{hms}]({rel}) {text}\n\n"
            : $"{hms} {text}\n\n";

        // 追加，不写 BOM
        using var fs = new FileStream(mdPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var sw = new StreamWriter(fs, new UTF8Encoding(false));
        sw.Write(line);

        return mdPath;
    }

    /// <summary>异步版本，实际写入仍是同步（IO 极小，避免额外线程池调度）。</summary>
    public Task<string> WriteAsync(
        string text, DateTime timestampLocal, string? audioPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Write(text, timestampLocal, audioPath));
    }

    /// <summary>
    /// 计算音频文件相对 <paramref name="mdPath"/> 所在目录的 POSIX 链接，
    /// 空格转 <c>%20</c>；不在 md 同一层级内则退化为绝对 POSIX 路径。
    /// </summary>
    internal static string BuildAudioLink(string mdPath, string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath)) return string.Empty;

        string rel;
        try
        {
            var mdDir = Path.GetDirectoryName(Path.GetFullPath(mdPath));
            var audioFull = Path.GetFullPath(audioPath);
            if (!string.IsNullOrEmpty(mdDir) && HasCommonRoot(mdDir, audioFull))
            {
                rel = Path.GetRelativePath(mdDir, audioFull);
                // 若相对路径以 ".." 开头（音频在 md 目录之上），仍用相对形式
            }
            else
            {
                rel = audioFull;
            }
        }
        catch
        {
            rel = audioPath;
        }

        // 统一 POSIX 分隔符 + 空格转义
        rel = rel.Replace('\\', '/').Replace(" ", "%20");
        return rel;
    }

    private static bool HasCommonRoot(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
