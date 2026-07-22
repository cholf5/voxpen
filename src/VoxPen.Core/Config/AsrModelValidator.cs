namespace VoxPen.Core.Config;

/// <summary>按模型清单检查 ASR 模型目录。</summary>
public static class AsrModelValidator
{
    public static ModelDirectoryValidation Validate(AsrModelDefinition definition, string? modelDir)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (string.IsNullOrWhiteSpace(modelDir))
            return new(false, "模型目录为空");

        var fullDir = Path.GetFullPath(modelDir);
        if (!Directory.Exists(fullDir))
            return new(false, $"模型目录不存在：{fullDir}");

        var missingFile = definition.RequiredFiles
            .FirstOrDefault(relativePath => !File.Exists(Path.Combine(fullDir, relativePath)));
        if (missingFile is not null)
            return new(false, $"缺少模型文件：{missingFile}");

        var modelPath = definition.RequiredFiles
            .Select(relativePath => Path.Combine(fullDir, relativePath))
            .FirstOrDefault(path => Path.GetFileName(path).StartsWith("model", StringComparison.OrdinalIgnoreCase));
        var tokensPath = definition.RequiredFiles
            .Select(relativePath => Path.Combine(fullDir, relativePath))
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "tokens.txt", StringComparison.OrdinalIgnoreCase));

        return new(true, "模型文件完整", modelPath, tokensPath);
    }
}
