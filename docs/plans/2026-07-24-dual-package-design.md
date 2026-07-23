# 双发行包设计

## 目标

每个版本同时提供开箱即用的自包含包和体积更小的框架依赖包，并让文件名直接说明后者需要 .NET 8 Runtime。

## 方案

`scripts/package.ps1` 以同一版本连续执行两次 `dotnet publish`。默认包保留现有名称 `VoxPen-<version>-win-x64.zip`，使用 `--self-contained true`。小体积包命名为 `VoxPen-<version>-win-x64-requires-dotnet-8-runtime.zip`，使用 `--self-contained false`，但保持单文件、Windows x64 和现有发布内容。

脚本为两种包使用独立的 publish 与 staging 目录，分别生成 SHA256，避免文件相互覆盖。Release 工作流仍调用一次脚本，并把两组 zip 与哈希一并作为 Release 资产上传。README 说明普通用户应下载默认包；仅已安装 .NET 8 x64 Runtime 的用户选择名称带 `requires-dotnet-8-runtime` 的小体积包。

## 错误处理与验证

任意一次发布失败即停止，保留已有的退出码检查。Pester 覆盖两种自包含设置、明确的文件名和工作流四个资产；端到端运行脚本后检查两份 zip 与哈希。最后运行 Core 测试。
