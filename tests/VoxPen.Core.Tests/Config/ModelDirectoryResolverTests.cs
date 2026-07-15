using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

public sealed class ModelDirectoryResolverTests
{
    [Fact]
    public void ResolvesModelFromAnAncestorWhenOutputDirectoryDoesNotContainIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "capswriter-model-test-" + Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(root, "src", "VoxPen.App", "bin", "Debug");
        var modelDirectory = Path.Combine(root, "models", "paraformer");

        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(modelDirectory);
        try
        {
            var resolved = ModelDirectoryResolver.Resolve(outputDirectory, "models/paraformer");

            Assert.Equal(Path.GetFullPath(modelDirectory), resolved);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
