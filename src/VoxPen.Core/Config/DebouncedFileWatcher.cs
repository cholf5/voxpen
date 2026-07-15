namespace VoxPen.Core.Config;

/// <summary>
/// 带 3 秒防抖的文件监视器。用于 config.json / hot-rule.txt 热重载。
///
/// 使用示例：
/// <code>
///   using var w = new DebouncedFileWatcher(path, TimeSpan.FromSeconds(3), () =&gt; Reload(path));
/// </code>
/// </summary>
public sealed class DebouncedFileWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly TimeSpan _debounce;
    private readonly Action _onChanged;
    private readonly object _lock = new();

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public DebouncedFileWatcher(string filePath, TimeSpan debounce, Action onChanged)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        var file = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
            throw new ArgumentException("Invalid file path", nameof(filePath));

        _debounce = debounce;
        _onChanged = onChanged;

        Directory.CreateDirectory(dir);
        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += (_, _) => Schedule();
        _watcher.Created += (_, _) => Schedule();
        _watcher.Renamed += (_, _) => Schedule();
    }

    private void Schedule()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounce, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) return;
                    _onChanged();
                }
                catch (TaskCanceledException) { }
                catch { /* onChanged 内部异常不影响下一次 */ }
            });
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _cts?.Cancel();
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }
    }
}
