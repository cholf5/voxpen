using System.Text.RegularExpressions;

namespace CapsWriterSharp.Core.Hotword.Phoneme;

/// <summary>纠错结果。</summary>
/// <param name="Text">替换后的文本。</param>
/// <param name="Matches">已执行替换的 [(原词, 热词, 分数)] 列表。</param>
/// <param name="Similars">相似但未替换的 [(原词, 热词, 分数)] 列表，供 UI 提示。</param>
public sealed record CorrectionResult(
    string Text,
    IReadOnlyList<(string Origin, string Hotword, double Score)> Matches,
    IReadOnlyList<(string Origin, string Hotword, double Score)> Similars);

/// <summary>
/// 音素 RAG 拼音纠错器。端口自 Python <c>hot_phoneme.PhonemeCorrector</c>。
///
/// 两阶段：
/// 1. <see cref="FastRag"/> 粗筛，锁定候选热词与大致位置。
/// 2. 在锚点附近开窗调用 <see cref="ConstrainedSearch"/> 精筛拿到具体字符边界。
/// 冲突解决按（分数, 长度）优先，右到左 splice 替换以避免破坏后续索引。
/// </summary>
public sealed class PhonemeCorrector
{
    private static readonly Regex SemanticTokenRegex = new(
        @"[\u4e00-\u9fa5]|[a-zA-Z]+|[0-9]+",
        RegexOptions.Compiled);

    public double Threshold { get; }
    public double SimilarThreshold { get; }
    private readonly PhonemeExtractor _extractor;

    private IReadOnlyList<HotwordEntry> _hotwords = Array.Empty<HotwordEntry>();
    private FastRag _fastRag;
    private readonly object _lock = new();

    public int HotwordCount => _hotwords.Count;

    public PhonemeCorrector(
        double threshold = 0.85,
        double? similarThreshold = null,
        PhonemeExtractor? extractor = null)
    {
        Threshold = threshold;
        SimilarThreshold = similarThreshold ?? Math.Max(0.0, threshold - 0.2);
        _extractor = extractor ?? PhonemeExtractor.Default;
        _fastRag = new FastRag(threshold: Math.Min(Threshold, SimilarThreshold) - 0.1);
    }

    /// <summary>加载新的热词集合（线程安全）。返回已加载数量。</summary>
    public int UpdateHotwords(IReadOnlyList<HotwordEntry> hotwords)
    {
        var newRag = new FastRag(threshold: Math.Min(Threshold, SimilarThreshold) - 0.1);
        var dict = new List<KeyValuePair<string, IReadOnlyList<IReadOnlyList<Phoneme>>>>(hotwords.Count);
        foreach (var e in hotwords)
        {
            dict.Add(new(e.Target, e.PhonemeLists));
        }
        newRag.AddHotwords(dict);

        lock (_lock)
        {
            _hotwords = hotwords;
            _fastRag = newRag;
        }
        return hotwords.Count;
    }

    /// <summary>直接从 hot.txt 内容加载。</summary>
    public int UpdateHotwordsFromText(string content)
        => UpdateHotwords(HotwordFile.Parse(content, _extractor));

