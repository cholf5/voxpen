namespace VoxPen.Core.Config;

/// <summary>
/// 检查标点模型目录是否满足 sherpa-onnx <c>OfflinePunctuation</c>（CT-Transformer）加载条件。
///
/// 与 <see cref="ModelDirectoryValidator"/> 分开维护：Paraformer 的目录里必须有 <c>tokens.txt</c>
/// 与 <c>model.onnx / model.int8.onnx</c> 二选一；标点模型只需要 <c>model.onnx</c>，
/// 且不同发行版对 tokens 的命名（<c>tokens.json</c> / <c>tokens.txt</c>）并不一致，因此这里
/// 只强校验 <c>model.onnx</c> 存在，避免"文件其实齐了但被错杀"。
/// </summary>
public static class PunctuationModelValidator
{
    private const string ModelFileName = "model.onnx";

    public static ModelDirectoryValidation Validate(string? modelDir)
    {
        if (string.IsNullOrWhiteSpace(modelDir))
            return new(false, "标点模型目录为空");

        var fullDir = Path.GetFullPath(modelDir);
        if (!Directory.Exists(fullDir))
            return new(false, $"标点模型目录不存在：{fullDir}");

        var modelPath = Path.Combine(fullDir, ModelFileName);
        if (!File.Exists(modelPath))
            return new(false, "缺少 model.onnx");

        return new(true, "标点模型文件完整", modelPath);
    }
}
