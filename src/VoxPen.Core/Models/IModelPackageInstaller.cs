using VoxPen.Core.Config;

namespace VoxPen.Core.Models;

public interface IModelPackageInstaller
{
    Task<string> InstallAsync(AsrModelDefinition model, string packagePath, string appBaseDir,
        CancellationToken cancellationToken = default);
}