    /// <summary>执行纠错替换。空输入或无热词直接原样返回。</summary>
    public CorrectionResult Correct(string text, int topK = 10, int blacklistWindow = 5)
    {
        if (string.IsNullOrEmpty(text) || _hotwords.Count == 0)
        {
            return new CorrectionResult(text ?? string.Empty, Array.Empty<(string, string, double)>(), Array.Empty<(string, string, double)>());
        }

        var inputPhonemes = _extractor.Extract(text);
        if (inputPhonemes.Count == 0)
        {
            return new CorrectionResult(text, Array.Empty<(string, string, double)>(), Array.Empty<(string, string, double)>());
        }

        IReadOnlyList<HotwordEntry> hotwordsSnapshot;
        FastRag ragSnapshot;
        lock (_lock)
        {
            hotwordsSnapshot = _hotwords;
            ragSnapshot = _fastRag;
        }

        var fastResults = ragSnapshot.Search(inputPhonemes, topK: 0);
        var (matches, similars) = FindMatches(text, fastResults, inputPhonemes, hotwordsSnapshot);
        var (newText, finalMatches, _) = ResolveAndReplace(text, matches, hotwordsSnapshot, blacklistWindow);

        // similars 按分数降序、同分按热词长度降序、去重取前 topK
        similars.Sort((a, b) =>
        {
            int c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : b.Hotword.Length.CompareTo(a.Hotword.Length);
        });
        var uniqSimilars = new List<(string Origin, string Hotword, double Score)>();
        var seen = new HashSet<string>();
        foreach (var s in similars)
        {
            if (seen.Add(s.Hotword)) uniqSimilars.Add(s);
        }
        if (uniqSimilars.Count > topK) uniqSimilars = uniqSimilars.GetRange(0, topK);

        return new CorrectionResult(newText, finalMatches, uniqSimilars);
    }

    // ----------------------- 精细匹配 -----------------------

    private (List<MatchInfo> Matches, List<(string Origin, string Hotword, double Score)> Similars) FindMatches(
        string text,
        List<(string Hotword, double Score, int EndPos)> fastResults,
        IReadOnlyList<Phoneme> inputPhonemes,
        IReadOnlyList<HotwordEntry> hotwords)
    {
        var matches = new List<MatchInfo>();
        var similars = new List<(string Origin, string Hotword, double Score)>();

        // 同 hotword 的多个位置去重（距离 < 5 视为同一处）
        var seenPositions = new Dictionary<string, List<int>>();
        foreach (var (hw, _, endIdx) in fastResults)
        {
            if (!seenPositions.TryGetValue(hw, out var list))
            {
                list = new List<int>();
                seenPositions[hw] = list;
            }
            bool near = false;
            foreach (var p in list) { if (Math.Abs(endIdx - p) < 5) { near = true; break; } }
            if (!near) list.Add(endIdx);
        }

        var hwByTarget = hotwords.ToDictionary(h => h.Target, h => h);
        double searchThreshold = Math.Min(Threshold, SimilarThreshold) - 0.1;

        foreach (var (hw, endIndices) in seenPositions)
        {
            if (!hwByTarget.TryGetValue(hw, out var entry)) continue;

            foreach (var approxEnd in endIndices)
            {
                foreach (var hwPhonemes in entry.PhonemeLists)
                {
                    int windowSize = hwPhonemes.Count + 10;
                    int windowStart = Math.Max(0, approxEnd - windowSize);
                    int windowEnd = Math.Min(inputPhonemes.Count, approxEnd + 5);
                    if (windowEnd <= windowStart) continue;

                    var localInput = new List<Phoneme>(windowEnd - windowStart);
                    for (int i = windowStart; i < windowEnd; i++) localInput.Add(inputPhonemes[i]);

                    var found = ConstrainedSearch.FindMatches(hwPhonemes, localInput, searchThreshold);
                    foreach (var (score, startIdx, endIdx) in found)
                    {
                        int globalStart = windowStart + startIdx;
                        int globalEnd = windowStart + endIdx;
                        if (globalStart < 0 || globalEnd - 1 >= inputPhonemes.Count) continue;

                        int charStart = inputPhonemes[globalStart].CharStart;
                        int charEnd = inputPhonemes[globalEnd - 1].CharEnd;
                        if (charEnd <= charStart) continue;

                        var origin = text[charStart..charEnd];
                        if (score >= Threshold)
                        {
                            matches.Add(new MatchInfo(charStart, charEnd, score, hw));
                        }
                        if (score >= SimilarThreshold)
                        {
                            similars.Add((origin, hw, score));
                        }
                    }
                }
            }
        }

        return (matches, similars);
    }

    // ----------------------- 冲突解决 & 替换 -----------------------

