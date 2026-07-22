using FluentAssertions;
using VoxPen.Core.Config;
using VoxPen.Core.Models;
using Xunit;

namespace VoxPen.Core.Tests.Models;

public sealed class ModelInstallCoordinatorTests
{
    [Fact]
    public async Task Install_reports_the_complete_state_sequence()
    {
        var states = new List<ModelDownloadState>();
        var coordinator = new ModelInstallCoordinator(new FakeDownloader(), new FakeInstaller());
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await coordinator.InstallAsync(AsrModelCatalog.Get(AsrEngineKind.SenseVoice), root,
                new Progress<ModelDownloadProgress>(progress => states.Add(progress.State)));

            states.Should().ContainInOrder(
                ModelDownloadState.Downloading,
                ModelDownloadState.Verifying,
                ModelDownloadState.Installing,
                ModelDownloadState.Completed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class FakeDownloader : IModelPackageDownloader
    {
        public Task<string> DownloadAsync(AsrModelDefinition model, string partialPath,
            IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken)
        {
            File.WriteAllText(partialPath, "package");
            progress?.Report(ModelDownloadProgress.Downloading(7, 10));
            return Task.FromResult(partialPath);
        }
    }

    private sealed class FakeInstaller : IModelPackageInstaller
    {
        public Task<string> InstallAsync(AsrModelDefinition model, string packagePath, string appBaseDir,
            CancellationToken cancellationToken) => Task.FromResult(Path.Combine(appBaseDir, model.DefaultModelDir));
    }
}
