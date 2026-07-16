# GitHub Release Workflow 设计（VoxPen v0.x，仅 Windows）

日期：2026-07-16
范围：为 VoxPen 首次搭建发版流水线；仅 Windows x64；手动触发；产物不含模型。

## 结论摘要

| 维度 | 决策 |
| --- | --- |
| 触发方式 | GitHub Actions `workflow_dispatch`，表单填版本号 |
| 平台 | 仅 `windows-latest` runner，输出 `win-x64` |
| 产物 | `VoxPen-<ver>-win-x64.zip`（App 本体 + hot-rule.txt + hot.txt + README + FIRST-RUN.txt） |
| 校验 | 同名 `.sha256` 一并上传 |
| 模型 | **不打入包**；首启缺 `models/paraformer/` 时由 App 提示用户下载 |
| 验证 | Publish 前跑 `dotnet test tests/VoxPen.Core.Tests`；失败即终止 |
| Release notes | 自定义 PowerShell 步骤按 conventional commits 前缀（feat/fix/refactor+perf/docs+chore+build+ci+style+test）从 `git log` 分组，写 `notes.md`，`gh release create --notes-file` 采纳；`.github/release.yml` 保留供未来 PR 流程 |
| tag / Release | 由 `gh release create v<ver> --target $GITHUB_SHA` 自动创建，失败不留残骸 |
| 代码签名 | 不做（个人项目 ROI 不够）；SmartScreen 首次提示由 README 说明 |
| pdb | 不打包也不上传，需要排错时本地重现构建 |

## §1 骨架

新增文件：

```
.github/
├── workflows/
│   └── release.yml            # 唯一的手动发版工作流
└── release.yml                # Release notes 分组配置
```

`workflows/release.yml` 关键属性：

- `on: workflow_dispatch:` 表单入参
  - `version`（必填，正则 `^\d+\.\d+\.\d+(-[a-z0-9.]+)?$`，允许 `0.1.0` / `0.1.0-rc.1`）
  - `prerelease`（可选，布尔，默认 `false`）
- `permissions: contents: write`
- `runs-on: windows-latest`（MediaFoundation / net8.0-windows 依赖）
- `concurrency: group: release`，`cancel-in-progress: false`

产物命名：

- tag：`v${{ inputs.version }}`
- zip：`VoxPen-${{ inputs.version }}-win-x64.zip`
- 校验：`VoxPen-${{ inputs.version }}-win-x64.zip.sha256`

`.github/release.yml` 分组（GitHub 官方 auto-generated notes 格式）：

- 🚀 新功能：`feat`
- 🐛 修复：`fix`
- 🔧 优化：`refactor`、`perf`
- 📝 杂项：`docs`、`chore`、`build`、`ci`
- 其余走默认 fallback

## §2 Workflow 步骤

单 job（`release`），串行步骤：

1. `actions/checkout@v4`，`fetch-depth: 0`（让 `--generate-notes` 能看到历史 tag）
2. `actions/setup-dotnet@v4`，`dotnet-version: 8.0.x`
3. `actions/cache@v4`，key 用 `hashFiles('**/*.csproj')`，缓存 NuGet
4. `dotnet restore src/VoxPen.App/VoxPen.App.csproj` + `dotnet restore tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj`
   - **不用 `dotnet restore VoxPen.slnx`**：`.slnx` 需要更新的 SDK（.NET 9 或 8.0.400+ 预览），走项目路径更稳。
5. `dotnet test tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj --no-restore -c Release`
6. `dotnet publish src/VoxPen.App/VoxPen.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:Version=<ver> -p:AssemblyVersion=<verNumeric>.0 -p:FileVersion=<verNumeric>.0 -p:InformationalVersion=<ver> -o publish/win-x64`
   - `<verNumeric>` = `inputs.version` 去掉 `-xxx` 后缀（`AssemblyVersion` 只接受 4 段数字）。完整字符串写进 `InformationalVersion`。
7. PowerShell 组织 staging 目录，`Compress-Archive` 出 zip（zip 内根目录同名）
8. `(Get-FileHash <zip> -Algorithm SHA256).Hash | Out-File <zip>.sha256`
9. **Generate release notes**：PowerShell 从 `git log <prevTag>..HEAD --no-merges` 按 conventional commits 前缀（`feat` / `fix` / `refactor|perf` / `docs|chore|build|ci|style|test` / 其他）分组，写入 `notes.md`，并在末尾附 `Full Changelog: .../compare/<prevTag>...v<ver>`。首次发版无 tag 时退化为 `HEAD` 全量并使用 `commits/v<ver>` 链接。
10. `gh release create v<ver> <zip> <zip>.sha256 --title "VoxPen v<ver>" --notes-file notes.md --target $env:GITHUB_SHA`，若 `prerelease=true` 追加 `--prerelease`；`env: GH_TOKEN: ${{ secrets.GITHUB_TOKEN }}`

失败策略：tag / Release 只在最后一步创建，前面任何步骤失败都不会留下悬空 tag。

**Release notes 分组的现实**：GitHub 原生的 `--generate-notes` 走 **PR labels**，本仓库单人直推 main 用不上，所以改用自定义步骤基于 commit 前缀分组。`.github/release.yml` 仍保留，未来引入 PR 流程或手动切回 `--generate-notes` 时按 labels 自动分类。

## §3 zip 内容 & 首启体验

zip 布局：

```
VoxPen-<ver>-win-x64/
├── VoxPen.App.exe
├── hot-rule.txt              # 从 publish 输出复制（csproj 已 CopyToOutputDirectory）
├── hot.txt                   # 从仓库根复制（不存在则跳过）
├── config.sample.json        # 从仓库根复制（若存在）
├── README.md                 # 从仓库根复制
└── FIRST-RUN.txt             # 三行中文说明 + 模型下载链接
```

不打 pdb；不打模型。

配套文档更新（与 workflow 同批提交）：

- README 增加 "下载模型" 一节，给出 sherpa-onnx Paraformer 官方链接和 `models/paraformer/` 目录结构。
- 新建 `FIRST-RUN.txt`（仓库根），三行中文：解压位置提示、模型下载链接、遇到 SmartScreen 提示的处理方式。
- README 增加 "SmartScreen 首次运行未知发布者" 的说明段落。

App 内缺模型提示带 "打开 README" 按钮属于后续增强，不阻塞首次发版。

## 未纳入本次范围

- 代码签名（Authenticode）
- App 内自动更新检测
- macOS / Linux 产物
- 模型独立分发
- CLI smoke（`test-postprocess` 等）作为 CI 门禁——如后续单测覆盖不足再补
