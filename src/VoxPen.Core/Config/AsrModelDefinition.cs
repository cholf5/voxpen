using VoxPen.Core.Abstractions;

namespace VoxPen.Core.Config;

/// <summary>一个可下载 ASR 模型的稳定元数据与目录契约。</summary>
public sealed record AsrModelDefinition(
    AsrEngineKind Kind,
    string DisplayName,
    string DefaultModelDir,
    IReadOnlyList<string> RequiredFiles,
    EngineCapabilities Capabilities,
    string PackageName,
    Uri DownloadUrl,
    long PackageSizeBytes,
    string Description) : IModelPackageDefinition;
