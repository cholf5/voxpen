using System.Globalization;
using System.Text;

namespace CapsWriterSharp.Core.Hotword.Phoneme;

/// <summary>
/// 拼音音节解析工具：把带声调标记的音节（如 <c>Sā</c>、<c>Xiān</c>）拆成
/// <c>(initial, final, tone)</c>；未识别的落到 fallback 处理。
///
/// 声调映射（覆盖普通话六元音 a/e/i/o/u/ü 的四声）：
/// - <c>ā ē ī ō ū ǖ</c> → 1
/// - <c>á é í ó ú ǘ</c> → 2
/// - <c>ǎ ě ǐ ǒ ǔ ǚ</c> → 3
/// - <c>à è ì ò ù ǜ</c> → 4
/// - 无声调标记 → 5（轻声，对齐 pypinyin <c>neutral_tone_with_five=True</c>）
/// </summary>
internal static class PinyinSyllableParser
{
    // 声母表：注意长的必须在前，否则 zh/ch/sh 会被误配成 z/c/s
    private static readonly string[] Initials =
    {
        "zh", "ch", "sh",
        "b", "p", "m", "f", "d", "t", "n", "l",
        "g", "k", "h", "j", "q", "x", "r",
        "z", "c", "s", "y", "w",
    };

    /// <summary>把带声调标记的音节拆成 (initial, final, tone)。返回 null 表示解析失败。</summary>
    public static (string Initial, string Final, string Tone)? Parse(string syllable)
    {
        if (string.IsNullOrEmpty(syllable)) return null;

        var lowered = syllable.ToLowerInvariant();
        var (stripped, tone) = StripToneMark(lowered);
        if (stripped.Length == 0) return null;

        // 找最长匹配声母前缀
        string initial = string.Empty;
        foreach (var cand in Initials)
        {
            if (stripped.StartsWith(cand, StringComparison.Ordinal))
            {
                initial = cand;
                break;
            }
        }

        var final = stripped[initial.Length..];
        return (initial, final, tone);
    }

    /// <summary>
    /// 剥掉带声调的字符，返回 (去掉声调后的音节, 声调数字字符串)。
    /// </summary>
    internal static (string Stripped, string Tone) StripToneMark(string syllable)
    {
        var sb = new StringBuilder(syllable.Length);
        int tone = 5;   // 默认轻声

        foreach (var ch in syllable)
        {
            var (baseCh, t) = MapToneChar(ch);
            if (t > 0) tone = t;
            sb.Append(baseCh);
        }
        return (sb.ToString(), tone.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 单字符声调映射。声调 0 表示未标声调（原字符原样返回）。
    /// </summary>
    private static (char Base, int Tone) MapToneChar(char ch) => ch switch
    {
        'ā' => ('a', 1), 'á' => ('a', 2), 'ǎ' => ('a', 3), 'à' => ('a', 4),
        'ē' => ('e', 1), 'é' => ('e', 2), 'ě' => ('e', 3), 'è' => ('e', 4),
        'ī' => ('i', 1), 'í' => ('i', 2), 'ǐ' => ('i', 3), 'ì' => ('i', 4),
        'ō' => ('o', 1), 'ó' => ('o', 2), 'ǒ' => ('o', 3), 'ò' => ('o', 4),
        'ū' => ('u', 1), 'ú' => ('u', 2), 'ǔ' => ('u', 3), 'ù' => ('u', 4),
        'ǖ' => ('v', 1), 'ǘ' => ('v', 2), 'ǚ' => ('v', 3), 'ǜ' => ('v', 4),
        'ü' => ('v', 0),   // 无声调标记的 ü → 记为 v，保留发音差异
        'ń' => ('n', 2), 'ň' => ('n', 3), 'ǹ' => ('n', 4),
        _ => (ch, 0),
    };
}
