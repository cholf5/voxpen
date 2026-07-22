using System.Diagnostics;
using System.Runtime.InteropServices;
using SherpaOnnx;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Core.Recognition;

namespace VoxPen.Platform.Windows.Recognition;

/// <summary>四种 sherpa-onnx 离线模型共用的加载、串行识别和释放逻辑。</summary>
public abstract class SherpaOfflineAsrEngineBase : IAsrEngine
{
    private const int TargetSampleRate = 16000;
    private readonly AsrConfig _config;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private OfflineRecognizer? _recognizer;
    private bool _disposed;

    protected SherpaOfflineAsrEngineBase(AsrConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    protected AsrConfig Config => _config;
    public abstract string Name { get; }
    public abstract EngineCapabilities Capabilities { get; }
    public int SampleRate => TargetSampleRate;
    public bool IsLoaded => _recognizer is not null;

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        _mutex.Wait(cancellationToken);
        try
        {
            if (_recognizer is not null) return;
            ThrowIfDisposed();

            var validation = AsrModelValidator.Validate(AsrModelCatalog.Get(_config.Engine), _config.ModelDir);
            if (!validation.IsValid)
                throw new DirectoryNotFoundException(validation.Message);

            _recognizer = new OfflineRecognizer(CreateRecognizerConfig());
        }
        finally
        {
            _mutex.Release();
        }
    }, cancellationToken);

    public async Task<RecognitionResult> RecognizeAsync(
        ReadOnlyMemory<float> samples, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (samples.Length == 0) return RecognitionResult.Empty;
        if (_recognizer is null) await LoadAsync(cancellationToken).ConfigureAwait(false);

        var sampleArray = MemoryMarshal.TryGetArray(samples, out ArraySegment<float> segment)
            && segment.Offset == 0 && segment.Count == segment.Array!.Length
                ? segment.Array
                : samples.ToArray();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _mutex.Wait(cancellationToken);
            try
            {
                ThrowIfDisposed();
                var stopwatch = Stopwatch.StartNew();
                using var stream = _recognizer!.CreateStream();
                stream.AcceptWaveform(TargetSampleRate, sampleArray);
                _recognizer.Decode(stream);
                var result = stream.Result;
                stopwatch.Stop();
                return new RecognitionResult
                {
                    Text = result.Text ?? string.Empty,
                    Tokens = result.Tokens ?? Array.Empty<string>(),
                    Timestamps = result.Timestamps ?? Array.Empty<float>(),
                    Elapsed = stopwatch.Elapsed,
                };
            }
            finally
            {
                _mutex.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _recognizer?.Dispose(); } catch { }
        _recognizer = null;
        _mutex.Dispose();
    }

    protected OfflineRecognizerConfig CreateBaseConfig()
    {
        var recognizerConfig = new OfflineRecognizerConfig();
        recognizerConfig.FeatConfig.SampleRate = TargetSampleRate;
        recognizerConfig.ModelConfig.NumThreads = Math.Max(1, _config.NumThreads);
        recognizerConfig.ModelConfig.Provider = _config.Provider ?? "cpu";
        recognizerConfig.ModelConfig.Debug = 0;
        recognizerConfig.DecodingMethod = "greedy_search";
        return recognizerConfig;
    }

    protected abstract OfflineRecognizerConfig CreateRecognizerConfig();

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(GetType().Name);
    }
}
