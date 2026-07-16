# 跨平台支持范围与代价（macOS / Linux）

**日期**：2026-07-17
**状态**：仅存档，不计划立即实施（ROI 低）

## 结论

"C# / .NET 8 / Avalonia" 是**跨平台技术栈**，但当前代码库**不是**跨平台产物：`VoxPen.App` 的 TFM 直接钉死 `net8.0-windows10.0.19041.0`，`VoxPen.Platform.Windows` 里的一半接口实现是 Win32 独占。要打出可用的 Linux / macOS 包，需要新增两个平台项目 + 改 App 多目标 + 平台特定的打包分发流程。工作量 **2–6 周/平台**，Wayland 下核心功能存在死结。

## 现状：项目结构与平台绑定

| 层 | 项目 | TFM | 跨平台度 |
| --- | --- | --- | --- |
| 抽象 | `src/VoxPen.Core/` | `net8.0` | ✅ 真跨平台 |
| CLI | `src/VoxPen.Cli/` | `net8.0` | ⚠️ 依赖 Platform.Windows |
| UI | `src/VoxPen.App/` | `net8.0-windows10.0.19041.0` | ❌ **TFM 钉死 Windows** |
| 平台实现 | `src/VoxPen.Platform.Windows/` | `net8.0-windows10.0.19041.0` | ❌ Win32 独占 |
| 测试 | `tests/VoxPen.Core.Tests/` | `net8.0` | ✅ |

Avalonia 本身支持 Linux / macOS，`net8.0` 目标框架 UI 项目可以跨平台运行。**当前 App 用 `net8.0-windows` 是因为要引用 `Platform.Windows`（它必须是 `-windows` TFM）**。要跨平台，App 得多目标（`net8.0-windows;net8.0`），Windows 版继续引用 Platform.Windows，非 Windows 版引用对应平台项目。

## 缺什么：7 个 Core 抽象接口的平台矩阵

| 接口 | Windows（已实现） | macOS 需要 | Linux 需要 |
| --- | --- | --- | --- |
| `IGlobalHotkey` | SharpHook ✅ | SharpHook（macOS 原生支持）✅ | SharpHook（X11 OK；**Wayland 由 compositor 决定，普遍不可用**）⚠️ |
| `IAudioCapture` | PortAudioSharp2 ✅ | PortAudio（Core Audio 后端）✅ | PortAudio（ALSA / PulseAudio）✅ |
| `IAsrEngine` | sherpa-onnx Paraformer ✅ | sherpa-onnx（含 macOS x64/arm64 二进制）✅ | sherpa-onnx（含 Linux x64 二进制）✅ |
| `IAudioDecoder` | MediaFoundation ❌ | AVFoundation（`AVAssetReader`）或 FFmpeg | FFmpeg（`FFMpegCore` 包装）|
| **`ITextOutput`** | SendInput ❌ | **CGEventPost via `ApplicationServices`**（P/Invoke） | **X11: XTest 扩展**；**Wayland: 无普适方案** |
| `IForegroundApp` | GetForegroundWindow ❌ | `NSWorkspace.frontmostApplication` | X11: EWMH `_NET_ACTIVE_WINDOW`；Wayland: 需 compositor 协议 |
| `INotificationService` | UWP Toast ❌ | `UNUserNotificationCenter`（macOS 10.14+）| libnotify via D-Bus（`Tmds.DBus.SourceGenerator`）|

**❌** = Windows-only 实现，非 Windows 需要新写。
**✅** = 库跨平台，直接复用。

### Wayland 死结（重要）

`ITextOutput` 在 Wayland 下没有干净的方案：

- **XTest / X11 API**：Wayland 不兼容。
- **`uinput`**：内核设备节点，需要 `root` 或 `input` 组权限，安装体验差。
- **`ydotool`**：绕过 Wayland 限制的守护进程，用户必须自己装 + 起 systemd service。
- **wtype / dotool**：只在部分 compositor（Sway 等 wlroots 家族）可用。
- **Portal-based**：`RemoteDesktop` portal 可以做，但只在 GNOME / KDE 新版有实现，且每次要用户授权。

**结论**：Linux Wayland 支持要么以"要求用户预装 `ydotool`"为前提，要么只承诺 X11（很多主流发行版仍然默认 X11 或提供 X11 会话）。

## 打包 / 分发的额外成本

Windows 单文件 exe 之外，macOS / Linux 的分发方式不一样：

### macOS

