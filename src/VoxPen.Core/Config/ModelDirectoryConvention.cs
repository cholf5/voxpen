namespace VoxPen.Core.Config;

/// <summary>模型安装位置约定。旧配置中的目录字段仅为兼容而保留，不再参与路径选择。</summary>
public static class ModelDirectoryConvention
{
    public const string PunctuationModelDirectory =
        "models/Punct-CT-Transformer/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12";

    public static void Apply(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Asr.ModelDir = AsrModelCatalog.Get(config.Asr.Engine).DefaultModelDir;
        config.Punctuation.ModelDir = PunctuationModelDirectory;
    }
}
