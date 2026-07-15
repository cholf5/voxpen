using System.Text;
using System.Text.RegularExpressions;

namespace CapsWriterSharp.Core.Transcribe;

/// <summary>
/// 长文本智能换行。端口自 Python <c>result_handler.smart_split</c>。
///
/// 规则：
/// 1. 保留标点。
/// 2. 强标点（<c>。？.?!</c>）强制换行。
/// 3. 弱标点（<c>，,</c>）只有在当前累积长度 &gt; <paramref name="minChars"/> 时才换行，避免"，"处切碎。
/// 4. 英文标点必须后跟空白符或字符串末尾才被识别，避免 <c>3.14</c> 被切成两行。
/// </summary>
public static class SmartSplit
{
    private static readonly Regex SplitRegex = new(
        @"([，。？]|[.,?!](?:\s+|$))",
        RegexOptions.Compiled);

    private static readonly HashSet<char> StrongPunct = new() { '。', '？', '.', '?', '!' };
    private static readonly HashSet<char> PunctChars = new() { '，', '。', '？', ',', '.', '?', '!' };

    /// <summary>
    /// 按规则拆行；返回值行间用 '\n' 拼接，标点保留在行尾。
    /// </summary>
    /// <param name="text">原始文本。空串或 null 原样返回。</param>
    /// <param name="minChars">弱标点触发换行的最小累积长度（默认 2）。</param>
    public static string Split(string? text, int minChars = 2)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var parts = SplitRegex.Split(text);
        var lines = new List<string>();
        var buffer = new StringBuilder();

        foreach (var part in parts)
        {
            var clean = part.Trim();
            if (clean.Length == 1 && PunctChars.Contains(clean[0]))
            {
                buffer.Append(part);
                bool isStrong = StrongPunct.Contains(clean[0]);
                if (isStrong || buffer.Length > minChars)
                {
                    lines.Add(buffer.ToString());
                    buffer.Clear();
                }
            }
            else
            {
                buffer.Append(part);
            }
        }

        if (buffer.Length > 0)
        {
            lines.Add(buffer.ToString());
        }

        return string.Join("\n", lines);
    }
}
