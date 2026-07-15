using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Core.Hotword;
using VoxPen.Core.Recognition;
using VoxPen.Core.Transcribe;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Transcribe;

public class FileTranscriberTests : IDisposable
{
    private readonly string _tempDir;

    public FileTranscriberTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cwsharp-ft-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ---------- Fakes ----------
    private sealed class FakeDecoder : IAudioDecoder
    {
        private readonly float[] _samples;
        private readonly int _rate;
        public FakeDecoder(float[] samples, int rate = 16000) { _samples = samples; _rate = rate; }
        public bool CanDecode(string filePath) => true;
        public ValueTask<DecodedAudio> DecodeAsync(string filePath, CancellationToken ct = default)
            => ValueTask.FromResult(new DecodedAudio(_samples, _rate));
    }

    /// <summary>脚本化 ASR：预置每段返回值。</summary>
    private sealed class ScriptedAsr : IAsrEngine
    {
        private readonly Queue<RecognitionResult> _script;
        public ScriptedAsr(IEnumerable<RecognitionResult> results) { _script = new Queue<RecognitionResult>(results); }
        public string Name => "scripted";
        public int SampleRate => 16000;
        public bool IsLoaded => true;
        public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<RecognitionResult> RecognizeAsync(ReadOnlyMemory<float> samples, CancellationToken ct = default)
        {
            if (_script.Count == 0) return Task.FromResult(RecognitionResult.Empty);
            return Task.FromResult(_script.Dequeue());
        }
        public void Dispose() { }
    }

    private static RecognitionResult MakeResult(string text, string[] tokens, float[] timestamps)
        => new() { Text = text, Tokens = tokens, Timestamps = timestamps };

    private string WriteDummyAudioFile(string ext = ".wav")
    {
        var p = Path.Combine(_tempDir, "clip" + ext);
        File.WriteAllBytes(p, new byte[] { 0 });   // 内容不重要：FakeDecoder 忽略
        return p;
    }

    // ---------- Tests ----------
    [Fact]
    public async Task SingleSegment_WritesAllOutputs()
    {
        var samples = new float[16000 * 5]; // 5s
        var asr = new ScriptedAsr(new[]
        {
            MakeResult("你好世界。这是测试。",
                new[] { "你", "好", "世", "界", "。", "这", "是", "测", "试", "。" },
                new[] { 0.0f, 0.2f, 0.4f, 0.6f, 0.7f, 1.0f, 1.2f, 1.4f, 1.6f, 1.7f }),
        });

        var t = new FileTranscriber(asr, new FakeDecoder(samples),
            new TranscribeConfig { SegDurationSeconds = 60, SegOverlapSeconds = 4 });
        var path = WriteDummyAudioFile();
        var res = await t.TranscribeAsync(path);

        res.Text.Should().Contain("你好世界");
        res.Tokens.Should().Contain("你");
        res.WrittenFiles.Should().Contain(p => p.EndsWith(".txt"));
        res.WrittenFiles.Should().Contain(p => p.EndsWith(".srt"));
        res.WrittenFiles.Should().Contain(p => p.EndsWith(".json"));
        res.WrittenFiles.Should().NotContain(p => p.EndsWith(".merge.txt"));

        // 检查落盘内容
        var txtPath = res.WrittenFiles.First(p => p.EndsWith(".txt"));
        var txt = await File.ReadAllTextAsync(txtPath);
        txt.Should().Contain("你好世界");
        txt.Should().Contain("\n"); // SmartSplit 分行

        var srt = await File.ReadAllTextAsync(res.WrittenFiles.First(p => p.EndsWith(".srt")));
        srt.Should().Contain("-->");
    }

    [Fact]
    public async Task SaveMergeFlag_WritesMergeFile()
    {
        var asr = new ScriptedAsr(new[]
        {
            MakeResult("测试", new[] { "测", "试" }, new[] { 0f, 0.2f }),
        });
        var t = new FileTranscriber(asr, new FakeDecoder(new float[16000]),
            new TranscribeConfig
            {
                SegDurationSeconds = 60,
                SegOverlapSeconds = 4,
                SaveMerge = true,
                SaveSrt = false,
                SaveJson = false,
            });
        var path = WriteDummyAudioFile();
        var res = await t.TranscribeAsync(path);

        res.WrittenFiles.Should().Contain(p => p.EndsWith(".merge.txt"));
        res.WrittenFiles.Should().NotContain(p => p.EndsWith(".srt"));
        var merge = await File.ReadAllTextAsync(res.WrittenFiles.First(p => p.EndsWith(".merge.txt")));
        merge.Should().Be("测试");
    }

