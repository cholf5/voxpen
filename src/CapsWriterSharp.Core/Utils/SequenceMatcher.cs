namespace CapsWriterSharp.Core.Utils;

/// <summary>
/// Python <c>difflib.SequenceMatcher</c>（Ratcliff-Obershelp）的最小端口。
/// 仅暴露 CapsWriter 需要的两个 API：<see cref="FindLongestMatch"/> 与 <see cref="GetMatchingBlocks"/>。
///
/// 算法要点（对齐 CPython Lib/difflib.py）：
/// - <c>b2j</c>：b 中每个字符出现的位置列表
/// - <c>autojunk</c>：n ≥ 200 时把 b 中出现次数 &gt; n/100+1 的字符视作"过分常见"，
///   从初次索引里剔除；扩展阶段仍能把它们纳入两侧连续等值扩展
/// - <see cref="FindLongestMatch"/>：滑动 j2len 字典，找最长公共子串
/// - <see cref="GetMatchingBlocks"/>：递归分治，最后合并相邻等长块，末尾追加 (la, lb, 0) 哨兵
///
/// 只支持 <see cref="char"/> 序列——CapsWriter 的两个调用点（SegmentMerger、SubtitleAligner）都工作在字符串上。
/// </summary>
public sealed class SequenceMatcher
{
    /// <summary>一个匹配块：a[<see cref="A"/>..<see cref="A"/>+<see cref="Size"/>) == b[<see cref="B"/>..<see cref="B"/>+<see cref="Size"/>)。</summary>
    public readonly record struct Match(int A, int B, int Size);

    private readonly string _a;
    private readonly string _b;
    private readonly Dictionary<char, List<int>> _b2j;
    // bJunk：用户显式指定的 junk 字符集合。当前未接入 API，永远为空。
    private readonly HashSet<char> _bJunk = new();
    // bPopular：autojunk 判定"过分常见"的字符集合，仅用于统计；实际剔除已在 _b2j 中完成。
    private readonly HashSet<char> _bPopular = new();

    public SequenceMatcher(string? a, string? b, bool autoJunk = true)
    {
        _a = a ?? string.Empty;
        _b = b ?? string.Empty;
        _b2j = new Dictionary<char, List<int>>(_b.Length);
        ChainB(autoJunk);
    }

    private void ChainB(bool autoJunk)
    {
        for (int i = 0; i < _b.Length; i++)
        {
            var ch = _b[i];
            if (!_b2j.TryGetValue(ch, out var list))
            {
                list = new List<int>();
                _b2j[ch] = list;
            }
            list.Add(i);
        }

        int n = _b.Length;
        if (autoJunk && n >= 200)
        {
            int nTest = n / 100 + 1;
            foreach (var kv in _b2j)
            {
                if (kv.Value.Count > nTest)
                {
                    _bPopular.Add(kv.Key);
                }
            }
            foreach (var elt in _bPopular)
            {
                _b2j.Remove(elt);
            }
        }
    }

