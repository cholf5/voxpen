# 声写 (VoxPen)

一个用 C# / Avalonia 重写的离线中文语音输入法，脱胎于 [HaujetZhao/CapsWriter-Offline](https://github.com/HaujetZhao/CapsWriter-Offline)。

**核心改进**
- 单进程：告别原项目"客户端 + 服务端 + 两个黑色终端窗口"的部署方式
- 一个 Avalonia UI + 系统托盘，安静地在后台待命
- 长按 <kbd>CapsLock</kbd> 说话，松开自动识别并把结果打到当前光标位置
- 短按 <kbd>CapsLock</kbd> 保留原始大小写切换语义（自动补发按键）
- 100% 兼容原项目的 `hot-rule.txt`（正则/字面量 + 反向引用）

**当前状态**：Windows 10/11 x64 v2 P7（P1-P7 完成）。macOS/Linux 抽象层已就位，实现下一阶段跟进。

---

## 路线图

| 阶段 | 内容 | 状态 |
|---|---|---|
| P1 | 骨架 + 抽象接口 | ✅ |
| P2 | PortAudio + Paraformer 端到端（RTF ≈ 0.09） | ✅ |
| P3 | SharpHook CapsLock + SendInput 上屏 | ✅ |
| P4 | Avalonia 托盘 UI + 状态/历史/日志面板 | ✅ |
| P5 | hot-rule.txt + 末尾标点 + JSON 热重载 + 录音归档（后处理 10/10 通过） | ✅ |
| P6 | dotnet publish single-file（P6：~53 MB / P7 后：~100 MB） | ✅ |
| P7 | 文件批量转录 · 音素 RAG · 鼠标侧键 · Toast · Markdown 日记 · xUnit（122 tests） | ✅ |
| P7 收尾 | HotRule 复制修复 · hot-rule.txt 自动随 publish · verify-p7 冒烟 | ✅ |

---

## 快速开始（预编译版）

