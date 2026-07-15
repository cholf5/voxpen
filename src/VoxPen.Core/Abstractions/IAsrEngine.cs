namespace VoxPen.Core.Abstractions;

/// <summary>
/// ASR 引擎抽象。MVP 只支持一次性识别（把整段音频送进去，等文本出来）。
/// 后续如果引入流式引擎，会新增一个 IStreamingAsrEngine。
/// </summary>
public interface IAsrEngine : IDisposable
{
    /// <summary>引擎名，用于日志和 UI 展示。例如 "paraformer-onnx"。</summary>
    string Name { get; }

    /// <summary>引擎期望的采样率（Hz）。当前所有 MVP 引擎均为 16000。</summary>
    int SampleRate { get; }

    /// <summary>是否已加载模型。首次识别前请先 <see cref="LoadAsync"/>。</summary>
    bool IsLoaded { get; }

    /// <summary>加载模型。耗时可能较长（几百毫秒到几秒），建议后台调用。</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 对一段完整音频做识别。samples 是 float32 mono，值域 [-1, 1]。
    /// </summary>
    Task<Recognition.RecognitionResult> RecognizeAsync(
        ReadOnlyMemory<float> samples,
        CancellationToken cancellationToken = default);
}