    /// <summary>
    /// 找 a[aLo..aHi) 与 b[bLo..bHi) 的最长公共子串。若无匹配返回 (aLo, bLo, 0)。
    /// </summary>
    public Match FindLongestMatch(int aLo, int aHi, int bLo, int bHi)
    {
        if (aLo < 0 || bLo < 0 || aHi > _a.Length || bHi > _b.Length || aLo > aHi || bLo > bHi)
        {
            throw new ArgumentOutOfRangeException(nameof(aLo));
        }

        int bestI = aLo, bestJ = bLo, bestSize = 0;

        // j2len[j] = 以 a[i-1] 结尾、b[j-1] 结尾的最长公共子串长度（滚动更新）
        var j2len = new Dictionary<int, int>();
        for (int i = aLo; i < aHi; i++)
        {
            var newJ2len = new Dictionary<int, int>();
            if (_b2j.TryGetValue(_a[i], out var positions))
            {
                foreach (var j in positions)
                {
                    if (j < bLo) continue;
                    if (j >= bHi) break;   // 索引已升序
                    int k = (j2len.TryGetValue(j - 1, out var prev) ? prev : 0) + 1;
                    newJ2len[j] = k;
                    if (k > bestSize)
                    {
                        bestI = i - k + 1;
                        bestJ = j - k + 1;
                        bestSize = k;
                    }
                }
            }
            j2len = newJ2len;
        }

        // 阶段 2：沿两侧扩展连续等值字符（跳过 junk）——这一步能把 popular（被 autojunk 剔除的）字符拉回来
        while (bestI > aLo && bestJ > bLo
               && !_bJunk.Contains(_b[bestJ - 1])
               && _a[bestI - 1] == _b[bestJ - 1])
        {
            bestI--; bestJ--; bestSize++;
        }
        while (bestI + bestSize < aHi && bestJ + bestSize < bHi
               && !_bJunk.Contains(_b[bestJ + bestSize])
               && _a[bestI + bestSize] == _b[bestJ + bestSize])
        {
            bestSize++;
        }

        // 阶段 3：再沿两侧扩展 junk 等值字符（仅在用户配置了 isjunk 时才生效）
        while (bestI > aLo && bestJ > bLo
               && _bJunk.Contains(_b[bestJ - 1])
               && _a[bestI - 1] == _b[bestJ - 1])
        {
            bestI--; bestJ--; bestSize++;
        }
        while (bestI + bestSize < aHi && bestJ + bestSize < bHi
               && _bJunk.Contains(_b[bestJ + bestSize])
               && _a[bestI + bestSize] == _b[bestJ + bestSize])
        {
            bestSize++;
        }

        return new Match(bestI, bestJ, bestSize);
    }

    /// <summary>
    /// 得到所有匹配块（按 A 升序、B 次升序），末尾追加 <c>(la, lb, 0)</c> 哨兵。
    /// 相邻可拼成一整块的匹配会被合并（difflib 语义）。
    /// </summary>
    public IReadOnlyList<Match> GetMatchingBlocks()
    {
        int la = _a.Length, lb = _b.Length;
        var stack = new Stack<(int aLo, int aHi, int bLo, int bHi)>();
        stack.Push((0, la, 0, lb));

        var blocks = new List<Match>();
        while (stack.Count > 0)
        {
            var (aLo, aHi, bLo, bHi) = stack.Pop();
            var m = FindLongestMatch(aLo, aHi, bLo, bHi);
            if (m.Size > 0)
            {
                blocks.Add(m);
                if (aLo < m.A && bLo < m.B)
                {
                    stack.Push((aLo, m.A, bLo, m.B));
                }
                if (m.A + m.Size < aHi && m.B + m.Size < bHi)
                {
                    stack.Push((m.A + m.Size, aHi, m.B + m.Size, bHi));
                }
            }
        }

        blocks.Sort((x, y) =>
        {
            var c = x.A.CompareTo(y.A);
            return c != 0 ? c : x.B.CompareTo(y.B);
        });

        // 合并相邻块
        int i1 = 0, j1 = 0, k1 = 0;
        var nonAdjacent = new List<Match>();
        foreach (var m in blocks)
        {
            if (i1 + k1 == m.A && j1 + k1 == m.B)
            {
                k1 += m.Size;
            }
            else
            {
                if (k1 > 0) nonAdjacent.Add(new Match(i1, j1, k1));
                (i1, j1, k1) = (m.A, m.B, m.Size);
            }
        }
        if (k1 > 0) nonAdjacent.Add(new Match(i1, j1, k1));
        nonAdjacent.Add(new Match(la, lb, 0));   // sentinel
        return nonAdjacent;
    }

    /// <summary>便捷入口：直接返回两串的 matching blocks（含末尾哨兵）。</summary>
    public static IReadOnlyList<Match> GetMatchingBlocks(string a, string b, bool autoJunk = true)
        => new SequenceMatcher(a, b, autoJunk).GetMatchingBlocks();
}