    [Fact]
    public async Task MultipleSegments_MergedByOverlap()
    {
        var samples = new float[16000 * 130]; // 130s → 3 segments
        // 两段前缀「共享」中间几字，验证合并去重
        var asr = new ScriptedAsr(new[]
        {
            MakeResult("你好世界这是第一段落尾",
                new[] { "你", "好", "世", "界", "这", "是", "第", "一", "段", "落", "尾" },
                new[] { 0f, 0.3f, 0.6f, 0.9f, 1.2f, 1.5f, 1.8f, 2.1f, 2.4f, 2.7f, 3.0f }),
            MakeResult("落尾第二段中",
                new[] { "落", "尾", "第", "二", "段", "中" },
                new[] { 0f, 0.3f, 0.6f, 0.9f, 1.2f, 1.5f }),
            MakeResult("段中第三段末",
                new[] { "段", "中", "第", "三", "段", "末" },
                new[] { 0f, 0.3f, 0.6f, 0.9f, 1.2f, 1.5f }),
        });

        var t = new FileTranscriber(asr, new FakeDecoder(samples),
            new TranscribeConfig
            {
                SegDurationSeconds = 60,
                SegOverlapSeconds = 4,
                SaveTxt = false,
                SaveJson = false,
                SaveSrt = false,
                SaveMerge = true,
            });
        var path = WriteDummyAudioFile();
        var res = await t.TranscribeAsync(path);

        res.Text.Should().Contain("你好世界");
        res.Text.Should().Contain("末");
        // 相邻段重叠部分应被吃掉（不出现两个 "落尾"）
        var occurrencesLuoWei = System.Text.RegularExpressions.Regex.Matches(res.Text, "落尾").Count;
        occurrencesLuoWei.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task EmptyAudio_ReturnsEmptyResult()
    {
        var t = new FileTranscriber(new ScriptedAsr(Array.Empty<RecognitionResult>()),
            new FakeDecoder(Array.Empty<float>()),
            new TranscribeConfig());
        var path = WriteDummyAudioFile();
        var res = await t.TranscribeAsync(path);
        res.Text.Should().BeEmpty();
        res.WrittenFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task HotRuleReplacer_AppliedBeforeSplit()
    {
        var asr = new ScriptedAsr(new[]
        {
            MakeResult("苹果", new[] { "苹", "果" }, new[] { 0f, 0.3f }),
        });
        var rules = HotRuleReplacer.Parse(new[] { "苹果=Apple" });
        var t = new FileTranscriber(asr, new FakeDecoder(new float[16000]),
            new TranscribeConfig { SaveSrt = false, SaveJson = false, SaveMerge = false },
            hotRule: rules);
        var path = WriteDummyAudioFile();
        var res = await t.TranscribeAsync(path);
        res.Text.Should().Be("Apple");
    }

    [Fact]
    public async Task NoTimestamps_SkipsSrt()
    {
        var asr = new ScriptedAsr(new[]
        {
            MakeResult("测试", Array.Empty<string>(), Array.Empty<float>()),
        });
        var t = new FileTranscriber(asr, new FakeDecoder(new float[16000]),
            new TranscribeConfig { SaveSrt = true, SaveJson = false, SaveMerge = false });
        var path = WriteDummyAudioFile();
        var res = await t.TranscribeAsync(path);
        res.WrittenFiles.Should().NotContain(p => p.EndsWith(".srt"));
        res.WrittenFiles.Should().Contain(p => p.EndsWith(".txt"));
    }

    [Fact]
    public async Task OutputPath_OverridesDefault()
    {
        var asr = new ScriptedAsr(new[]
        {
            MakeResult("测试", new[] { "测", "试" }, new[] { 0f, 0.3f }),
        });
        var t = new FileTranscriber(asr, new FakeDecoder(new float[16000]),
            new TranscribeConfig { SaveSrt = false, SaveJson = false, SaveMerge = false });
        var path = WriteDummyAudioFile();
        var custom = Path.Combine(_tempDir, "custom.wav");
        var res = await t.TranscribeAsync(path, outputPath: custom);
        res.WrittenFiles.Should().OnlyContain(p => p.StartsWith(Path.Combine(_tempDir, "custom")));
    }

    [Fact]
    public void LinearResample_UpsampleAndDownsample()
    {
        var src = new float[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        var up = FileTranscriber.LinearResample(src, 8000, 16000);
        up.Length.Should().Be(16);
        up[0].Should().Be(0);
        // pos = 15*0.5 = 7.5 → i0=7, i1 clamps to 7 (last index) → interp = 7
        up[15].Should().BeApproximately(7f, 1e-3f);
        // pos = 7*0.5 = 3.5 → interp = 3.5
        up[7].Should().BeApproximately(3.5f, 1e-3f);

        var down = FileTranscriber.LinearResample(src, 16000, 8000);
        down.Length.Should().Be(4);
        down[0].Should().Be(0);
    }
}
