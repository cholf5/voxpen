namespace VoxPen.Core.Models;

public sealed record ModelDownloadProgress(
    ModelDownloadState State,
    long DownloadedBytes = 0,
    long? TotalBytes = null,
    double BytesPerSecond = 0,
    TimeSpan? Remaining = null,
    string? Message = null)
{
    public double? Percent => TotalBytes is > 0 ? DownloadedBytes * 100d / TotalBytes.Value : null;

    public static ModelDownloadProgress Downloading(long downloadedBytes, long? totalBytes) =>
        new(ModelDownloadState.Downloading, downloadedBytes, totalBytes);
}
