namespace VoxPen.Core.Config;

/// <summary>追踪组合键的完成与打断；只有全部配置键按下时才进入活动状态。</summary>
public sealed class ShortcutChord
{
    private readonly HashSet<string> _keys;
    private readonly HashSet<string> _pressed = new(StringComparer.OrdinalIgnoreCase);
    private bool _isActive;

    public ShortcutChord(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _keys = keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_keys.Count == 0) throw new ArgumentException("至少需要一个键", nameof(keys));
    }

    /// <summary>登记按下；仅当本次按下使组合完整时返回 <see langword="true"/>。</summary>
    public bool Press(string key)
    {
        _pressed.Add(key);
        if (_isActive || !_keys.IsSubsetOf(_pressed)) return false;
        _isActive = true;
        return true;
    }

    /// <summary>登记松开；仅当本次松开打断已完成组合时返回 <see langword="true"/>。</summary>
    public bool Release(string key)
    {
        _pressed.Remove(key);
        if (!_isActive || _keys.IsSubsetOf(_pressed)) return false;
        _isActive = false;
        return true;
    }
}
