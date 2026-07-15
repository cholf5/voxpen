using VoxPen.Core.Utils;

namespace VoxPen.Core.Transcribe;

/// <summary>
/// 分段拼接（音频切片场景）。端口自 Python <c>text_merger.merge_by_text</c> +
/// <c>token_merger.merge_tokens_by_sequence_matcher</c>。
///
/// 两条通路：
/// - <see cref="MergeText"/>：纯文本拼接（实时回显走这条，标点差异不敏感）
/// - <see cref="MergeTokens"/>：token+时间戳级别拼接（字幕生成走这条，需要 SRT 时间对齐）
///
/// 核心思路：在 prev 尾部和 new 头部找 <see cref="SequenceMatcher"/> 匹配块，
/// 加位置约束（匹配终点 &gt; tail 长度 3/4，匹配起点 ≤ head 长度 1/4），
/// 打分 <c>size² + a - b</c>（长度主导 + 越靠合流位置越加分），选最优对齐后剪接。
/// </summary>
public static class SegmentMerger
{
    /// <summary>与 Python <c>Punctuation.ALL</c> 保持一致。</summary>
    private static readonly HashSet<char> Punctuation = new(
        "，。！？；：、「」『』（）《》【】[]{},.!?;:\"'");

    /// <summary>标点 + 空格，用于清理连续重复。</summary>
    private static readonly HashSet<char> PunctOrSpace;

    static SegmentMerger()
    {
        PunctOrSpace = new HashSet<char>(Punctuation);
        PunctOrSpace.Add(' ');
    }

    // ----------------------------- 文本拼接 -----------------------------

    /// <summary>
    /// 基于文本重叠的稳定拼接。若找不到重叠块则直接串联。
    /// </summary>
    public static string MergeText(string? prev, string? next)
    {
        if (string.IsNullOrEmpty(prev)) return next ?? string.Empty;
        if (string.IsNullOrEmpty(next)) return prev;

        // 去掉 prev 尾部标点 / next 头部标点，避免标点差异干扰
        var prevClean = TrimEnd(prev, Punctuation);
        int newStart = 0;
        while (newStart < next.Length && Punctuation.Contains(next[newStart])) newStart++;
        var newClean = next[newStart..];

        if (prevClean.Length == 0 || newClean.Length == 0)
        {
            return prev + next;
        }

        var tail = prevClean.Length > 100 ? prevClean[^100..] : prevClean;
        var head = newClean.Length > 100 ? newClean[..100] : newClean;

        var best = FindBestOverlap(tail, head);
        if (best is null) return prev + next;

        var (matchA, matchB, matchLen) = best.Value;
        int keepPrevLen = prevClean.Length - tail.Length + matchA + matchLen;
        int skipNewLen = matchB + matchLen;

        var resPrev = prevClean[..keepPrevLen];
        var resNew = next[(newStart + skipNewLen)..];
        return resPrev + resNew;
    }

    // ----------------------------- Token 拼接 -----------------------------

    /// <summary>
    /// Token 拼接结果：合并后的 token 序列与全局时间戳。
    /// </summary>
    public readonly record struct TokenMergeResult(string[] Tokens, float[] Timestamps);

    /// <summary>
    /// 基于 SequenceMatcher 的 token 级拼接。<paramref name="newTimestamps"/> 是片段内相对时间，
    /// 内部会加上 <paramref name="offsetSeconds"/> 变成全局时间戳。
    /// </summary>
    /// <param name="prevTokens">已累积 token 序列（全局）。</param>
    /// <param name="prevTimestamps">已累积时间戳（全局，秒）。</param>
    /// <param name="newTokens">新片段 token。</param>
    /// <param name="newTimestamps">新片段相对时间戳（片段内，秒）。</param>
    /// <param name="offsetSeconds">当前片段的全局起始偏移（秒）。</param>
    /// <param name="overlapSeconds">相邻片段重叠时长（秒），用来估计 overlap 字符数窗口。</param>
    /// <param name="isFirstSegment">首片段：直接返回，不做重叠对齐。</param>
    public static TokenMergeResult MergeTokens(
        IReadOnlyList<string> prevTokens,
        IReadOnlyList<float> prevTimestamps,
        IReadOnlyList<string> newTokens,
        IReadOnlyList<float> newTimestamps,
        double offsetSeconds,
        double overlapSeconds,
        bool isFirstSegment)
    {
        var newGlobal = new float[newTimestamps.Count];
        for (int i = 0; i < newTimestamps.Count; i++)
        {
            newGlobal[i] = newTimestamps[i] + (float)offsetSeconds;
        }

        if (isFirstSegment || prevTokens.Count == 0)
        {
            return new TokenMergeResult(newTokens.ToArray(), newGlobal);
        }
        if (newTokens.Count == 0)
        {
            return new TokenMergeResult(prevTokens.ToArray(), prevTimestamps.ToArray());
        }

        // 估计 overlap 区域的字符数（约 5 字/秒），窗口再放宽 3 倍以覆盖抖动
        int overlapCharEst = Math.Max((int)(overlapSeconds * 5), 20);
        int prevTailLen = Math.Min(prevTokens.Count, overlapCharEst * 3);
        int newHeadLen = Math.Min(newTokens.Count, overlapCharEst * 3);

        var prevTailText = string.Concat(prevTokens.Skip(prevTokens.Count - prevTailLen));
        var newHeadText = string.Concat(newTokens.Take(newHeadLen));

        var best = FindBestOverlap(prevTailText, newHeadText);

        if (best is null)
        {
            return FallbackMerge(prevTokens, prevTimestamps, newTokens, newGlobal);
        }

        var (matchA, matchB, matchLen) = best.Value;

        int prevCut = CharPosToTokenIdx(
            prevTokens,
            baseOffset: prevTokens.Count - prevTailLen,
            charPos: matchA + matchLen);
        int newStart = CharPosToTokenIdx(
            newTokens,
            baseOffset: 0,
            charPos: matchB + matchLen);

        var resultTokens = new List<string>(prevCut + (newTokens.Count - newStart));
        var resultTs = new List<float>(prevCut + (newTokens.Count - newStart));

        for (int i = 0; i < prevCut; i++) resultTokens.Add(prevTokens[i]);
        for (int i = 0; i < prevCut; i++) resultTs.Add(prevTimestamps[i]);
        for (int i = newStart; i < newTokens.Count; i++) resultTokens.Add(newTokens[i]);
        for (int i = newStart; i < newTokens.Count; i++) resultTs.Add(newGlobal[i]);

        return CleanRepeatedPunct(resultTokens, resultTs);
    }