1. 下载 Release 中的 `VoxPen-win-x64.zip` 并解压到任意目录（例如 `D:\Apps\VoxPen\`）。
2. 下载 Paraformer 模型（约 229 MB）：推荐从原项目预打包的 [HaujetZhao/CapsWriter-Offline releases · models](https://github.com/HaujetZhao/CapsWriter-Offline/releases/tag/models) 下载 `Paraformer.zip`（含国内网盘镜像，SHA256 `a12a3f97...`）。也可用上游 [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx/releases) 的 `sherpa-onnx-paraformer-zh-2023-09-14`。
3. 解压后是一个 `speech_paraformer-large-vad-punc_asr_nat-zh-cn-16k-common-vocab8404-onnx/` 目录，把**它里面的内容**（不是这个目录本身）摊平到应用目录的 `models/paraformer/` 下：

   ```
   D:\Apps\VoxPen\
     ├─ VoxPen.App.exe
     ├─ config.json                  ← 首次启动自动生成
     ├─ hot-rule.txt                 ← publish 时自动附带；用户可编辑
     └─ models/
        └─ paraformer/
           ├─ model.onnx            (或 model.int8.onnx)
           ├─ tokens.txt
           └─ ...                    (am.mvn / config.yaml 等其余附属文件全部保留)
   ```

4. 双击 `VoxPen.App.exe` 启动。任务栏右下角会出现一个蓝底 "C" 托盘图标。首次启动模型加载约 2–3 秒。
5. 把鼠标点到任意可输入的位置，**按住 CapsLock 说话，松开自动上屏**。

---

## 使用

### 快捷键
- 长按 <kbd>CapsLock</kbd> ≥ 0.3 秒：录音，松开后识别并上屏
- 短按 <kbd>CapsLock</kbd> < 0.3 秒：跳过录音，自动补发一次 CapsLock（保留大小写切换）
- 长按期间 CapsLock 的系统切换行为被抑制（可在 `config.json` 里关闭）

### 托盘菜单
- **显示主窗口** — 状态灯、识别历史、日志
- **暂停/继续监听**
- **打开配置文件夹**
- **退出**

### 主窗口
- **识别历史**：最近 50 条识别结果，可复制最新一条
- **设置**：编辑模型目录、查看模型有效状态，并通过下拉框选择和保存快捷键；模型目录和快捷键保存后需重启应用生效
- **日志**：实时滚动模型加载 / hot-rule 重载 / 识别错误等事件

关闭按钮不会退出应用，而是最小化到托盘。真正退出请用托盘菜单或主窗口底部的"退出"按钮。

### 配置文件 `config.json`

首次启动自动在应用同目录生成。字段来自原项目 `config_client.py`，P7 新增 `transcribe / hotword / notification` 三节 + `shortcut.keys` + `audio.diaryEnabled`（老配置文件不需改动，缺字段自动填默认值）：

```json
{
  "shortcut": {
    "key": "caps_lock",
    "keys": ["caps_lock", "x2"],         // 新：多快捷键；非空时优先。支持 caps_lock/f13-f16/x1/x2
    "suppress": true,
    "shortPressThresholdSeconds": 0.3
  },
  "audio": {
    "inputDevice": null,
    "saveRecording": true,
    "audioNameLength": 20,
    "diaryEnabled": true                  // 新：写 recordings/YYYY/MM/DD.md
  },
  "asr":     { "engine": "paraformer", "modelDir": "models/paraformer", "numThreads": 2, "provider": "cpu" },
  "output":  { "mode": "Type", "restoreClipboard": true, "pasteApps": ["WeiXin.exe", "Telegram.exe"] },
  "postprocess": {
    "enableHotRule": true,
    "hotRulePath": "hot-rule.txt",
    "trashPunctuation": "，。,.",
    "trashPuncThreshold": 8,
    "trashPuncApps": ["WeiXin.exe"]
  },
  "transcribe": {                          // 新：批量转录
    "segDurationSeconds": 60,
    "segOverlapSeconds": 4,
    "saveSrt": true,
    "saveTxt": true,
    "saveJson": true,
    "saveMerge": false
  },
  "hotword": {                             // 新：音素 RAG（独立于 hot-rule 正则）
    "enablePhonemeRag": true,
    "hotwordPath": "hot.txt",
    "matchThreshold": 0.85,
    "similarThreshold": 0.6
  },
  "notification": {                        // 新：Toast 通知
    "enabled": true,
    "showOnRecordingStart": false,
    "showOnResult": true,
    "showOnError": true
  },
  "logLevel": "Information"
}
```

**热重载**：修改 `config.json` / `hot-rule.txt` / `hot.txt` 后 3 秒自动生效（无需重启）。快捷键 / 模型路径 / 日记根目录的改动仍需重启。

### `hot-rule.txt`

原项目文件可直接拿来用，无需转换。示例：

```text
毫安时     =      mAh
赫兹       =      Hz
(艾特)\s*(\w+)\s*(点)\s*(\w+)    =     @\2.\4
欧拉玛     =  Ollama
```

- `#` 起始为注释
- 左侧当正则；正则编译失败则视为字面量
- 右侧支持 `\1..\n` 反向引用与 `\s` (空格)

### 录音归档

`config.audio.saveRecording=true` 时，每次识别都会把 WAV + 同名 txt 存到：

```
recordings/2026/07/assets/20260709-223245_你好世界.wav
                                        _你好世界.txt
```

不需要时把 `saveRecording` 改为 `false`。

---

## P7 新特性

### 文件批量转录（离线字幕）

把一段/多段音频转成 `.txt / .srt / .json / .merge.txt` 四件套。默认按 60 秒切片 + 4 秒重叠，`SegmentMerger` 用 SequenceMatcher 做 token 级去重拼接，`SubtitleAligner` 输出标准 SRT 时间戳。

```bash
dotnet run --project src/VoxPen.Cli -- transcribe path/to/audio.mp3 another.wav
# 可选参数：
#   --seg-duration 60   --seg-overlap 4
#   --no-srt --no-json --no-txt --merge
#   --model <dir>
```

