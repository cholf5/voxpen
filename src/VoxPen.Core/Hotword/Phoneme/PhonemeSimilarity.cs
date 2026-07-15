namespace VoxPen.Core.Hotword.Phoneme;

/// <summary>
/// 音素相似度与代价计算。端口自 Python <c>algo_calc</c>。
///
/// - <see cref="SimilarPhonemeSets"/>：普通话易混音素集合（前后鼻音、平翘舌、l/n、f/h 等）。
/// - <see cref="LcsLength"/>：滚动数组最长公共子序列（英文 token 相似度）。
/// - <see cref="PhonemeCost"/>：0 完全匹配 / 0.5 相似音素或声调不同 / 1 完全不匹配。
/// </summary>
public static class PhonemeSimilarity
{
    /// <summary>易混音素集合（每组内两两互认为 0.5 代价）。</summary>
    public static readonly IReadOnlyList<IReadOnlySet<string>> SimilarPhonemeSets = new IReadOnlySet<string>[]
    {
        // 前后鼻音
        new HashSet<string>{ "an", "ang" },
        new HashSet<string>{ "en", "eng" },
        new HashSet<string>{ "in", "ing" },
        new HashSet<string>{ "ian", "iang" },
        new HashSet<string>{ "uan", "uang" },
        // 平翘舌
        new HashSet<string>{ "z", "zh" },
        new HashSet<string>{ "c", "ch" },
        new HashSet<string>{ "s", "sh" },
        // 鼻音/边音
        new HashSet<string>{ "l", "n" },
        // 唇齿/声门
        new HashSet<string>{ "f", "h" },
        // 常见易混韵母
        new HashSet<string>{ "ai", "ei" },
        new HashSet<string>{ "o", "uo" },
        new HashSet<string>{ "e", "ie" },
        // 送气/清浊
        new HashSet<string>{ "p", "t" },
        new HashSet<string>{ "p", "b" },
        new HashSet<string>{ "t", "d" },
        new HashSet<string>{ "k", "g" },
    };

    /// <summary>两个音素是否属于同一相似集合。</summary>
    public static bool IsSimilar(string a, string b)
    {
        if (a == b) return false;
        foreach (var set in SimilarPhonemeSets)
        {
            if (set.Contains(a) && set.Contains(b)) return true;
        }
        return false;
    }

    /// <summary>最长公共子序列长度（滚动数组 O(min(m,n)) 空间）。</summary>
    public static int LcsLength(string s1, string s2)
    {
        if (s1.Length < s2.Length) (s1, s2) = (s2, s1);
        int m = s1.Length, n = s2.Length;
        if (n == 0) return 0;

        var prev = new int[n + 1];
        var curr = new int[n + 1];
        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                if (s1[i - 1] == s2[j - 1]) curr[j] = prev[j - 1] + 1;
                else curr[j] = Math.Max(prev[j], curr[j - 1]);
            }
            (prev, curr) = (curr, prev);
        }
        return prev[n];
    }

    /// <summary>音素对齐代价：0=同 / 0.5=中文相似音素 / 英文按 LCS 归一 / 其他为 1。</summary>
    public static float PhonemeCost(Phoneme p1, Phoneme p2)
    {
        if (p1.Lang != p2.Lang) return 1f;
        if (p1.Value == p2.Value) return 0f;

        if (p1.Lang == PhonemeLang.Zh && IsSimilar(p1.Value, p2.Value)) return 0.5f;
        if (p1.Lang == PhonemeLang.En)
        {
            int lcs = LcsLength(p1.Value, p2.Value);
            int max = Math.Max(p1.Value.Length, p2.Value.Length);
            return 1f - (float)lcs / max;
        }
        return 1f;
    }
}
