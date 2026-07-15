namespace CapsWriterSharp.Core.Config;

/// <summary>解析模型目录：优先使用应用目录，开发运行时再向父目录查找。</summary>
public static class ModelDirectoryResolver
{
    public static string Resolve(string applicationDirectory, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        var directory = new DirectoryInfo(Path.GetFullPath(applicationDirectory));
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, configuredPath);
            if (Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
            directory = directory.Parent;
        }

        return Path.GetFullPath(Path.Combine(applicationDirectory, configuredPath));
    }
}