输入格式：`.wav / .mp3 / .m4a / .aac / .wma / .flac / .mp4 / .ogg / .opus`（走 Windows Media Foundation 解码 + 重采样到 16 kHz mono）。

### 音素 RAG 热词（`hot.txt`）

100% 兼容原项目的 `hot.txt`：一行一个热词，支持 `|` 分隔别名（第一项为目标）、`~~~` 后紧跟黑名单窗口词。用 `ToolGood.Words.Pinyin` 抽取声/韵/调音素序列，锚点扫描 + DP 距离找候选，右到左 splice 替换。

```text
撒贝宁
北大青鸟|beidaqingniao|BDQN
GitHub|吉他不
先|xiān|xian ~~~ 首先 优先 领先
```

默认阈值 0.85（低于则不替换）；相似阈值 0.6 用于 UI 提示。可在 `config.json` 里调，也可直接改 `hot.txt` 后 3 秒热重载。

### 鼠标侧键 + 多快捷键

`shortcut.keys` 支持任意组合，例如 `["caps_lock", "x2"]` 表示 CapsLock 和鼠标 X2（前进键）任一按下都会触发。已支持的键名：`caps_lock / f13..f16 / x1 / x2 / mouse_left / mouse_right / mouse_middle`。

### Toast 通知

`notification.showOnResult=true` 时，识别完成会在系统右下角弹一条含结果预览的 Toast；出错时弹错误 Toast。首次触发会自动创建 AUMID 和开始菜单快捷方式（Windows 10 1903+）。旧系统会静默降级。

### Markdown 日记

`audio.diaryEnabled=true` 时，每条识别追写到 `recordings/YYYY/MM/DD.md`：

```markdown
[12:34:56](assets/20260709-123456_你好世界.wav) 你好世界

[12:35:10](assets/20260709-123510_下一句.wav) 下一句
```

首次创建文件会自动写入 header —— 一段 Typora 里能一键把音频链接换成 `<audio controls>` 控件的正则替换 Tip。

### xUnit 单元测试套件

```bash
dotnet test tests/VoxPen.Core.Tests/
```

覆盖 SequenceMatcher / SmartSplit / SegmentMerger / SubtitleAligner / SrtWriter / TranscriptJsonWriter / PhonemeExtractor / FastRag / PhonemeCorrector / HotwordFile / HotRuleReplacer / TrashPuncCleaner / DiaryWriter / AudioSegmenter / FileTranscriber，共 **122 test cases 全绿**。

---

## 从源码构建

前置：.NET 8 SDK。

```bash
dotnet build src/VoxPen.App
```

### 打包单文件 exe（Windows）

```bash
dotnet publish src/VoxPen.App/VoxPen.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish/win-x64
```

产物：`publish/win-x64/VoxPen.App.exe`（P7 起约 100 MB，P6 时约 53 MB —— 增大来自 NAudio + Toolkit.Uwp.Notifications + ToolGood.Words.Pinyin 等 P7 依赖。自包含 .NET 运行时 + sherpa-onnx / PortAudio / SharpHook / MediaFoundation 所有原生依赖）。

publish 目录会自动带上 `hot-rule.txt`；`config.json` 首次运行会在 exe 同目录自动生成默认值。用户只需自己放 `models/paraformer/` 就能开箱即用。

### CLI 冒烟测试

```bash
# 后处理端到端（HotRule + TrashPunc）
dotnet run --project src/VoxPen.Cli -- test-postprocess

# 音素 RAG 冒烟（内置样例，不加载模型）
dotnet run --project src/VoxPen.Cli -- test-hotword

# Markdown 日记冒烟（写到临时目录）
dotnet run --project src/VoxPen.Cli -- test-diary

# 段合并冒烟（模拟 3 段重叠文本）
dotnet run --project src/VoxPen.Cli -- test-merger

# 从 WAV 直接识别
dotnet run --project src/VoxPen.Cli -- --file models/paraformer/example/asr_example.wav

# 批量转录
dotnet run --project src/VoxPen.Cli -- transcribe path/to/audio.mp3

# 无 UI 常驻模式（真实 CapsLock 监听）
dotnet run --project src/VoxPen.Cli -- run
```

