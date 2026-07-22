namespace VoxPen.Core.Config;

public sealed record ModelDirectoryValidation(
    bool IsValid,
    string Message,
    string? ModelPath = null,
    string? TokensPath = null);

/// <summary>检查 Paraformer 模型目录是否满足引擎加载条件。</summary>
public static class ModelDirectoryValidator
{
    public static ModelDirectoryValidation Validate(string? modelDir)
    {
        if (string.IsNullOrWhiteSpace(modelDir))
            return new(false, "模型目录为空");

        var fullDir = Path.GetFullPath(modelDir);
        if (!Directory.Exists(fullDir))
            return new(false, $"模型目录不存在：{fullDir}");

        // 保持原有公开校验器的扁平目录与 int8 模型兼容；新代码改用 AsrModelValidator。
        var modelPath = new[] { "model.int8.onnx", "model.onnx" }
            .Select(name => Path.Combine(fullDir, name))
            .FirstOrDefault(File.Exists);
        if (modelPath is null)
            return new(false, "缺少模型文件：model.int8.onnx 或 model.onnx");

        var tokensPath = Path.Combine(fullDir, "tokens.txt");
        return File.Exists(tokensPath)
            ? new(true, "模型文件完整", modelPath, tokensPath)
            : new(false, "缺少 tokens.txt");
    }
}