    private (string NewText,
             List<(string Origin, string Hotword, double Score)> Matches,
             List<(string Hotword, double Score)> AllInfo)
    ResolveAndReplace(
        string text,
        List<MatchInfo> matches,
        IReadOnlyList<HotwordEntry> hotwords,
        int window)
    {
        // 分数优先 > 长度优先
        matches.Sort((a, b) =>
        {
            int c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            return (b.End - b.Start).CompareTo(a.End - a.Start);
        });

        var blByTarget = hotwords.ToDictionary(h => h.Target, h => h.Blacklist);
        var tokens = TokenizeSemantic(text);

        var finalMatches = new List<MatchInfo>();
        var occupied = new List<(int Start, int End)>();
        var allInfo = new List<(string Hotword, double Score)>();
        var seenHwScore = new HashSet<(string, double)>();

        foreach (var m in matches)
        {
            if (seenHwScore.Add((m.Hotword, m.Score)))
            {
                allInfo.Add((m.Hotword, m.Score));
            }
            if (m.Score < Threshold) continue;
            if (IsBlacklisted(text, m, tokens, blByTarget, window)) continue;

            bool overlap = false;
            foreach (var (s, e) in occupied)
            {
                if (!(m.End <= s || m.Start >= e)) { overlap = true; break; }
            }
            if (overlap) continue;

            occupied.Add((m.Start, m.End));

            var origin = text[m.Start..m.End];
            if (origin != m.Hotword)
            {
                finalMatches.Add(m);
            }
        }

        // 右到左 splice
        finalMatches.Sort((a, b) => b.Start.CompareTo(a.Start));
        var sb = new System.Text.StringBuilder(text);
        var reportMatches = new List<(string Origin, string Hotword, double Score)>(finalMatches.Count);
        foreach (var m in finalMatches)
        {
            var origin = sb.ToString().Substring(m.Start, m.End - m.Start);
            sb.Remove(m.Start, m.End - m.Start);
            sb.Insert(m.Start, m.Hotword);
            reportMatches.Add((origin, m.Hotword, m.Score));
        }
        // finalMatches 是右到左的顺序，导致 reportMatches 也是右到左；恢复左到右方便展示
        reportMatches.Reverse();

        return (sb.ToString(), reportMatches, allInfo);
    }

    private static bool IsBlacklisted(
        string text,
        MatchInfo m,
        List<(string Val, int Start, int End)> tokens,
        Dictionary<string, IReadOnlySet<string>> blByTarget,
        int window)
    {
        if (!blByTarget.TryGetValue(m.Hotword, out var bl) || bl.Count == 0) return false;

        // 找到与 [m.Start, m.End) 有交集的 token 范围
        var matched = new List<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            var (_, ts, te) = tokens[i];
            if (!(te <= m.Start || ts >= m.End)) matched.Add(i);
        }

        int winStart, winEnd;
        if (matched.Count > 0)
        {
            int lo = matched[0], hi = matched[^1];
            int wLo = Math.Max(0, lo - window);
            int wHi = Math.Min(tokens.Count, hi + 1 + window);
            winStart = tokens[wLo].Start;
            winEnd = tokens[wHi - 1].End;
        }
        else
        {
            winStart = Math.Max(0, m.Start - window);
            winEnd = Math.Min(text.Length, m.End + window);
        }

        var ctx = text[winStart..winEnd].ToLowerInvariant();
        foreach (var b in bl)
        {
            if (ctx.Contains(b.ToLowerInvariant(), StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static List<(string Val, int Start, int End)> TokenizeSemantic(string text)
    {
        var list = new List<(string, int, int)>();
        foreach (Match m in SemanticTokenRegex.Matches(text))
        {
            list.Add((m.Value, m.Index, m.Index + m.Length));
        }
        return list;
    }

    internal readonly record struct MatchInfo(int Start, int End, double Score, string Hotword);
}
