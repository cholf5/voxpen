namespace VoxPen.Core.Hotword.Phoneme;

/// <summary>
/// 高性能 RAG 粗筛。端口自 Python <c>rag_fast.py</c>。
///
/// 结构：
/// - <see cref="PhonemeEncoder"/> 把 phoneme 字符串编码为 int，加速 DP 内层比较。
/// - <see cref="PhonemeIndex"/> 按热词前两个音素建倒排索引；查询时在输入音素的位置附近开锚点窗口。
/// - <see cref="FastRag"/> 编排：编码 → 拿候选 → 局部编辑距离打分 → 去重排序。
/// </summary>
public sealed class FastRag
{
    /// <summary>粗筛阈值（分数 &gt;= 该值才进入下一阶段）。</summary>
    public double Threshold { get; }

    private readonly PhonemeIndex _index;
    /// <summary>已添加的热词条数（同一 target 多别名会计多次）。</summary>
    public int HotwordCount { get; private set; }

    public FastRag(double threshold = 0.6)
    {
        Threshold = threshold;
        _index = new PhonemeIndex();
    }

    /// <summary>
    /// 批量添加热词。字典的 value 是同一 target 的多条音素序列（别名场景）。
    /// </summary>
    public void AddHotwords(IEnumerable<KeyValuePair<string, IReadOnlyList<IReadOnlyList<Phoneme>>>> hotwords)
    {
        foreach (var (hw, seqs) in hotwords)
        {
            foreach (var phonemes in seqs)
            {
                if (phonemes.Count == 0) continue;
                _index.Add(hw, phonemes);
                HotwordCount++;
            }
        }
    }

    /// <summary>
    /// 检索匹配。返回 [(hotword, score, endPos)]；<paramref name="topK"/> 为 0 表示不截断。
    /// </summary>
    public List<(string Hotword, double Score, int EndPos)> Search(
        IReadOnlyList<Phoneme> inputPhonemes,
        int topK = 10)
    {
        var results = new List<(string, double, int)>();
        if (inputPhonemes.Count == 0) return results;

        var inputCodes = _index.EncodeInput(inputPhonemes);
        var candidates = _index.GetCandidates(inputCodes);
        results = ScoreCandidates(inputCodes, candidates);
        results.Sort((a, b) => b.Item2.CompareTo(a.Item2));
        if (topK > 0 && results.Count > topK) results = results.GetRange(0, topK);
        return results;
    }

    private List<(string Hotword, double Score, int EndPos)> ScoreCandidates(
        int[] inputList,
        List<(string Hw, int[] HwCodes, int[] Anchors)> candidates)
    {
        var all = new List<(string Hw, double Score, int EndPos)>();
        int inputLen = inputList.Length;

        foreach (var (hw, hwList, anchors) in candidates)
        {
            int hwLen = hwList.Length;
            foreach (var anchor in anchors)
            {
                int scanStart = Math.Max(0, anchor - 2);
                int scanEnd = Math.Min(inputLen, anchor + hwLen + 3);
                if (scanEnd <= scanStart) continue;

                var localInput = new int[scanEnd - scanStart];
                Array.Copy(inputList, scanStart, localInput, 0, localInput.Length);

                var (dist, localEnd) = LocalEditDistance(localInput, hwList);
                double score = 1.0 - (dist / (double)hwLen);
                if (score >= Threshold)
                {
                    int endPos = scanStart + localEnd;
                    all.Add((hw, Math.Round(score, 3), endPos));
                }
            }
        }

        // 同 (hw, endPos) 去重，取最高分
        var final = new Dictionary<(string, int), (double Score, int EndPos)>();
        foreach (var (hw, score, endPos) in all)
        {
            var key = (hw, endPos);
            if (!final.TryGetValue(key, out var existing) || score > existing.Score)
            {
                final[key] = (score, endPos);
            }
        }
        var res = new List<(string, double, int)>(final.Count);
        foreach (var kv in final) res.Add((kv.Key.Item1, kv.Value.Score, kv.Value.EndPos));
        return res;
    }

    /// <summary>
    /// 局部编辑距离 DP，端口自 Python <c>_python_distance_simple</c>。
    /// 返回 (最小距离, 距离最小时对应的 <paramref name="main"/> 长度切分点)。
    /// </summary>
    internal static (double Dist, int EndPos) LocalEditDistance(int[] main, int[] sub)
    {
        int n = sub.Length;
        int m = main.Length;
        if (n == 0) return (0, 0);
        if (m == 0) return (n, 0);

        var prev = new double[n + 1];
        var curr = new double[n + 1];
        for (int i = 0; i <= n; i++) prev[i] = i;

        double bestDist = double.PositiveInfinity;
        int bestPos = 0;

        for (int j = 1; j <= m; j++)
        {
            curr[0] = j;
            int mVal = main[j - 1];
            for (int i = 1; i <= n; i++)
            {
                double cost = sub[i - 1] == mVal ? 0.0 : 1.0;
                double dDel = prev[i] + 1.0;
                double dIns = curr[i - 1] + 1.0;
                double dMatch = prev[i - 1] + cost;

                double best;
                if (dDel < dIns) best = dDel < dMatch ? dDel : dMatch;
                else best = dIns < dMatch ? dIns : dMatch;
                curr[i] = best;
            }

            if (curr[n] <= bestDist)
            {
                bestDist = curr[n];
                bestPos = j;
            }
            (prev, curr) = (curr, prev);
        }
        return (bestDist, bestPos);
    }
}

