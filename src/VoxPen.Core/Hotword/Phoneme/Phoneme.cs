namespace VoxPen.Core.Hotword.Phoneme;

/// <summary>
/// 带语言属性的音素。端口自 Python <c>algo_phoneme.Phoneme</c>。
/// </summary>
/// <param name="Value">音素值（如 <c>b</c>、<c>ing</c>、<c>python</c>、<c>4</c>）。</param>
/// <param name="Lang">语言类型：zh / en / num / other。</param>
/// <param name="IsWordStart">是否是字边界起始（声母或零声母的韵母/英数首字符）。</param>
/// <param name="IsWordEnd">是否是字边界结束（中文声调或英数末字符）。</param>
/// <param name="CharStart">原文中的起始索引（包含）。</param>
/// <param name="CharEnd">原文中的结束索引（不包含）。</param>
public readonly record struct Phoneme(
    string Value,
    PhonemeLang Lang,
    bool IsWordStart,
    bool IsWordEnd,
    int CharStart,
    int CharEnd)
{
    /// <summary>中文声调（value 是纯数字）为 true。</summary>
    public bool IsTone => Value.Length > 0 && Value.All(char.IsDigit);
}

/// <summary>音素语言分类。</summary>
public enum PhonemeLang
{
    Zh,     // 中文
    En,     // 英文
    Num,    // 数字
    Other,  // 其他（非中文非 ASCII 字母数字的 Unicode 字母）
}
