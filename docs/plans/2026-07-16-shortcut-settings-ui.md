# 快捷键设置 UI 实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 在 Avalonia 设置页用下拉框修改录音快捷键，并安全保存到 `config.json`。

**架构：** ViewModel 提供固定的友好名称与配置键名映射、当前选择和保存命令；保存时更新 `shortcut.keys`/`shortcut.key` 并通过 AppHost 写入配置文件。全局 Hook 不重建，保存后提示重启生效。

**技术栈：** .NET 8、C#、Avalonia、CommunityToolkit.Mvvm、System.Text.Json、xUnit。

---

### 任务 1：抽取并测试快捷键选项与配置保存

**文件：**
- 创建：`src/VoxPen.Core/Config/ShortcutSettings.cs`
- 创建：`tests/VoxPen.Core.Tests/Config/ShortcutSettingsTests.cs`

**步骤 1：编写失败的测试**

覆盖默认 Caps Lock、键名到显示名映射、保存后同时写入 `keys` 和旧版 `key` 字段、非法键名拒绝，以及不破坏原配置文件的失败场景。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj --filter FullyQualifiedName~ShortcutSettingsTests`

预期：因 `ShortcutSettings` 尚不存在而失败。

**步骤 3：编写最少实现**

提供固定支持列表、友好名称映射和把选择写回指定 JSON 文件的服务；使用临时文件完成写入后替换目标文件，序列化或写入失败时保留原文件。

**步骤 4：运行测试以验证它通过**

运行同一条 `dotnet test` 命令，预期全部通过。

### 任务 2：接入 AppHost 与 ViewModel

**文件：**
- 修改：`src/VoxPen.App/Services/AppHost.cs`
- 修改：`src/VoxPen.App/ViewModels/MainWindowViewModel.cs`

**步骤 1：编写失败的测试**

为配置保存入口增加行为测试或使用任务 1 的真实保存服务测试，确认 ViewModel 保存后状态提示为“已保存，重启后生效”，失败时显示错误且不改变当前选择。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj`

预期：新增行为尚未实现时失败。

**步骤 3：编写最少实现**

让 AppHost 暴露配置文件保存入口；ViewModel 初始化选项和当前值，增加 `SaveShortcutCommand`、状态提示和错误提示。设计时无参构造继续可用。

**步骤 4：运行测试以验证它通过**

运行核心测试并确认全部通过。

### 任务 3：更新 Avalonia 设置页与文档

**文件：**
- 修改：`src/VoxPen.App/Views/MainWindow.axaml`
- 修改：`README.md`

**步骤 1：实现 UI**

将原只读快捷键文本替换为 `ComboBox` 和“保存快捷键”按钮，显示保存结果及重启提示；保留模型目录、上屏模式等只读信息。

**步骤 2：验证构建**

运行：`dotnet build src/VoxPen.App/VoxPen.App.csproj`

预期：构建成功且无 XAML 绑定错误。

**步骤 3：更新文档**

说明可在“设置”页选择快捷键、支持的选项、保存后需重启，以及 `config.json` 仍可作为高级配置入口。

### 任务 4：完整验证

运行：`dotnet test`，随后运行 `dotnet build src/VoxPen.App/VoxPen.App.csproj`。

检查默认配置仍为 Caps Lock，保存单项快捷键后旧字段和新字段一致，并确认工作区没有修改无关文件。

---

## 审查范围

详见：`docs/plans/2026-07-16-shortcut-settings-ui-review-scope.md`
