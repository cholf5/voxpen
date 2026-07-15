using System.Text;
using System.Text.Json;

namespace VoxPen.Core.Transcribe;

/// <summary>
/// 转录 JSON 输出：<c>{"timestamps":[..], "tokens":[..]}</c>。
/// 与原项目 <c>ResultHandler.save_results</c> 保持 schema 一致，
/// 便于用户后续手动修改 txt 再用同名 json 重跑对齐。
/// </summary>
public static class TranscriptJsonWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Compose(
        IReadOnlyList<double> timestamps,
        IReadOnlyList<string> tokens)
    {
        var payload = new Payload
        {
            Timestamps = (timestamps ?? Array.Empty<double>()).ToArray(),
            Tokens = (tokens ?? Array.Empty<string>()).ToArray(),
        };
        return JsonSerializer.Serialize(payload, Options);
    }

    public static async Task WriteAsync(
        string filePath,
        IReadOnlyList<double> timestamps,
        IReadOnlyList<string> tokens,
        CancellationToken cancellationToken = default)
    {
        var json = Compose(timestamps, tokens);
        await File.WriteAllTextAsync(filePath, json, new UTF8Encoding(false), cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class Payload
    {
        [System.Text.Json.Serialization.JsonPropertyName("timestamps")]
        public double[] Timestamps { get; set; } = Array.Empty<double>();

        [System.Text.Json.Serialization.JsonPropertyName("tokens")]
        public string[] Tokens { get; set; } = Array.Empty<string>();
    }
}