    // ----------------------------- 内部工具 -----------------------------

    /// <summary>
    /// 在 tail/head 中找最佳重叠块。返回 (a, b, size)，其中 a 是 tail 中起点、b 是 head 中起点。
    /// </summary>
    internal static (int A, int B, int Size)? FindBestOverlap(string tail, string head)
    {
        if (tail.Length == 0 || head.Length == 0) return null;

        const int MinMatch = 2;
        var blocks = SequenceMatcher.GetMatchingBlocks(tail, head, autoJunk: false);

        int tailEndThreshold = tail.Length / 4 * 3;
        int headStartThreshold = head.Length / 4;

        (int A, int B, int Size)? best = null;
        long bestScore = long.MinValue;

        foreach (var m in blocks)
        {
            if (m.Size < MinMatch) continue;
            if (m.A + m.Size <= tailEndThreshold) continue;
            if (m.B > headStartThreshold) continue;

            long score = (long)m.Size * m.Size + m.A - m.B;
            if (score > bestScore)
            {
                bestScore = score;
                best = (m.A, m.B, m.Size);
            }
        }
        return best;
    }

    /// <summary>
    /// 从 <paramref name="baseOffset"/> 开始累计 token 字符数，返回累计达到 <paramref name="charPos"/> 时的
    /// token 索引（全局）。若累计不到则返回 tokens.Count（表示"到末尾"）。
    /// </summary>
    internal static int CharPosToTokenIdx(IReadOnlyList<string> tokens, int baseOffset, int charPos)
    {
        int charCount = 0;
        for (int i = baseOffset; i < tokens.Count; i++)
        {
            if (charCount >= charPos) return i;
            charCount += tokens[i].Length;
        }
        return tokens.Count;
    }

    /// <summary>
    /// 兜底：找不到重叠时按时间戳排除已识别过的头部 token（超过 prev 最后时间戳 + 0.1s 才保留）。
    /// </summary>
    private static TokenMergeResult FallbackMerge(
        IReadOnlyList<string> prevTokens,
        IReadOnlyList<float> prevTimestamps,
        IReadOnlyList<string> newTokens,
        float[] newGlobal)
    {
        float lastTime = prevTimestamps.Count > 0 ? prevTimestamps[^1] : 0f;
        int newStartIdx = newTokens.Count;   // for-else 语义：默认全部跳过
        for (int i = 0; i < newGlobal.Length; i++)
        {
            if (newGlobal[i] > lastTime + 0.1f)
            {
                newStartIdx = i;
                break;
            }
        }

        var tokens = new List<string>(prevTokens.Count + (newTokens.Count - newStartIdx));
        var ts = new List<float>(prevTimestamps.Count + (newTokens.Count - newStartIdx));
        tokens.AddRange(prevTokens);
        ts.AddRange(prevTimestamps);
        for (int i = newStartIdx; i < newTokens.Count; i++) tokens.Add(newTokens[i]);
        for (int i = newStartIdx; i < newTokens.Count; i++) ts.Add(newGlobal[i]);
        return new TokenMergeResult(tokens.ToArray(), ts.ToArray());
    }

    /// <summary>清理连续重复的标点/空格 token。</summary>
    private static TokenMergeResult CleanRepeatedPunct(List<string> tokens, List<float> timestamps)
    {
        var outTokens = new List<string>(tokens.Count);
        var outTs = new List<float>(timestamps.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (outTokens.Count > 0
                && t.Length == 1
                && PunctOrSpace.Contains(t[0])
                && outTokens[^1] == t)
            {
                continue;
            }
            outTokens.Add(t);
            outTs.Add(timestamps[i]);
        }
        return new TokenMergeResult(outTokens.ToArray(), outTs.ToArray());
    }

    private static string TrimEnd(string s, HashSet<char> chars)
    {
        int end = s.Length;
        while (end > 0 && chars.Contains(s[end - 1])) end--;
        return end == s.Length ? s : s[..end];
    }
}
