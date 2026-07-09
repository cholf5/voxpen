# CapsWriter Sharp

一个用 C# / Avalonia 重写的离线中文语音输入法，脱胎于 [HaujetZhao/CapsWriter-Offline](https://github.com/HaujetZhao/CapsWriter-Offline)。

**核心改进**
- 单进程：告别原项目"客户端 + 服务端 + 两个黑色终端窗口"的部署方式
- 一个 Avalonia UI + 系统托盘，安静地在后台待命
- 长按 <kbd>CapsLock</kbd> 说话，松开自动识别并把结果打到当前光标位置
- 短按 <kbd>CapsLock</kbd> 保留原始大小写切换语义（自动补发按键）
- 100% 兼容原项目的 `hot-rule.txt`（正则/字面量 + 反向引用）

**当前状态**：Windows 10/11 x64 MVP（P1-P6 完成）。macOS/Linux 抽象层已就位，实现下一阶段跟进。

---

## 路线图

| 阶段 | 内容 | 状态 |
|---|---|---|
| P1 | 骨架 + 抽象接口 | ✅ |
| P2 | PortAudio + Paraformer 端到端（RTF ≈ 0.09） | ✅ |
| P3 | SharpHook CapsLock + SendInput 上屏 | ✅ |
| P4 | Avalonia 托盘 UI + 状态/历史/日志面板 | ✅ |
| P5 | hot-rule.txt + 末尾标点 + JSON 热重载 + 录音归档（后处理 10/10 通过） | ✅ |
| P6 | dotnet publish single-file（~53 MB） | ✅ |

---

## 快速开始（预编译版）

1. 下载 Release 中的 `CapsWriter-Sharp-win-x64.zip` 并解压到任意目录（例如 `D:\Apps\CapsWriter\`）。
2. 下载 Paraformer 模型（约 220 MB）：来自 [k2-fsa/sherpa-onnx 官方发行版](https://github.com/k2-fsa/sherpa-onnx/releases) —— `sherpa-onnx-paraformer-zh-2023-09-14`。
3. 把模型文件放到应用目录的 `models/paraformer/` 下：

   ```
   D:\Apps\CapsWriter\
     ├─ CapsWriterSharp.App.exe
     ├─ config.json                  ← 首次启动自动生成
     ├─ hot-rule.txt                 ← 可选，兼容原项目格式
     └─ models/
        └─ paraformer/
           ├─ model.onnx            (或 model.int8.onnx)
           └─ tokens.txt
   ```

4. 双击 `CapsWriterSharp.App.exe` 启动。任务栏右下角会出现一个蓝底 "C" 托盘图标。首次启动模型加载约 2–3 秒。
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
- **设置**：查看当前生效的模型路径、快捷键、上屏模式（MVP 阶段只读，编辑请改 `config.json`）
- **日志**：实时滚动模型加载 / hot-rule 重载 / 识别错误等事件

关闭按钮不会退出应用，而是最小化到托盘。真正退出请用托盘菜单或主窗口底部的"退出"按钮。

### 配置文件 `config.json`

首次启动自动在应用同目录生成。字段来自原项目 `config_client.py`：

```json
{
  "shortcut":   { "key": "caps_lock", "suppress": true, "shortPressThresholdSeconds": 0.3 },
  "audio":      { "inputDevice": null, "saveRecording": true, "audioNameLength": 20 },
  "asr":        { "engine": "paraformer", "modelDir": "models/paraformer", "numThreads": 2, "provider": "cpu" },
  "output":     { "mode": "Type", "restoreClipboard": true, "pasteApps": ["WeiXin.exe", "Telegram.exe"] },
  "postprocess":{
    "enableHotRule": true,
    "hotRulePath": "hot-rule.txt",
    "trashPunctuation": "，。,.",
    "trashPuncThreshold": 8,
    "trashPuncApps": ["WeiXin.exe"]
  },
  "logLevel": "Information"
}
```

**热重载**：修改 `config.json` 或 `hot-rule.txt` 后 3 秒自动生效（无需重启）。快捷键 / 模型路径的改动仍需重启。

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

## 从源码构建

前置：.NET 8 SDK。

```bash
dotnet build src/CapsWriterSharp.App
```

### 打包单文件 exe（Windows）

```bash
dotnet publish src/CapsWriterSharp.App/CapsWriterSharp.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish/win-x64
```

产物：`publish/win-x64/CapsWriterSharp.App.exe`（约 53 MB，自包含 .NET 运行时 + sherpa-onnx / PortAudio / SharpHook 全部原生依赖）。

### CLI 冒烟测试

```bash
# 后处理端到端（HotRule + TrashPunc）
dotnet run --project src/CapsWriterSharp.Cli -- test-postprocess

# 从 WAV 直接识别
dotnet run --project src/CapsWriterSharp.Cli -- --file models/paraformer/example/asr_example.wav

# 无 UI 常驻模式（真实 CapsLock 监听）
dotnet run --project src/CapsWriterSharp.Cli -- run
```

---

## 项目结构

```
CapsWriterSharp/
├─ src/
│  ├─ CapsWriterSharp.Core/              抽象接口 + Pipeline 状态机 + 后处理 + 归档 + 配置
│  ├─ CapsWriterSharp.Platform.Windows/  SharpHook + PortAudio + sherpa-onnx + SendInput
│  ├─ CapsWriterSharp.App/               Avalonia UI + Tray + 组合根 (AppHost)
│  └─ CapsWriterSharp.Cli/               无 UI 冒烟测试用
├─ models/paraformer/                    你自己放模型
├─ hot-rule.txt                          可选，与原项目 100% 兼容
├─ config.json                           首次启动自动生成
└─ recordings/                           录音归档（可关）
```

Core 抽象接口（供 macOS / Linux 增量实现）：`IGlobalHotkey · IAudioCapture · ITextOutput · IAsrEngine · IForegroundApp`

---

## 已知限制 / v2 路线

MVP 精简，以下功能暂未移植：

- 音素 RAG 热词、LLM 润色角色系统
- 文件批量转录、SRT/JSON 输出
- Toast、Markdown 日记
- UDP 广播 / UDP 控制
- Fun-ASR / Qwen3-ASR 引擎
- 鼠标侧键（X1/X2）快捷键
- macOS / Linux 平台实现

---

## 权限提示

- **首次启动**会请求麦克风权限（Windows 10/11 系统对话框）
- **向管理员窗口输入**：目标窗口以管理员启动时，CapsWriter 也需以管理员启动才能 SendInput
- **杀毒软件**：单文件 exe 内含大量本地 DLL，可能被误报，请添加到白名单

---

## 致谢

- [HaujetZhao/CapsWriter-Offline](https://github.com/HaujetZhao/CapsWriter-Offline) — 原项目 / 交互设计 / 配置约定
- [k2-fsa/sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) — ASR 引擎 + PortAudioSharp2
- [TolikPylypchuk/SharpHook](https://github.com/TolikPylypchuk/SharpHook) — 跨平台全局键盘 hook
- [AvaloniaUI/Avalonia](https://github.com/AvaloniaUI/Avalonia) — 跨平台 XAML UI

## 许可

MIT，跟随原项目。