- **产物形式**：`.app` bundle（目录结构：`VoxPen.app/Contents/{MacOS,Resources,Info.plist}`）+ 打成 `.dmg` 或 `.zip`。
- **代码签名**：Apple Developer Program **$99/年**。用 `codesign` 签 `.app`。
- **公证 (Notarization)**：`xcrun notarytool submit` 上传 Apple 服务器扫描，然后 `stapler staple` 把凭据钉到产物。**必做**，否则 Gatekeeper 直接拒绝。
- **CI**：`macos-latest` runner，从 GitHub Secrets 导入 P12 证书到 keychain。
- **Universal Binary**：x64 + arm64，`lipo` 合并两个原生 dylib。

### Linux

- **产物形式**：三选一 —
  - `AppImage`（单文件、无依赖、开箱即用）—— 推荐。
  - `.tar.gz` + 手动放置 + 用户自己写 `.desktop` 文件 —— 最简单。
  - `.deb` / `.rpm` —— 覆盖面广但打包工序繁琐，需要多个 runner。
- **签名**：GPG 签发行制品的 sha256 校验和即可，Linux 无强制签名。
- **CI**：`ubuntu-latest` runner。若做 AppImage 需要 `appimagetool`。

### CI 侧变化

现有 `release.yml` 单 job 单 runner，改造为：

```yaml
strategy:
  matrix:
    include:
      - os: windows-latest
        rid: win-x64
        artifact: VoxPen-${{ version }}-win-x64.zip
      - os: macos-latest
        rid: osx-arm64
        artifact: VoxPen-${{ version }}-macos-arm64.dmg
      - os: macos-13     # Intel Mac，如需
        rid: osx-x64
      - os: ubuntu-latest
        rid: linux-x64
        artifact: VoxPen-${{ version }}-linux-x64.AppImage
```

三平台的产物由 `gh release create` 一次上传到同一个 Release。

## 工作量估算

| 任务 | 人日 | 备注 |
| --- | --- | --- |
| App 多目标 + AppHost 组合根拆分 | 1–2 | 编译期通过条件编译或 partial 组合根切换平台实现 |
| **VoxPen.Platform.MacOS** 全部实现 | 5–8 | 4 个平台接口 + Accessibility 权限对话框处理 |
| macOS 打包（`.app` + `codesign` + `notarize`） | 2–3 | 需要 Apple Developer 账号 + P12 证书 |
| macOS CI job | 1–2 | 加入 matrix + 处理 keychain secrets |
| **macOS 小计** | **9–15 人日**（≈ 2–3 周） | 硬性 $99/年成本 |
| **VoxPen.Platform.Linux** (X11) | 5–8 | 4 个平台接口，X11-only |
| Linux Wayland 兼容（可选） | 3–5 | 走 `ydotool` 依赖 + 文档说明 |
| Linux 打包（AppImage） | 2 | `appimagetool` + `.desktop` |
| Linux CI job | 1 | 相对简单 |
| **Linux 小计** | **11–16 人日**（≈ 2–3 周） | 无硬性金钱成本 |
| **合计** | **20–31 人日**（≈ 4–6 周） | |

## 什么时候考虑做？

**信号**：
- 用户实际提出 Linux / macOS 需求（不是"跨平台看起来更酷"）。
- Windows 版核心功能已稳定 3+ 个月无重大回归。
- 有测试硬件（macOS 实机；Linux 至少一台 GNOME 桌面）。

**先做什么**：
1. 把 App TFM 改成多目标，先在 Linux 上跑一个"UI 起得来但快捷键不 work"的最小版本，验证 Avalonia 路径通。这一步花不了多少时间，能提前发现死结。
2. 然后再决定投入 macOS 还是继续 Linux 实现。

**替代方案（低成本部分覆盖）**：
- 只做 **CLI 跨平台**：`VoxPen.Cli transcribe` 走 sherpa-onnx，配合 PortAudio 抓音频，够 Linux/macOS 用户做批量转录。不涉及 SendInput，能规避绝大多数死结。CLI 项目本身已经是 `net8.0`，只要拆掉对 `Platform.Windows` 的引用即可，估计 3–5 人日。

## 相关

- **代码签名**（Windows Trusted Signing / SignPath）：独立任务，不依赖跨平台工作，可以先做。
- **自动更新**（Velopack）：Velopack 本身跨平台，等三平台都出来后再上更划算；也可以先在 Windows 上做，后续增量适配。
