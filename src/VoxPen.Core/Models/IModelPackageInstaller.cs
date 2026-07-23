using VoxPen.Core.Config;

namespace VoxPen.Core.Models;

public interface IModelPackageInstaller
{
    Task<string> InstallAsync(IModelPackageDefinition model, string packagePath, string appBaseDir,
        CancellationToken cancellationToken = default);
}
