namespace VoxPen.Core.Config;

/// <summary>可下载、可安装模型包的公共目录契约。</summary>
public interface IModelPackageDefinition
{
    string DefaultModelDir { get; }
    IReadOnlyList<string> RequiredFiles { get; }
    string PackageName { get; }
    Uri DownloadUrl { get; }
    long PackageSizeBytes { get; }
}
