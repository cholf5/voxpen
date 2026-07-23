# 双发行包审查

**审查范围：** `docs/plans/2026-07-24-dual-package-review-scope.md`

## 结果

已审查脚本、测试、Release 工作流、忽略规则、README 和计划文档。未发现高风险问题或重复实现。

## 已确认事项

- 框架依赖单文件发布不启用 `EnableCompressionInSingleFile`，避免 `NETSDK1176`。
- Release 同时上传两份 zip 和各自 SHA256。
- 小体积包文件名明确包含 `requires-dotnet-8-runtime`。

## 验证

- `Invoke-Pester tests/scripts/Package.Tests.ps1 -EnableExit`：6 项通过。
- `pwsh -File scripts/package.ps1`：生成两份 zip；自包含包约 91.3 MB，运行时依赖包约 29.5 MB。
- 两份 zip 的 SHA256 和必需文件已验证。
- `dotnet test tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj`：233 项通过。
- `git diff --check`：无输出。
