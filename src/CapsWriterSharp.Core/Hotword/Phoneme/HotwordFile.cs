namespace CapsWriterSharp.Core.Hotword.Phoneme;

/// <summary>
/// 一条热词记录：目标词 + 若干别名的音素序列 + 黑名单词。
/// </summary>
/// <param name="Target">目标词（替换后的输出）。</param>
/// <param name="PhonemeLists">目标 + 别名的音素序列列表（用于粗筛与精筛）。</param>
/// <param name="Blacklist">黑名单：命中时若邻近上下文出现这些词则跳过替换。</param>
public sealed record HotwordEntry(
    string Target,
    IReadOnlyList<IReadOnlyList<Phoneme>> PhonemeLists,
    IReadOnlySet<string> Blacklist);

/// <summary>
/// <c>hot.txt</c> 解析。格式（与原 CapsWriter 保持一致）：
/// <list type="bullet">
/// <item>每行一条热词；<c>#</c> 开头行视为注释。</item>
/// <item>形如 <c>Claude | Cloud</c>：<c>|</c> 分隔别名，第一项为目标词。</item>
/// <item>形如 <c>句子 ~~~ 一句话 | 另一句</c>：<c>~~~</c> 后是黑名单。</item>
/// </list>
/// </summary>
public static class HotwordFile
{
    /// <summary>解析文本内容为热词条目列表。</summary>
    public static List<HotwordEntry> Parse(string content, PhonemeExtractor? extractor = null)
    {
        extractor ??= PhonemeExtractor.Default;
        var result = new List<HotwordEntry>();
        if (string.IsNullOrEmpty(content)) return result;

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith("#")) continue;

            string hotwordPart, blacklistPart;
            int idx = line.IndexOf("~~~", StringComparison.Ordinal);
            if (idx >= 0)
            {
                hotwordPart = line[..idx];
                blacklistPart = line[(idx + 3)..];
            }
            else
            {
                hotwordPart = line;
                blacklistPart = string.Empty;
            }

            var aliases = SplitAndTrim(hotwordPart);
            if (aliases.Count == 0) continue;

            var target = aliases[0];
            var seqs = new List<IReadOnlyList<Phoneme>>(aliases.Count);
            foreach (var alias in aliases)
            {
                var ph = extractor.Extract(alias);
                if (ph.Count > 0) seqs.Add(ph);
            }
            if (seqs.Count == 0) continue;

            var blacklist = new HashSet<string>(SplitAndTrim(blacklistPart));
            result.Add(new HotwordEntry(target, seqs, blacklist));
        }
        return result;
    }

    /// <summary>从文件加载。文件不存在返回空列表。</summary>
    public static List<HotwordEntry> Load(string filePath, PhonemeExtractor? extractor = null)
    {
        if (!File.Exists(filePath)) return new();
        var content = File.ReadAllText(filePath);
        return Parse(content, extractor);
    }

    private static List<string> SplitAndTrim(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new();
        var parts = s.Split('|');
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }
}
