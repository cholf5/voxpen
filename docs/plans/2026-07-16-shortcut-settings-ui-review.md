# 快捷键设置 UI 代码审查

## 审查范围

依据 `docs/plans/2026-07-16-shortcut-settings-ui-review-scope.md`，审查快捷键选项映射、配置保存、ViewModel 命令、Avalonia 设置页和 README 更新。

## 结果

- 审查文件：6 个预期修改文件及新增设计/计划文档
- P0：0
- P1：0
- P2：0
- 待复查：0

## 重点结论

- 默认快捷键仍为 Caps Lock。
- UI 只允许选择平台层已支持的预设键，避免写入无法监听的键名。
- 配置写入使用临时文件替换；写入失败时清理临时文件并恢复内存配置。
- 同时维护 `shortcut.keys` 和旧版 `shortcut.key`，兼容现有读取逻辑。
- 保存后不重建全局 Hook，并明确提示重启生效。
