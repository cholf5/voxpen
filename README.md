<div align="center">

# 🎙️ VoxPen · 声写

**An offline Chinese voice-to-text input helper — hold <kbd>CapsLock</kbd>, speak, release.**

A modern C# / .NET 8 / Avalonia rewrite of
[HaujetZhao/CapsWriter-Offline](https://github.com/HaujetZhao/CapsWriter-Offline).

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform: Windows 10/11 x64](https://img.shields.io/badge/Platform-Windows%2010%2F11%20x64-0078D6?logo=windows&logoColor=white)](#-quick-start-prebuilt)
[![UI: Avalonia 12](https://img.shields.io/badge/UI-Avalonia%2012-8B44AC)](https://avaloniaui.net/)
[![ASR: sherpa-onnx](https://img.shields.io/badge/ASR-Paraformer%20%2F%20sherpa--onnx-orange)](https://github.com/k2-fsa/sherpa-onnx)
[![Tests: 122 ✓](https://img.shields.io/badge/xUnit-122%20green-brightgreen)](tests/VoxPen.Core.Tests)

**[简体中文说明 →](README.zh-CN.md)**

</div>

---

## ✨ Highlights

- 🧩 **One process, one tray icon.** No more "client + server + two black terminal windows" — everything lives in a single Avalonia app that quietly waits in the system tray.
- ⌨️ **Push-to-talk on <kbd>CapsLock</kbd>.** Hold to record, release to transcribe and type the result into whatever window has focus.
- 🔡 **Short press keeps its native meaning.** A tap shorter than 0.3 s still toggles CapsLock (VoxPen re-emits the key for you).
- 🔁 **100% `hot-rule.txt` compatible.** Regex/literal replacements with `\1..\n` backreferences behave exactly like the upstream Python project — bring your existing files.
- 🧠 **Four offline ASR engines.** Choose Paraformer, SenseVoice-Small, Fun-ASR-Nano, or Qwen3-ASR from Settings; download compatible models in-app with live progress and resume.
- 🧠 **Phoneme RAG hot-words.** `hot.txt` is loaded through a Chinese pinyin phoneme index for fuzzy correction that survives common ASR mishears.
- 📂 **Offline batch transcription.** Turn `.mp3 / .m4a / .wav / .flac / .mp4 / .opus / …` into `.txt / .srt / .json / .merge.txt` from the CLI.
- 📓 **Optional Markdown diary.** Every utterance is archived with its WAV under `recordings/YYYY/MM/DD.md`, one Typora regex away from an inline `<audio controls>` player.
- 🔥 **Hot reload.** Edits to `config.json`, `hot-rule.txt`, and `hot.txt` apply in ~3 s without restarting.
- 📦 **Single-file exe.** `dotnet publish` produces one self-contained `~100 MB` executable; models use fixed directories under `models/`.

> **Status:** Windows 10/11 x64 · v2 P7 shipped (P1–P7 complete). macOS / Linux abstractions are already in `VoxPen.Core`; concrete implementations are the next milestone.

---

## 📸 Overview

```
┌─────────────────────────────────────────────────────────┐
│  Global hotkey (CapsLock / mouse X-button / F13..F16)  │
│                        │                                │
│                        ▼                                │
│  ┌─────────────┐  ┌──────────────┐  ┌───────────────┐  │
│  │ PortAudio   │─▶│  Paraformer  │─▶│  hot-rule +   │  │
│  │ 16 kHz mono │  │  sherpa-onnx │  │  hot.txt RAG  │  │
│  └─────────────┘  └──────────────┘  └───────┬───────┘  │
│                                              ▼          │
│                                    ┌─────────────────┐  │
│                                    │  SendInput to   │  │
│                                    │  focused window │  │
│                                    └─────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## 🗺️ Roadmap

| Phase | Scope | Status |
| ----- | ----- | :----: |
| P1 | Solution skeleton + Core abstractions | ✅ |
| P2 | PortAudio + Paraformer end-to-end (RTF ≈ 0.09) | ✅ |
| P3 | SharpHook `CapsLock` + `SendInput` output | ✅ |
| P4 | Avalonia tray UI + status / history / log panels | ✅ |
| P5 | `hot-rule.txt` + trailing-punct + JSON hot-reload + recording archive (post-process 10/10) | ✅ |
| P6 | `dotnet publish` single-file (~53 MB) | ✅ |
| P7 | Batch transcribe · Phoneme RAG · Mouse side-buttons · Toast · Markdown diary · xUnit (122 tests) | ✅ |
| P7 wrap-up | HotRule copy fix · `hot-rule.txt` bundled by publish · `verify-p7` smoke | ✅ |

**Next up (v2 planning):** LLM polish / role prompts · UDP broadcast · Fun-ASR / Qwen3-ASR engines · macOS / Linux platform impls · Traditional Chinese & Chinese ITN.

---

## 🚀 Quick start (prebuilt)

1. **Download the latest release** — grab `VoxPen-win-x64.zip` from the [Releases](../../releases) page and extract it anywhere, e.g. `D:\Apps\VoxPen\`.
2. **Fetch a Paraformer model** (~229 MB). Recommended: the pre-packaged `Paraformer.zip` from [HaujetZhao/CapsWriter-Offline releases · models](https://github.com/HaujetZhao/CapsWriter-Offline/releases/tag/models) (mirrors on Chinese cloud drives, SHA-256 `a12a3f97...`). Upstream `sherpa-onnx-paraformer-zh-2023-09-14` from [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx/releases) works too.
3. **(Optional) Fetch a punctuation model** (~278 MB). Paraformer itself does not emit punctuation — grab the sherpa-onnx CT-Transformer punctuation model `sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12.tar.bz2` from [k2-fsa/sherpa-onnx-releases](https://github.com/k2-fsa/sherpa-onnx/releases/tag/punctuation-models) (mirror of the FunASR CT-Transformer). Without it, transcripts are punctuation-free and the trailing-punct heuristics in `hot-rule.txt` still work.
4. **Flatten the model directories** next to the exe. Copy the *contents* of each extracted folder, not the folder itself:

   ```
   D:\Apps\VoxPen\
     ├─ VoxPen.App.exe
     ├─ config.json         ← auto-generated on first launch
     ├─ hot-rule.txt        ← bundled by publish; edit freely
     └─ models\
        ├─ paraformer\
        │  ├─ model.onnx          (or model.int8.onnx)
        │  ├─ tokens.txt
        │  └─ …                   (am.mvn / config.yaml / …)
        └─ Punct-CT-Transformer\
           └─ sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12\
              ├─ model.onnx
              └─ …                (tokens.json / etc.)
   ```

5. **Launch `VoxPen.App.exe`.** A blue "C" icon appears in the system tray. First-time model load takes about 2–3 s.
6. **Click into any text field, hold <kbd>CapsLock</kbd>, speak, release.** The transcript is typed at your cursor.

---

## 🖱️ Usage

### Hotkeys

| Gesture | Behaviour |
| ------- | --------- |
| Hold <kbd>CapsLock</kbd> ≥ 0.3 s | Record, then transcribe & type on release |
| Tap <kbd>CapsLock</kbd> < 0.3 s | Skip recording; re-emit CapsLock so the OS toggle still works |
| During hold | System CapsLock toggle is suppressed (togglable in `config.json`) |

### Tray menu

- **Show main window** — status light, recognition history, live log
- **Pause / resume listening**
- **Open config folder**
- **Exit**

### Main window

- **History** — the last 50 transcripts, one-click copy the latest
- **Settings** — choose/download a model, live download progress, model validity indicator, hotkey picker, then use one **Save and apply** button to activate both choices immediately
- **Log** — live stream of model load, hot-rule reloads, recognition errors, etc.

> The window's close button minimizes to tray. Use the tray menu or the in-window "Exit" button to actually quit.

### `config.json`

Auto-created on first launch next to the exe. Field names come from the upstream Python project; VoxPen only *adds* keys (`transcribe / hotword / notification / shortcut.keys / audio.diaryEnabled`) — old config files keep working, missing keys are filled with defaults.

```json
{
  "shortcut": {
    "key": "caps_lock",
    "keys": ["caps_lock", "x2"],
    "suppress": true,
    "shortPressThresholdSeconds": 0.3
  },
  "audio": {
    "inputDevice": null,
    "saveRecording": true,
    "audioNameLength": 20,
    "diaryEnabled": true
  },
  "asr":     { "engine": "Paraformer", "modelDir": "models/paraformer", "numThreads": 2, "provider": "cpu" },
  "punctuation": {
    "modelDir": "models/Punct-CT-Transformer/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12",
    "numThreads": 2,
    "provider": "cpu"
  },
  "output":  { "mode": "Type", "restoreClipboard": true, "pasteApps": ["WeiXin.exe", "Telegram.exe"] },
  "postprocess": {
    "enableHotRule": true,
    "hotRulePath": "hot-rule.txt",
    "trashPunctuation": "，。,.",
    "trashPuncThreshold": 8,
    "trashPuncApps": ["WeiXin.exe"]
  },
  "transcribe": {
    "segDurationSeconds": 60,
    "segOverlapSeconds": 4,
    "saveSrt": true,
    "saveTxt": true,
    "saveJson": true,
    "saveMerge": false
  },
  "hotword": {
    "enablePhonemeRag": true,
    "hotwordPath": "hot.txt",
    "matchThreshold": 0.85,
    "similarThreshold": 0.6
  },
  "notification": {
    "enabled": true,
    "showOnRecordingStart": false,
    "showOnError": true
  },
  "logLevel": "Information"
}
```

**Models:** Settings downloads official sherpa-onnx-compatible packages to fixed paths under `models/`, shows progress/speed, and resumes an interrupted download. The supported engine values are `Paraformer`, `SenseVoice`, `FunAsrNano`, and `QwenAsr`. Qwen3-ASR does not provide word timestamps. To reuse an existing model, copy its files into the matching fixed directory; model paths cannot be selected in the app or CLI.

**Hot reload:** edits to `config.json` / `hot-rule.txt` / `hot.txt` are picked up within ~3 s. In Settings, saving a hotkey or ASR model selection applies it immediately; diary root changes still require a restart. Legacy `asr.modelDir` and `punctuation.modelDir` values are ignored.

**Supported keys for `shortcut.keys`:** `caps_lock`, `f13`..`f16`, `x1`, `x2`, `mouse_left`, `mouse_right`, `mouse_middle`. Any non-empty combination triggers on either key.

### `hot-rule.txt`

Drop in your existing file from CapsWriter-Offline unchanged.

```text
毫安时     =      mAh
赫兹       =      Hz
(艾特)\s*(\w+)\s*(点)\s*(\w+)    =     @\2.\4
欧拉玛     =  Ollama
```

- Lines starting with `#` are comments.
- Left side is parsed as a regex; if regex compilation fails, it falls back to a literal match.
- Right side supports `\1..\n` back-references and `\s` (space).

### Recording archive

When `config.audio.saveRecording = true`, each transcript writes a WAV + sidecar `.txt`:

```
recordings/2026/07/assets/20260709-223245_你好世界.wav
                                        _你好世界.txt
```

Set it to `false` to disable.

---

## 🧪 P7 features

### Batch transcription (offline subtitles)

Turn one or many audio files into `.txt / .srt / .json / .merge.txt`. Defaults: 60 s slice + 4 s overlap. `SegmentMerger` de-duplicates overlaps at token level with a SequenceMatcher; `SubtitleAligner` emits standard SRT timestamps.

```bash
dotnet run --project src/VoxPen.Cli -- transcribe path/to/audio.mp3 another.wav
# Optional flags:
#   --seg-duration 60   --seg-overlap 4
#   --no-srt --no-json --no-txt --merge
#   --model <dir>
```

Supported inputs: `.wav / .mp3 / .m4a / .aac / .wma / .flac / .mp4 / .ogg / .opus` (decoded by Windows Media Foundation, resampled to 16 kHz mono).

### Phoneme-RAG hot-words (`hot.txt`)

Fully compatible with the upstream `hot.txt` format: one word per line, `|` for aliases (first entry is the target), `~~~` followed by blacklist context words to suppress false positives. `ToolGood.Words.Pinyin` extracts initial / final / tone phoneme sequences; anchor scan + DP distance selects candidates; splices are applied right-to-left.

```text
撒贝宁
北大青鸟|beidaqingniao|BDQN
GitHub|吉他不
先|xiān|xian ~~~ 首先 优先 领先
```

Default match threshold 0.85 (below → no replacement). Similar threshold 0.6 is used for UI hints. Tune in `config.json` or edit `hot.txt` directly — hot-reloaded in ~3 s.

### Mouse side buttons & multi-hotkey

`shortcut.keys` accepts any combination — e.g. `["caps_lock", "x2"]` fires on either CapsLock or the mouse "forward" button.

### Toast notifications

No Toast on successful recognition (would be noisy). Errors optionally raise a Toast via `notification.showOnError`. First fire auto-registers an AUMID and a Start-menu shortcut (Windows 10 1903+). Older systems degrade silently.

### Markdown diary

When `audio.diaryEnabled = true`, each transcript is appended to `recordings/YYYY/MM/DD.md`:

```markdown
[12:34:56](assets/20260709-123456_你好世界.wav) 你好世界

[12:35:10](assets/20260709-123510_下一句.wav) 下一句
```

A first-time header ships with a Typora regex-replace tip that swaps the audio link for a native `<audio controls>` player.

### xUnit test suite

```bash
dotnet test tests/VoxPen.Core.Tests/
```

Covers SequenceMatcher, SmartSplit, SegmentMerger, SubtitleAligner, SrtWriter, TranscriptJsonWriter, PhonemeExtractor, FastRag, PhonemeCorrector, HotwordFile, HotRuleReplacer, TrashPuncCleaner, DiaryWriter, AudioSegmenter, FileTranscriber — **122 test cases, all green.**

---

## 🛠️ Building from source

Prerequisite: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build src/VoxPen.App
```

### Single-file publish (Windows)

```bash
dotnet publish src/VoxPen.App/VoxPen.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish/win-x64
```

Output: `publish/win-x64/VoxPen.App.exe` (~100 MB from P7 on, up from ~53 MB at P6 — growth is NAudio + Toolkit.Uwp.Notifications + ToolGood.Words.Pinyin). The exe self-contains the .NET runtime and every native dependency (sherpa-onnx, PortAudio, SharpHook, MediaFoundation).

`hot-rule.txt` is bundled into `publish/` automatically. `config.json` is written on first launch next to the exe. Users can download a model in Settings, or copy an existing model into its fixed directory under `models/`.

### CLI smoke tests

```bash
# Post-process end-to-end (HotRule + TrashPunc)
dotnet run --project src/VoxPen.Cli -- test-postprocess

# Punctuation model smoke (loads CT-Transformer, needs the punct model directory)
dotnet run --project src/VoxPen.Cli -- test-punc "你好世界这是一段没有标点的文本"

# Phoneme RAG smoke (built-in fixtures, no model load)
dotnet run --project src/VoxPen.Cli -- test-hotword

# Markdown diary smoke (writes to temp dir)
dotnet run --project src/VoxPen.Cli -- test-diary

# Segment merger smoke (simulated 3-way overlap)
dotnet run --project src/VoxPen.Cli -- test-merger

# Transcribe a WAV directly
dotnet run --project src/VoxPen.Cli -- --file models/paraformer/example/asr_example.wav

# Batch file transcription
dotnet run --project src/VoxPen.Cli -- transcribe path/to/audio.mp3

# Headless CapsLock listener (no UI)
dotnet run --project src/VoxPen.Cli -- run
```

---

## 🧱 Project structure

```
VoxPen/
├─ src/
│  ├─ VoxPen.Core/              Abstractions · Pipeline state machine · Post-process
│  │                            Config · Archive · Transcribe · Diary · Phoneme RAG
│  ├─ VoxPen.Platform.Windows/  SharpHook · PortAudio · sherpa-onnx · SendInput
│  │                            MediaFoundation · UWP Toast
│  ├─ VoxPen.App/               Avalonia UI · Tray · AppHost (composition root)
│  └─ VoxPen.Cli/               Headless smoke tests + batch transcribe
├─ tests/VoxPen.Core.Tests/     xUnit test suite (122 tests)
├─ models/paraformer/           (your ASR model — gitignored)
├─ models/Punct-CT-Transformer/ (optional punctuation model — gitignored)
├─ hot-rule.txt                 optional · 100% upstream-compatible regex rules
├─ hot.txt                      optional · phoneme-RAG hot-words
├─ config.json                  auto-generated on first launch
└─ recordings/                  audio archive + Markdown diary (opt-out)
   └─ 2026/07/
      ├─ 09.md
      └─ assets/
```

**Core abstractions** (ready for macOS / Linux implementations):
`IGlobalHotkey · IAudioCapture · ITextOutput · IAsrEngine · IForegroundApp · IAudioDecoder · INotificationService`

---

## ⚠️ Permissions & caveats

- **Microphone.** Windows prompts on first launch.
- **SmartScreen.** The release exe is not code-signed; SmartScreen will warn on first run. Click **More info → Run anyway**; the warning won't re-appear.
- **Elevated targets.** To `SendInput` into an administrator window, VoxPen itself must run elevated.
- **Antivirus false positives.** A single-file exe packs many native DLLs — allow-list if flagged.

---

## 🧠 Developer notes

### Filesystem trap: don't junction `publish/models` → `models/`

PowerShell `Remove-Item -Recurse` **follows junctions** and will wipe the real `models/` tree. `-Exclude` only shields the junction name itself, not the traversal. Use `cmd /c rd <junction>` to detach, or just `Copy-Item -Recurse` the model (~250 MB, instant on ReFS/SSD).

### Out-of-the-box behaviour

- `hot-rule.txt` ships with `build` / `publish` via `<None CopyToOutputDirectory>` in `VoxPen.App.csproj`.
- `config.json` is written on first launch by `AppHost.LoadOrCreateConfig` (includes P7 sections `transcribe / hotword / notification`).
- `hot.txt` is opt-in — users drop their own, no default file is generated.
- Missing `models/paraformer/` fails fast with `Model directory not found` and a clear log path — do not paper over this.
- Missing punctuation model degrades gracefully to `NullPunctuator` (transcripts stay punctuation-free); a warning is logged pointing at `punctuation.modelDir`.

### Headless / CI env vars

- `CAPSWRITER_LOG_FILE=<path>` — mirror App log to a file.
- `CAPSWRITER_AUTO_EXIT_SECS=<n>` — auto-shutdown after `n` seconds (for smoke runs).

See `publish/verify-p7/` for a one-shot boot-and-shutdown example.

---

## 🙏 Credits

- **[HaujetZhao/CapsWriter-Offline](https://github.com/HaujetZhao/CapsWriter-Offline)** — original project, UX design, config conventions.
- **[k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx)** — ASR engine + PortAudioSharp2.
- **[TolikPylypchuk/SharpHook](https://github.com/TolikPylypchuk/SharpHook)** — cross-platform global keyboard hook.
- **[AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia)** — cross-platform XAML UI.
- **[ToolGood.Words.Pinyin](https://github.com/toolgood/ToolGood.Words)** — pinyin/phoneme extraction.

---

## 📄 License

[MIT](LICENSE), matching the upstream project.