---

## 项目结构

```
VoxPen/
├─ src/
│  ├─ VoxPen.Core/              抽象接口 + Pipeline 状态机 + 后处理 + 归档 + 配置 + 转录/日记/音素 RAG
│  ├─ VoxPen.Platform.Windows/  SharpHook + PortAudio + sherpa-onnx + SendInput + MediaFoundation + Toast
│  ├─ VoxPen.App/               Avalonia UI + Tray + 组合根 (AppHost)
│  └─ VoxPen.Cli/               无 UI 冒烟测试 + 批量转录
├─ tests/VoxPen.Core.Tests/     xUnit 测试套件（124 tests）
├─ models/paraformer/                    你自己放模型
├─ hot-rule.txt                          可选，与原项目 100% 兼容（正则替换）
├─ hot.txt                               可选，音素 RAG 热词（原项目格式）
├─ config.json                           首次启动自动生成
└─ recordings/                           录音归档 + Markdown 日记（可关）
   └─ 2026/07/
      ├─ 09.md                           当日日记
      └─ assets/                         WAV + 侧车 txt
```

Core 抽象接口（供 macOS / Linux 增量实现）：`IGlobalHotkey · IAudioCapture · ITextOutput · IAsrEngine · IForegroundApp · IAudioDecoder · INotificationService`

---

## 已知限制 / 下一轮路线

P7 后仍未实现（下一轮 v2 计划）：

- LLM 润色 / 角色系统（chat prompt）
- UDP 广播 / UDP 远程控制
- Fun-ASR / Qwen3-ASR 引擎
- macOS / Linux 平台实现（Core 抽象已就位）
- 繁体中文转换、Chinese ITN

---

## 权限提示

- **首次启动**会请求麦克风权限（Windows 10/11 系统对话框）
- **向管理员窗口输入**：目标窗口以管理员启动时，声写也需以管理员启动才能 SendInput
- **杀毒软件**：单文件 exe 内含大量本地 DLL，可能被误报，请添加到白名单

---

## 开发者备忘

### 目录/软链陷阱

**不要**用 `mklink /J` 建 junction 让 `publish/` 目录借用 `models/`。PowerShell `Remove-Item -Recurse` 处理 junction 时会**跟穿**，把目标目录（真的 models）里的内容删光；`-Exclude` 参数只保护 junction 本身，不阻止跟穿。要清理带 junction 的目录，先用 `cmd /c rd <junction>` 拆链，再操作父目录；或直接 `Copy-Item -Recurse` 拷贝模型（250 MB，ReFS/SSD 上瞬间完成），从根本避免这个陷阱。

### 首次开箱即用

- `hot-rule.txt` 通过 `App.csproj` 的 `<None CopyToOutputDirectory>` 随 `build` / `publish` 自动带到输出目录
- `config.json` 由 `AppHost.LoadOrCreateConfig` 首次启动写默认（含 P7 的 `transcribe / hotword / notification` 三节）
- `hot.txt` 用户按需自己放，不会自动生成
- 缺 `models/paraformer/` 时启动会 fail-fast 报 `Model directory not found`，日志给出明确路径

### 后台冒烟测试环境变量

- `CAPSWRITER_LOG_FILE=<path>` — 把 App 的 Emit 日志实时写到文件
- `CAPSWRITER_AUTO_EXIT_SECS=<n>` — n 秒后自动 shutdown（无 UI 冒烟专用）

组合使用可在 CI / 脚本里做启动冒烟验证，见 `publish/verify-p7/` 目录的一次性演示。

---

## 致谢

- [HaujetZhao/CapsWriter-Offline](https://github.com/HaujetZhao/CapsWriter-Offline) — 原项目 / 交互设计 / 配置约定
- [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) — ASR 引擎 + PortAudioSharp2
- [TolikPylypchuk/SharpHook](https://github.com/TolikPylypchuk/SharpHook) — 跨平台全局键盘 hook
- [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) — 跨平台 XAML UI

## 许可

MIT，跟随原项目。
