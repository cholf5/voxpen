using VoxPen.Core.Config;

namespace VoxPen.Core.Models;

/// <summary>协调可续传下载、安装与 UI 状态，不持有任何平台 I/O 实现。</summary>
public sealed class ModelInstallCoordinator
{
    private readonly IModelPackageDownloader _downloader;
    private readonly IModelPackageInstaller _installer;

    public ModelInstallCoordinator(IModelPackageDownloader downloader, IModelPackageInstaller installer)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
    }

    public async Task<string> InstallAsync(IModelPackageDefinition model, string appBaseDir,
        IProgress<ModelDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(appBaseDir);
        var downloadDirectory = Path.Combine(appBaseDir, "models", ".downloads");
        Directory.CreateDirectory(downloadDirectory);
        var partialPath = Path.Combine(downloadDirectory, model.PackageName + ".partial");

        try
        {
            progress?.Report(new(ModelDownloadState.Downloading));
            var packagePath = await _downloader.DownloadAsync(model, partialPath, progress, cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new(ModelDownloadState.Verifying, Message: "正在校验模型包…"));
            progress?.Report(new(ModelDownloadState.Installing, Message: "正在安装模型…"));
            var installedPath = await _installer.InstallAsync(model, packagePath, appBaseDir, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new(ModelDownloadState.Completed, Message: "模型已安装"));
            return installedPath;
        }
        catch (OperationCanceledException)
        {
            progress?.Report(new(ModelDownloadState.Canceled, Message: "下载已取消，可稍后继续"));
            throw;
        }
        catch (Exception ex)
        {
            progress?.Report(new(ModelDownloadState.Failed, Message: ex.Message));
            throw;
        }
    }
}
