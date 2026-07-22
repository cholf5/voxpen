# 固定模型目录与下载文件释放 审查

**关联计划**: docs/plans/2026-07-23-fixed-model-directories.md

## 结果

审查了目录约定、下载包安装、Avalonia 设置页和 CLI 参数路径。未发现新的高风险问题或重复实现。

## 已确认事项

- 旧配置的 ASR 与标点模型目录在启动时被固定目录约定覆盖。
- 安装器先释放 archive 和包读取流，再删除 `.partial` 文件。
- 设置页与 CLI 都不再接受自定义模型目录。

## 验证

- `dotnet build src/VoxPen.App`
- `dotnet build src/VoxPen.Cli`
- `dotnet test tests/VoxPen.Core.Tests`：224/224 通过。
- `dotnet run --project src/VoxPen.Cli -- --model C:\temp\custom`：按预期返回 2。
