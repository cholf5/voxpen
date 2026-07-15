using ToolGood.Words.Pinyin;

namespace CapsWriterSharp.Core.Hotword.Phoneme;

/// <summary>
/// 文本 → 音素序列。端口自 Python <c>algo_phoneme.get_phoneme_info</c>。
///
/// 处理规则：
/// 1. 逐字符扫描，聚合连续同类型片段。
/// 2. CJK 字符片段（<c>\u4e00-\u9fff</c>）：调用 ToolGood 拿每个字的拼音音节，
///    再拆成 (声母 / 韵母 / 声调) 三个 Phoneme（零声母时省略声母）。
/// 3. ASCII 字母/数字：按驼峰边界（小写→大写）+ 字母/数字边界 拆分 token；
///    默认按字符拆开为独立音素（<c>ascii_split_char=true</c>）。
/// 4. 其他 Unicode 字母：单独作为 <see cref="PhonemeLang.Other"/> 音素。
/// 5. 标点/空格/符号：跳过。
/// </summary>
public sealed class PhonemeExtractor
{
    private readonly bool _asciiSplitChar;

    public PhonemeExtractor(bool asciiSplitChar = true)
    {
        _asciiSplitChar = asciiSplitChar;
    }

    /// <summary>默认实例（英数按字符拆分）。</summary>
    public static readonly PhonemeExtractor Default = new(asciiSplitChar: true);

    /// <summary>
    /// 返回带位置信息的音素序列。<paramref name="text"/> 为 null 或空返回空列表。
    /// </summary>
    public List<Phoneme> Extract(string? text)
    {
        var result = new List<Phoneme>();
        if (string.IsNullOrEmpty(text)) return result;

        int pos = 0;
        while (pos < text.Length)
        {
            char ch = text[pos];
            if (IsCjk(ch))
            {
                pos = ProcessCjk(text, pos, result);
            }
            else if (IsAsciiLetterOrDigit(ch))
            {
                pos = ProcessEnNum(text, pos, result);
            }
            else if (char.IsLetter(ch))
            {
                // 非 CJK、非 ASCII 的 Unicode 字母（希腊字母/西里尔/日文假名等）作 'other'
                result.Add(new Phoneme(
                    Value: char.ToLowerInvariant(ch).ToString(),
                    Lang: PhonemeLang.Other,
                    IsWordStart: true,
                    IsWordEnd: true,
                    CharStart: pos,
                    CharEnd: pos + 1));
                pos++;
            }
            else
            {
                pos++;   // 标点/空格/其他符号跳过
            }
        }

        return result;
    }

    private static bool IsCjk(char ch) => ch >= '\u4e00' && ch <= '\u9fff';
    private static bool IsAsciiLetterOrDigit(char ch)
        => (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9');

    /// <summary>处理连续 CJK 片段。</summary>
    private int ProcessCjk(string text, int start, List<Phoneme> seq)
    {
        int end = start + 1;
        while (end < text.Length && IsCjk(text[end])) end++;

        var fragment = text[start..end];
        string[] syllables;
        try
        {
            syllables = WordsHelper.GetPinyinList(fragment, tone: true);
        }
        catch
        {
            syllables = Array.Empty<string>();
        }

        int minLen = Math.Min(fragment.Length, syllables.Length);
        for (int i = 0; i < minLen; i++)
        {
            int idx = start + i;
            var parsed = PinyinSyllableParser.Parse(syllables[i]);
            if (parsed is null)
            {
                // 拼音库没识别（多为姓氏冷字或非常用字），作为整体作 zh 音素兜底
                seq.Add(new Phoneme(fragment[i].ToString(), PhonemeLang.Zh,
                    IsWordStart: true, IsWordEnd: true,
                    CharStart: idx, CharEnd: idx + 1));
                continue;
            }

            var (initial, final, tone) = parsed.Value;
            bool hasInitial = initial.Length > 0;
            if (hasInitial)
            {
                seq.Add(new Phoneme(initial, PhonemeLang.Zh,
                    IsWordStart: true, IsWordEnd: false,
                    CharStart: idx, CharEnd: idx + 1));
            }
            if (final.Length > 0)
            {
                seq.Add(new Phoneme(final, PhonemeLang.Zh,
                    IsWordStart: !hasInitial, IsWordEnd: false,
                    CharStart: idx, CharEnd: idx + 1));
            }
            if (tone.Length > 0)
            {
                seq.Add(new Phoneme(tone, PhonemeLang.Zh,
                    IsWordStart: false, IsWordEnd: true,
                    CharStart: idx, CharEnd: idx + 1));
            }
            else
            {
                // 极少数无 final 也无 tone 的情况，作为整字兜底
                if (!hasInitial)
                {
                    seq.Add(new Phoneme(fragment[i].ToString(), PhonemeLang.Zh,
                        IsWordStart: true, IsWordEnd: true,
                        CharStart: idx, CharEnd: idx + 1));
                }
            }
        }

        // 对齐失败的尾部字（syllables 不足），逐字兜底
        for (int i = minLen; i < fragment.Length; i++)
        {
            int idx = start + i;
            seq.Add(new Phoneme(fragment[i].ToString(), PhonemeLang.Zh,
                IsWordStart: true, IsWordEnd: true,
                CharStart: idx, CharEnd: idx + 1));
        }

        return end;
    }

    /// <summary>
    /// 处理 ASCII 字母/数字片段，按驼峰 + 字母/数字边界切分 token。
    /// </summary>
    private int ProcessEnNum(string text, int start, List<Phoneme> seq)
    {
        int pos = start;
        while (pos < text.Length)
        {
            char ch = text[pos];
            if (!IsAsciiLetterOrDigit(ch)) break;

            // 遇到边界（驼峰 aA / 字母数字 a1 / 数字字母 1a）在此处结束当前 token
            if (pos > start)
            {
                char prev = text[pos - 1];
                if ((char.IsLower(prev) && char.IsUpper(ch))
                    || (char.IsLetter(prev) && char.IsDigit(ch))
                    || (char.IsDigit(prev) && char.IsLetter(ch)))
                {
                    break;
                }
            }
            pos++;
        }

        int end = pos;
        var token = text[start..end].ToLowerInvariant();
        var lang = token.All(char.IsDigit) ? PhonemeLang.Num : PhonemeLang.En;

        if (_asciiSplitChar)
        {
            for (int i = 0; i < token.Length; i++)
            {
                seq.Add(new Phoneme(
                    Value: token[i].ToString(),
                    Lang: lang,
                    IsWordStart: i == 0,
                    IsWordEnd: i == token.Length - 1,
                    CharStart: start + i,
                    CharEnd: start + i + 1));
            }
        }
        else
        {
            seq.Add(new Phoneme(
                Value: token,
                Lang: lang,
                IsWordStart: true,
                IsWordEnd: true,
                CharStart: start,
                CharEnd: end));
        }

        // 继续处理紧跟的下一个片段（若还在 EnNum 范围）— 由外层 while 再调
        return end;
    }
}
