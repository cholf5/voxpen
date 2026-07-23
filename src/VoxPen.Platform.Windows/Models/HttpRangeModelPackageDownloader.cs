using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using VoxPen.Core.Config;
using VoxPen.Core.Models;

namespace VoxPen.Platform.Windows.Models;

/// <summary>基于 HTTP Range 的可续传模型下载器。</summary>
public sealed class HttpRangeModelPackageDownloader : IModelPackageDownloader
{
    private readonly HttpClient _httpClient;

    public HttpRangeModelPackageDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<string> DownloadAsync(IModelPackageDefinition model, string partialPath,
        IProgress<ModelDownloadProgress>? progress, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        var existingBytes = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, model.DownloadUrl);
        if (existingBytes > 0) request.Headers.Range = new RangeHeaderValue(existingBytes, null);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var append = existingBytes > 0 && response.StatusCode == HttpStatusCode.PartialContent;
        if (!append) existingBytes = 0;
        var totalBytes = response.Content.Headers.ContentLength is { } contentLength
            ? contentLength + existingBytes
            : model.PackageSizeBytes;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(partialPath,
            append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);

        var buffer = new byte[128 * 1024];
        var downloadedBytes = existingBytes;
        var stopwatch = Stopwatch.StartNew();
        var lastReported = TimeSpan.Zero;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            downloadedBytes += read;
            if (stopwatch.Elapsed - lastReported >= TimeSpan.FromMilliseconds(250))
            {
                ReportProgress(progress, downloadedBytes, totalBytes, stopwatch.Elapsed);
                lastReported = stopwatch.Elapsed;
            }
        }
        ReportProgress(progress, downloadedBytes, totalBytes, stopwatch.Elapsed);
        return partialPath;
    }

    private static void ReportProgress(IProgress<ModelDownloadProgress>? progress, long downloaded, long total,
        TimeSpan elapsed)
    {
        var speed = elapsed.TotalSeconds <= 0 ? 0 : downloaded / elapsed.TotalSeconds;
        TimeSpan? remaining = speed > 0 && total >= downloaded
            ? TimeSpan.FromSeconds((total - downloaded) / speed)
            : null;
        progress?.Report(new(ModelDownloadState.Downloading, downloaded, total, speed, remaining));
    }
}
