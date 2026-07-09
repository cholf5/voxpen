using System.Globalization;

namespace CapsWriterSharp.Core.Postprocess;

/// <summary>
/// 末尾标点清理器。对应原项目 <c>trash_punc</c> / <c>trash_punc_thresh</c> / <c>trash_punc_apps</c>。
///
/// 规则：
/// - 短文本（token 数 &lt; threshold）或命中"强制清理进程"时，一律清除末尾标点
/// - 否则保留末尾标点
///
/// "token 数"按 Unicode 分类近似原项目的 "words" 概念：
/// 中日韩字符每字算 1；连续 ASCII/字母数字算 1。
/// </summary>
public sealed class TrashPuncCleaner
{
    private readonly char[] _trashChars;
    private readonly int _threshold;
    private readonly HashSet<string> _forceApps;

    public TrashPuncCleaner(string trashPunctuation, int threshold, IEnumerable<string> forceApps)
    {
        _trashChars = (trashPunctuation ?? string.Empty).ToCharArray();
        _threshold = threshold;
        _forceApps = new HashSet<string>(forceApps ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 清理末尾标点。
    /// </summary>
    /// <param name="text">识别结果</param>
    /// <param name="foregroundExeName">当前前台进程的 exe 名（可空）</param>
    public string Apply(string text, string? foregroundExeName = null)
    {
        if (string.IsNullOrEmpty(text) || _trashChars.Length == 0) return text;

        var forceStrip = foregroundExeName is not null && _forceApps.Contains(foregroundExeName);
        if (!forceStrip)
        {
            var tokens = CountTokens(text);
            if (tokens >= _threshold) return text;
        }

        // 去掉末尾的匹配字符（可能连续多个）
        int end = text.Length;
        while (end > 0 && Array.IndexOf(_trashChars, text[end - 1]) >= 0)
        {
            end--;
        }
        return end == text.Length ? text : text[..end];
    }

    private static int CountTokens(string text)
    {
        int count = 0;
        bool inLatinRun = false;

        foreach (var ch in text)
        {
            if (IsCjk(ch))
            {
                count++;
                inLatinRun = false;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                if (!inLatinRun) { count++; inLatinRun = true; }
            }
            else
            {
                inLatinRun = false;
            }
        }
        return count;
    }

    private static bool IsCjk(char ch)
    {
        // 覆盖常用 CJK 统一表意 + 常用假名（近似即可）
        if (ch >= 0x4E00 && ch <= 0x9FFF) return true;
        if (ch >= 0x3400 && ch <= 0x4DBF) return true;
        if (ch >= 0x3040 && ch <= 0x30FF) return true;
        if (ch >= 0xAC00 && ch <= 0xD7AF) return true;
        return false;
    }
}
