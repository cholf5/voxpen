namespace VoxPen.Core.Hotword.Phoneme;

/// <summary>
/// 精细匹配：在指定音素窗口内寻找热词的最佳对齐片段（起始必须是词起始、结束必须是词结束）。
/// 端口自 Python <c>algo_calc.fuzzy_substring_search_constrained</c>。
/// </summary>
internal static class ConstrainedSearch
{
    /// <summary>
    /// 返回一批匹配 [(score, startIdx, endIdx)]，按分数降序、按结束位置去重。
    /// </summary>
    public static List<(double Score, int Start, int End)> FindMatches(
        IReadOnlyList<Phoneme> hw,
        IReadOnlyList<Phoneme> input,
        double threshold = 0.6)
    {
        int n = hw.Count;
        int m = input.Count;
        if (n == 0 || m == 0) return new();

        // dp[i,j] 用一维展开：dp[i * (m+1) + j]
        var dp = new double[(n + 1) * (m + 1)];
        var pathStart = new int[(n + 1) * (m + 1)];   // 记录起点 j
        for (int k = 0; k < dp.Length; k++) dp[k] = double.PositiveInfinity;

        // 初始化：允许在输入的任意 word-start 位置起匹配
        for (int j = 0; j <= m; j++)
        {
            if (j == 0 || (j < m && input[j].IsWordStart))
            {
                dp[0 * (m + 1) + j] = 0;
                pathStart[0 * (m + 1) + j] = j;
            }
        }

        double earlyStop = n * (1.0 - threshold) + 2;
        for (int i = 1; i <= n; i++)
        {
            double rowMin = double.PositiveInfinity;
            var hp = hw[i - 1];
            for (int j = 1; j <= m; j++)
            {
                var ip = input[j - 1];
                double cost;
                if (hp.Lang != ip.Lang) cost = 1.0;
                else if (hp.Value == ip.Value) cost = 0.0;
                else if (hp.Lang == PhonemeLang.Zh)
                {
                    if (hp.IsTone) cost = 0.5;   // 声调不同 → 0.5（原版）
                    else if (PhonemeSimilarity.IsSimilar(hp.Value, ip.Value)) cost = 0.5;
                    else cost = 1.0;
                }
                else if (hp.Lang == PhonemeLang.En)
                {
                    int lcs = PhonemeSimilarity.LcsLength(hp.Value, ip.Value);
                    cost = 1.0 - (double)lcs / Math.Max(hp.Value.Length, ip.Value.Length);
                }
                else cost = 1.0;

                double match = dp[(i - 1) * (m + 1) + (j - 1)] + cost;
                double del = dp[(i - 1) * (m + 1) + j] + 1.0;
                double ins = dp[i * (m + 1) + (j - 1)] + 1.0;

                double chosen;
                int start;
                if (match <= del && match <= ins)
                {
                    chosen = match;
                    start = pathStart[(i - 1) * (m + 1) + (j - 1)];
                }
                else if (del <= ins)
                {
                    chosen = del;
                    start = pathStart[(i - 1) * (m + 1) + j];
                }
                else
                {
                    chosen = ins;
                    start = pathStart[i * (m + 1) + (j - 1)];
                }

                dp[i * (m + 1) + j] = chosen;
                pathStart[i * (m + 1) + j] = start;
                if (chosen < rowMin) rowMin = chosen;
            }
            if (rowMin > earlyStop) break;
        }

        var raw = new List<(double Score, int Start, int End)>();
        for (int j = 1; j <= m; j++)
        {
            if (!input[j - 1].IsWordEnd) continue;
            double dist = dp[n * (m + 1) + j];
            if (double.IsInfinity(dist)) continue;
            if (dist >= n * 0.8) continue;
            double score = 1.0 - dist / n;
            if (score >= threshold)
            {
                raw.Add((score, pathStart[n * (m + 1) + j], j));
            }
        }

        raw.Sort((a, b) => b.Score.CompareTo(a.Score));

        // 按 end 去重，保留最高分
        var byEnd = new Dictionary<int, (double Score, int Start, int End)>();
        foreach (var r in raw)
        {
            if (!byEnd.TryGetValue(r.End, out var existing) || r.Score > existing.Score)
            {
                byEnd[r.End] = r;
            }
        }
        var final = byEnd.Values.ToList();
        final.Sort((a, b) => b.Score.CompareTo(a.Score));
        return final;
    }
}
