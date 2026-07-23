namespace VoxPen.Core.Config;

/// <summary>CT-Transformer 外挂标点模型的下载与安装目录契约。</summary>
public sealed record PunctuationModelDefinition(
    string DefaultModelDir,
    IReadOnlyList<string> RequiredFiles,
    string PackageName,
    Uri DownloadUrl,
    long PackageSizeBytes) : IModelPackageDefinition
{
    public static PunctuationModelDefinition Default { get; } = new(
        ModelDirectoryConvention.PunctuationModelDirectory,
        ["model.onnx"],
        PunctuationModelDownloadInfo.PackageName,
        new Uri(PunctuationModelDownloadInfo.DownloadUrl),
        291_000_000);
}
