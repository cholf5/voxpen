using System.Text.RegularExpressions;

namespace VoxPen.Core.Hotword;

/// <summary>
/// 兼容原项目 hot-rule.txt 的替换器。
///
/// 文件格式：
///   - 以 '#' 开头的行为注释
///   - 空行忽略
///   - 每条规则以第一个 '=' 分隔左右两侧
///   - 左侧 strip 后当作正则；正则编译失败则退化为字面量匹配（用 Regex.Escape）
///   - 右侧 strip 后支持 \1..\n 反向引用（.NET Regex 原生支持）
///     以及 \s 表示空格（通过 Regex.Unescape 处理）
///
/// 用法：一次性 <see cref="Load"/> 拿到 replacer，然后线程安全地 <see cref="Apply"/>。
/// </summary>
public sealed class HotRuleReplacer
{
    private readonly IReadOnlyList<Rule> _rules;

    private HotRuleReplacer(IReadOnlyList<Rule> rules)
    {
        _rules = rules;
    }

    public int RuleCount => _rules.Count;

    /// <summary>空 replacer：Apply 直接返回原文本。</summary>
    public static HotRuleReplacer Empty { get; } = new(Array.Empty<Rule>());

    /// <summary>
    /// 加载 hot-rule.txt。文件不存在或读取失败时返回 <see cref="Empty"/>，不抛。
    /// </summary>
    public static HotRuleReplacer Load(string path)
    {
        if (!File.Exists(path)) return Empty;
        try
        {
            var lines = File.ReadAllLines(path);
            return Parse(lines);
        }
        catch
        {
            return Empty;
        }
    }

    /// <summary>从内存中的行序列解析规则。</summary>
    public static HotRuleReplacer Parse(IEnumerable<string> lines)
    {
        var list = new List<Rule>();
        foreach (var raw in lines)
        {
            if (raw is null) continue;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var lhs = line[..eq].Trim();
            var rhs = line[(eq + 1)..].Trim();
            if (lhs.Length == 0) continue;

            var rule = TryBuildRule(lhs, rhs);
            if (rule is not null) list.Add(rule);
        }
        return new HotRuleReplacer(list);
    }

    private static Rule? TryBuildRule(string pattern, string replacement)
    {
        // 替换侧：允许 \s 表示空格 / \n 表示换行 / \t 表示制表
        // 用 Regex.Unescape 但保留 \1..\n 反向引用（Unescape 不会破坏它们）
        string replaceExpr;
        try { replaceExpr = Regex.Unescape(replacement); }
        catch { replaceExpr = replacement; }

        // 尝试当作正则
        try
        {
            var re = new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);
            return new Rule(re, replaceExpr, false);
        }
        catch (ArgumentException)
        {
            // 退化为字面量
            var re = new Regex(Regex.Escape(pattern), RegexOptions.Compiled | RegexOptions.CultureInvariant);
            return new Rule(re, replaceExpr, true);
        }
    }

    /// <summary>按规则先后顺序执行替换，返回最终文本。规则为空时直接返回原文。</summary>
    public string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || _rules.Count == 0) return text;

        var current = text;
        foreach (var rule in _rules)
        {
            try
            {
                current = rule.Regex.Replace(current, rule.Replacement);
            }
            catch
            {
                // 单条规则替换失败，跳过，不影响其他规则
            }
        }
        return current;
    }

    private sealed record Rule(Regex Regex, string Replacement, bool IsLiteral);
}