/// <summary>音素字符串 → int 编码。首次遇到的音素分配递增 ID（0 保留）。</summary>
internal sealed class PhonemeEncoder
{
    private readonly Dictionary<string, int> _map = new();
    private int _next = 1;
    private Dictionary<int, List<int>>? _simMap;

    public int Encode(string phoneme)
    {
        if (_map.TryGetValue(phoneme, out var code)) return code;
        code = _next++;
        _map[phoneme] = code;
        _simMap = null;   // 编码扩容后相似映射失效
        return code;
    }

    public int[] EncodeSequence(IReadOnlyList<string> phonemes)
    {
        var codes = new int[phonemes.Count];
        for (int i = 0; i < phonemes.Count; i++) codes[i] = Encode(phonemes[i]);
        return codes;
    }

    /// <summary>获取相似音素编码列表（延迟构建）。</summary>
    public IReadOnlyList<int> GetSimilarCodes(int code)
    {
        if (_simMap is null) BuildSimMap();
        return _simMap!.TryGetValue(code, out var list) ? list : Array.Empty<int>();
    }

    private void BuildSimMap()
    {
        _simMap = new Dictionary<int, List<int>>();
        foreach (var set in PhonemeSimilarity.SimilarPhonemeSets)
        {
            var codes = new List<int>();
            foreach (var p in set)
            {
                if (_map.TryGetValue(p, out var c)) codes.Add(c);
            }
            for (int i = 0; i < codes.Count; i++)
            {
                for (int j = 0; j < codes.Count; j++)
                {
                    if (i == j) continue;
                    if (!_simMap.TryGetValue(codes[i], out var list))
                    {
                        list = new List<int>();
                        _simMap[codes[i]] = list;
                    }
                    list.Add(codes[j]);
                }
            }
        }
    }
}

/// <summary>音素前两位倒排索引。</summary>
internal sealed class PhonemeIndex
{
    private readonly PhonemeEncoder _encoder = new();
    private readonly Dictionary<int, List<(string Hw, int[] Codes)>> _index = new();

    public void Add(string hotword, IReadOnlyList<Phoneme> phonemes)
    {
        var codes = new int[phonemes.Count];
        for (int i = 0; i < phonemes.Count; i++)
        {
            codes[i] = _encoder.Encode(phonemes[i].Value);
        }

        int limit = Math.Min(codes.Length, 2);
        var seen = new HashSet<int>();
        for (int i = 0; i < limit; i++) seen.Add(codes[i]);

        foreach (var code in seen)
        {
            if (!_index.TryGetValue(code, out var list))
            {
                list = new List<(string, int[])>();
                _index[code] = list;
            }
            list.Add((hotword, codes));
        }
    }

    public int[] EncodeInput(IReadOnlyList<Phoneme> phonemes)
    {
        var codes = new int[phonemes.Count];
        for (int i = 0; i < phonemes.Count; i++) codes[i] = _encoder.Encode(phonemes[i].Value);
        return codes;
    }

    /// <summary>返回候选热词及其在输入中的锚点位置（去重升序）。</summary>
    public List<(string Hw, int[] HwCodes, int[] Anchors)> GetCandidates(int[] inputCodes)
    {
        // {code: [positions]}，同时把相似音素的命中并入
        var codePositions = new Dictionary<int, List<int>>();
        for (int idx = 0; idx < inputCodes.Length; idx++)
        {
            int c = inputCodes[idx];
            if (!codePositions.TryGetValue(c, out var list))
            {
                list = new List<int>();
                codePositions[c] = list;
            }
            list.Add(idx);
            foreach (var sim in _encoder.GetSimilarCodes(c))
            {
                if (!codePositions.TryGetValue(sim, out var simList))
                {
                    simList = new List<int>();
                    codePositions[sim] = simList;
                }
                simList.Add(idx);
            }
        }

        // 用 (hw, codes 序列身份) 作为 key，避免同一热词多别名互相覆盖
        var candidateData = new Dictionary<(string, int), (string Hw, int[] Codes, List<int> Positions)>();
        foreach (var (code, positions) in codePositions)
        {
            if (!_index.TryGetValue(code, out var entries)) continue;
            foreach (var (hw, codes) in entries)
            {
                var key = (hw, RuntimeHelpers.GetHashCode(codes));   // 数组引用哈希
                if (!candidateData.TryGetValue(key, out var entry))
                {
                    entry = (hw, codes, new List<int>());
                    candidateData[key] = entry;
                }
                entry.Positions.AddRange(positions);
            }
        }

        var result = new List<(string, int[], int[])>(candidateData.Count);
        foreach (var (_, entry) in candidateData)
        {
            var uniq = new HashSet<int>(entry.Positions);
            var arr = new int[uniq.Count];
            uniq.CopyTo(arr);
            Array.Sort(arr);
            result.Add((entry.Hw, entry.Codes, arr));
        }
        return result;
    }

    // 为避免引入 System.Runtime.CompilerServices 命名空间冲突，用一个静态别名
    private static class RuntimeHelpers
    {
        public static int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }
}
