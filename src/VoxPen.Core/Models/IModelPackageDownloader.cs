using VoxPen.Core.Config;

namespace VoxPen.Core.Models;

public interface IModelPackageDownloader
{
    Task<string> DownloadAsync(IModelPackageDefinition model, string partialPath,
        IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken = default);
}
