# 多 ASR 模型与下载功能审查

**审查范围**：`docs/plans/2026-07-23-multi-asr-models-review-scope.md`

## 审查结果

- P1（已修复）：`.gitignore` 的 `models/` 会忽略 `src/**/Models/` 源码。已改为仅忽略根目录 `/models/`。
- P1（已修复）：CLI 文档化的 `fun_asr_nano`、`qwen_asr` 不能由枚举解析。已改为显式映射四个稳定标识。
- P1（已修复）：设置页手动选择模型后保存目录不会保存引擎类型。现在保存操作先保存当前引擎，再保存目录。

## 验证

- `dotnet test tests/VoxPen.Core.Tests`：222 通过，0 失败。
- `dotnet build src/VoxPen.App`：通过（仅既存 Avalonia 过时属性警告）。
- `dotnet build src/VoxPen.Cli`：通过。
- 四个 `--engine` 文档化标识均通过 `test-postprocess` 参数解析冒烟。

## 未执行的外部验证

未下载数百 MB 至数 GB 的真实模型包，因此尚未对 HTTP Range 续传、tar.bz2 解压和四种模型的真实音频识别执行端到端验证。首次安装每种模型后应各执行一次 CLI `--file <16k-wav>` 冒烟。
