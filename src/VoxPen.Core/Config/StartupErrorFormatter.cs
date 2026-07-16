namespace VoxPen.Core.Config;

/// <summary>把启动异常转换为用户可执行的提示。</summary>
public static class StartupErrorFormatter
{
    public static string Format(Exception error)
    {
        if (error is DirectoryNotFoundException)
        {
            return $"模型加载失败：{error.Message}\n" +
                   $"请检查模型目录是否存在，并确认 config.json 中的 modelDir 配置正确。\n" +
                   $"若尚未下载，请获取 {ModelDownloadInfo.PackageName}：{ModelDownloadInfo.DownloadUrl}";
        }

        return $"程序启动失败：{error.Message}\n请查看“日志”页获取详细信息。";
    }
}
