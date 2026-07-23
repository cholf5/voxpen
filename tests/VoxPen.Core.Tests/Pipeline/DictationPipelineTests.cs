using FluentAssertions;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;
using VoxPen.Core.Notification;
using VoxPen.Core.Pipeline;
using VoxPen.Core.Recognition;
using Xunit;

namespace VoxPen.Core.Tests.Pipeline;

public sealed class DictationPipelineTests
{
    [Fact]
    public async Task SuccessfulRecognition_DoesNotShowSuccessNotification()
    {
        using var hotkey = new FakeHotkey();
        using var capture = new FakeAudioCapture();
        using var pipeline = new DictationPipeline(
            hotkey,
            capture,
            new FakeAsr("测试结果"),
            new FakeTextOutput(),
            new AppConfig
            {
                Shortcut = new ShortcutConfig { ShortPressThresholdSeconds = 0.001 },
                Notification = new NotificationConfig
                {
                    Enabled = true,
                },
            });

        var recognized = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var notifications = 0;
        pipeline.TextRecognized += (_, text) => recognized.TrySetResult(text);
        pipeline.NotificationHandler = (_, _, _) =>
        {
            Interlocked.Increment(ref notifications);
            return Task.CompletedTask;
        };

        var pressedAt = DateTime.UtcNow;
        hotkey.RaisePressed(pressedAt);
        capture.RaiseChunk(new float[8000]);
        Thread.Sleep(10);
        hotkey.RaiseReleased(pressedAt.AddSeconds(1));

        (await recognized.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be("测试结果");
        await Task.Delay(100);

        notifications.Should().Be(0);
    }

    [Fact]
    public void ImmediateRelease_IsShortPressEvenWhenHookTimestampIsUnreliable()
    {
        using var hotkey = new FakeHotkey();
        using var capture = new FakeAudioCapture();
        using var pipeline = new DictationPipeline(
            hotkey,
            capture,
            new FakeAsr("不应识别"),
            new FakeTextOutput(),
            new AppConfig());

        var shortPresses = 0;
        pipeline.ShortPressDetected += (_, _) => shortPresses++;

        var pressedAt = DateTime.UtcNow;
        hotkey.RaisePressed(pressedAt);
        hotkey.RaiseReleased(pressedAt.AddSeconds(10));

        shortPresses.Should().Be(1);
        pipeline.State.Should().Be(PipelineState.Idle);
    }

    [Fact]
    public void ShortPressDetected_CarriesTheTriggeringKeyName()
    {
        using var hotkey = new FakeHotkey();
        using var capture = new FakeAudioCapture();
        using var pipeline = new DictationPipeline(
            hotkey,
            capture,
            new FakeAsr("不应识别"),
            new FakeTextOutput(),
            new AppConfig());

        string? seen = null;
        pipeline.ShortPressDetected += (_, key) => seen = key;

        var t = DateTime.UtcNow;
        hotkey.RaisePressed(t, key: "x2");
        hotkey.RaiseReleased(t.AddSeconds(10), key: "x2");

        seen.Should().Be("x2");
    }

    private sealed class FakeHotkey : IGlobalHotkey
    {
        public event EventHandler<HotkeyEventArgs>? KeyPressed;
        public event EventHandler<HotkeyEventArgs>? KeyReleased;
        public event EventHandler<HotkeyObservedEventArgs>? KeyObserved
        {
            add { }
            remove { }
        }
        public bool IsRunning => true;
        public void Start() { }
        public void Stop() { }
        public void Dispose() { }
        public void RaisePressed(DateTime timestamp, string key = "caps_lock") => KeyPressed?.Invoke(this,
            new HotkeyEventArgs { Key = key, TimestampUtc = timestamp });
        public void RaiseReleased(DateTime timestamp, string key = "caps_lock") => KeyReleased?.Invoke(this,
            new HotkeyEventArgs { Key = key, TimestampUtc = timestamp });
    }

    private sealed class FakeAudioCapture : IAudioCapture
    {
        public int SampleRate => 16000;
        public bool IsRecording { get; private set; }
        public event EventHandler<AudioChunkEventArgs>? ChunkAvailable;
        public void Start() => IsRecording = true;
        public void Stop() => IsRecording = false;
        public IReadOnlyList<string> ListInputDevices() => Array.Empty<string>();
        public void Dispose() { }
        public void RaiseChunk(float[] samples) => ChunkAvailable?.Invoke(this,
            new AudioChunkEventArgs { Samples = samples });
    }

    private sealed class FakeAsr : IAsrEngine
    {
        private readonly string _text;
        public FakeAsr(string text) => _text = text;
        public string Name => "fake";
        public int SampleRate => 16000;
        public bool IsLoaded => true;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RecognitionResult> RecognizeAsync(
            ReadOnlyMemory<float> samples,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new RecognitionResult { Text = _text });
        public void Dispose() { }
    }

    private sealed class FakeTextOutput : ITextOutput
    {
        public Task TypeAsync(string text, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task PasteAsync(string text, bool restoreClipboard) => Task.CompletedTask;
    }
}
