using System.Text;
using CapsWriterSharp.Core.Utils;

namespace CapsWriterSharp.Core.Transcribe;

/// <summary>带时间戳的字/词单元。</summary>
public readonly record struct TranscriptWord(string Word, double StartSec, double EndSec);

/// <summary>字幕条目（1-based Index）。</summary>
public readonly record struct SubtitleEntry(int Index, string Content, TimeSpan Start, TimeSpan End);

/// <summary>
/// 分行文本 ↔ 字级时间戳 对齐器。
/// 端口自 <c>core/tools/srt_from_txt.py::lines_match_words</c>：
/// 用 <see cref="SequenceMatcher"/> 把去标点/去空白/去数字后的 token 串与 line 串对齐，
/// 得到每行的 start/end 时间。
/// </summary>
public static class SubtitleAligner
{
    // 与 SegmentMerger / 原 Punctuation.ALL 一致
    private const string PunctChars = "，。！？；：、「」『』（）《》【】[]{},.!?;:\"'";
    private static readonly HashSet<char> IgnoreSet = BuildIgnoreSet();

    private static HashSet<char> BuildIgnoreSet()
    {
        var s = new HashSet<char>();
        foreach (var c in PunctChars) s.Add(c);
        return s;
    }

    private static bool IsIgnored(char c)
        => char.IsWhiteSpace(c) || char.IsDigit(c) || IgnoreSet.Contains(c);

    /// <summary>
    /// 根据 tokens/timestamps 构建 words，末位默认 +<paramref name="defaultDuration"/> 秒，
    /// 中间被下一个 token 的 start 截断（避免重叠）。
    /// </summary>
    public static IReadOnlyList<TranscriptWord> BuildWordsFromTokens(
        IReadOnlyList<string> tokens,
        IReadOnlyList<double> timestamps,
        double defaultDuration = 0.2)
    {
        if (tokens == null || timestamps == null) return Array.Empty<TranscriptWord>();
        var n = Math.Min(tokens.Count, timestamps.Count);
        if (n == 0) return Array.Empty<TranscriptWord>();

        var words = new TranscriptWord[n];
        for (var i = 0; i < n; i++)
        {
            var word = (tokens[i] ?? string.Empty).Replace("@", string.Empty);
            var start = timestamps[i];
            var end = start + defaultDuration;
            words[i] = new TranscriptWord(word, start, end);
        }
        // 截断相邻重叠
        for (var i = 0; i < n - 1; i++)
        {
            if (words[i].EndSec > words[i + 1].StartSec)
            {
                words[i] = words[i] with { EndSec = words[i + 1].StartSec };
            }
        }
        return words;
    }

    /// <summary>把分行文本对齐到字级 <paramref name="words"/> 上，产出字幕列表。</summary>
    public static IReadOnlyList<SubtitleEntry> Align(
        IReadOnlyList<string> textLines,
        IReadOnlyList<TranscriptWord> words)
    {
        if (textLines == null || words == null || textLines.Count == 0 || words.Count == 0)
            return Array.Empty<SubtitleEntry>();

        // 1) 展平 words 为「pure char 串」，同时记录每个 char 属于哪个 word
        var tokenCharsBuilder = new StringBuilder();
        var tokenIndices = new List<int>(words.Count * 2);
        for (var i = 0; i < words.Count; i++)
        {
            var wordClean = Clean(words[i].Word);
            foreach (var ch in wordClean)
            {
                tokenCharsBuilder.Append(ch);
                tokenIndices.Add(i);
            }
        }
        var pureTokens = tokenCharsBuilder.ToString();

        // 2) 展平所有行为一段清洁串
        var allLinesBuilder = new StringBuilder();
        foreach (var raw in textLines)
        {
            allLinesBuilder.Append(Clean(raw));
        }
        var cleanAllLines = allLinesBuilder.ToString();

        // 3) 全局对齐 → 建立 「lines 中字符偏移 → word 索引」的稀疏映射
        var charToWord = new Dictionary<int, int>(cleanAllLines.Length);
        if (pureTokens.Length > 0 && cleanAllLines.Length > 0)
        {
            var matches = SequenceMatcher.GetMatchingBlocks(pureTokens, cleanAllLines);
            foreach (var m in matches)
            {
                for (var i = 0; i < m.Size; i++)
                {
                    charToWord[m.B + i] = tokenIndices[m.A + i];
                }
            }
        }

        // 4) 按行切时间
        var result = new List<SubtitleEntry>(textLines.Count);
        var currentOffset = 0;
        var lastWordIdx = 0;

        foreach (var raw in textLines)
        {
            var lineClean = Clean(raw);
            if (lineClean.Length == 0) continue;

            var lineLen = lineClean.Length;

            int? minIdx = null, maxIdx = null;
            for (var i = currentOffset; i < currentOffset + lineLen; i++)
            {
                if (!charToWord.TryGetValue(i, out var w)) continue;
                if (minIdx == null || w < minIdx) minIdx = w;
                if (maxIdx == null || w > maxIdx) maxIdx = w;
            }

            double t1, t2;
            if (minIdx.HasValue && maxIdx.HasValue)
            {
                t1 = words[minIdx.Value].StartSec;
                t2 = words[maxIdx.Value].EndSec;
                lastWordIdx = maxIdx.Value;
            }
            else
            {
                var fallbackIdx = Math.Min(lastWordIdx + 1, words.Count - 1);
                t1 = words[fallbackIdx].StartSec;
                t2 = t1 + 0.5;
            }

            var content = (raw ?? string.Empty).Trim();
            result.Add(new SubtitleEntry(
                result.Count + 1,
                content,
                TimeSpan.FromSeconds(Math.Max(0.0, t1)),
                TimeSpan.FromSeconds(Math.Max(0.0, t2))));

            currentOffset += lineLen;
        }

        return result;
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (IsIgnored(ch)) continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
