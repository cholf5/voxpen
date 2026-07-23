using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using VoxPen.Core.Config;
using VoxPen.Core.Models;

namespace VoxPen.Platform.Windows.Models;

/// <summary>安全解压 ZIP 或 tar.bz2，并且只在模型文件完整后安装。</summary>
public sealed class CompressedModelPackageInstaller : IModelPackageInstaller
{
    public Task<string> InstallAsync(IModelPackageDefinition model, string packagePath, string appBaseDir,
        CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var modelsRoot = Path.Combine(appBaseDir, "models");
        Directory.CreateDirectory(modelsRoot);
        var temporaryDirectory = Path.Combine(modelsRoot, $".install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            using (var packageStream = new FileStream(
                       packagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var archive = ArchiveFactory.OpenArchive(packageStream, new ReaderOptions()))
            {
                foreach (var entry in archive.Entries.Where(entry => !entry.IsDirectory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                    var outputPath = Path.GetFullPath(Path.Combine(temporaryDirectory, entry.Key));
                    if (!outputPath.StartsWith(Path.GetFullPath(temporaryDirectory) + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"模型包包含不安全路径：{entry.Key}");
                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                    entry.WriteToFile(outputPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = false });
                }
            }

            var sourceDirectory = Directory.EnumerateDirectories(temporaryDirectory, "*", SearchOption.AllDirectories)
                .Append(temporaryDirectory)
                .FirstOrDefault(directory => HasRequiredFiles(model, directory))
                ?? throw new InvalidDataException("模型包解压后缺少必需文件");
            var targetDirectory = Path.Combine(appBaseDir, model.DefaultModelDir);
            if (Directory.Exists(targetDirectory))
                throw new IOException($"目标模型目录已存在：{targetDirectory}");
            Directory.CreateDirectory(Path.GetDirectoryName(targetDirectory)!);
            Directory.Move(sourceDirectory, targetDirectory);
            File.Delete(packagePath);
            return targetDirectory;
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, recursive: true);
        }
    }, cancellationToken);

    private static bool HasRequiredFiles(IModelPackageDefinition model, string directory) =>
        model.RequiredFiles.All(relativePath => File.Exists(Path.Combine(directory, relativePath)));
}
