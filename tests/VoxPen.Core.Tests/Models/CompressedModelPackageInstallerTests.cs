using System.IO.Compression;
using FluentAssertions;
using VoxPen.Core.Config;
using VoxPen.Platform.Windows.Models;
using Xunit;

namespace VoxPen.Core.Tests.Models;

public sealed class CompressedModelPackageInstallerTests
{
    [Fact]
    public async Task Install_deletes_package_after_releasing_the_archive_file_handle()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(root, "Paraformer.zip.partial");
        Directory.CreateDirectory(root);
        try
        {
            CreateParaformerPackage(packagePath);

            var installer = new CompressedModelPackageInstaller();
            var installedPath = await installer.InstallAsync(
                AsrModelCatalog.Get(AsrEngineKind.Paraformer), packagePath, root);

            Directory.Exists(installedPath).Should().BeTrue();
            File.Exists(packagePath).Should().BeFalse();
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // 测试失败时保留原始安装异常；临时文件由系统临时目录清理。
            }
        }
    }

    private static void CreateParaformerPackage(string packagePath)
    {
        using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
        archive.CreateEntry("paraformer/model.onnx");
        archive.CreateEntry("paraformer/tokens.txt");
    }
}
