using System.Diagnostics;
using SherpaOnnx;
using VoxPen.Core.Abstractions;

namespace VoxPen.Platform.Windows.Recognition;

/// <summary>
/// 基于 sherpa-onnx <c>OfflinePunctuation</c>（CT-Transformer）的中英标点补全实现。
///
/// 模型约定：<c>&lt;ModelDir&gt;/model.onnx</c>，与上游 CapsWriter-Offline 的
/// <c>sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12</c> 目录结构一致，
/// 用户可直接复用同一份 <c>model.onnx</c>。
///
/// 加载策略：构造函数只保存参数，真正的模型加载在 <see cref="LoadAsync"/> 中完成
/// （与 <see cref="ParaformerEngine"/> 保持一致的两阶段初始化）。
///
/// 降级策略：<see cref="LoadAsync"/> 失败或未调用时，<see cref="AddPunctuation"/> 静默原样返回，
/// 使调用方无需为"无标点模式"额外分支。
///
/// 线程模型：sherpa-onnx 的 <c>OfflinePunctuation</c> 单实例非线程安全（内部 P/Invoke 无锁），
/// 因此这里用 <see cref="SemaphoreSlim"/> 串行化 <see cref="AddPunctuation"/>。
/// </summary>
public sealed class SherpaPunctuator : IPunctuator
{
    private readonly string _modelPath;
    private readonly int _numThreads;
    private readonly string _provider;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private OfflinePunctuation? _engine;
    private bool _disposed;

    public string Name => "ct-transformer-onnx";
    public bool IsLoaded => _engine is not null;

    /// <summary>构造。<paramref name="modelPath"/> 应为 <c>model.onnx</c> 的绝对/相对文件路径。</summary>
    public SherpaPunctuator(string modelPath, int numThreads, string? provider)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
            throw new ArgumentException("模型路径不能为空", nameof(modelPath));

        _modelPath = modelPath;
        _numThreads = Math.Max(1, numThreads);
        _provider = string.IsNullOrWhiteSpace(provider) ? "cpu" : provider!;
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _mutex.Wait(cancellationToken);
            try
            {
                if (_engine is not null) return;
                ThrowIfDisposed();

                if (!File.Exists(_modelPath))
                    throw new FileNotFoundException("标点模型文件不存在", _modelPath);

                var cfg = new OfflinePunctuationConfig();
                cfg.Model.CtTransformer = _modelPath;
                cfg.Model.NumThreads = _numThreads;
                cfg.Model.Provider = _provider;
                cfg.Model.Debug = 0;

                _engine = new OfflinePunctuation(cfg);
            }
            finally
            {
                _mutex.Release();
            }
        }, cancellationToken);
    }

    public string AddPunctuation(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (_engine is null) return text;      // 未加载：走"无标点模式"降级
        if (_disposed) return text;

        _mutex.Wait();
        try
        {
            var engine = _engine;
            if (engine is null || _disposed) return text;
            var sw = Stopwatch.StartNew();
            var result = engine.AddPunct(text);
            sw.Stop();
            // 保留 sw 但不打点：调用方（AppHost）不需要每次都拿耗时；后续可通过 Emit 汇总
            return string.IsNullOrEmpty(result) ? text : result;
        }
        finally
        {
            _mutex.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _engine?.Dispose();
        }
        catch
        {
            // sherpa-onnx 的 Dispose 若失败，通常是双重释放；忽略
        }
        _engine = null;
        _mutex.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SherpaPunctuator));
    }
}
