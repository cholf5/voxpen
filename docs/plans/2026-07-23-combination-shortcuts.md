# 组合快捷键与录制 实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 支持任意可识别键的组合快捷键录制，并拒绝单独录制普通字母键。

**架构：** `ShortcutSettings` 在 Core 中规范化和校验组合，保留 `shortcut.key` 为组合首键以兼容旧配置。Windows hook 为配置组合维护按下状态，仅全部按下时触发，并将原始可识别键流提供给设置页录制器。设置页录制按住的键组并保存。

**技术栈：** .NET 8、Avalonia、SharpHook、xUnit、FluentAssertions。

---

### 任务 1：组合配置的规范化与持久化

**文件：**
- 修改：`src/VoxPen.Core/Config/ShortcutSettings.cs`
- 修改：`src/VoxPen.Core/Config/SettingsSelection.cs`
- 测试：`tests/VoxPen.Core.Tests/Config/ShortcutSettingsTests.cs`
- 测试：`tests/VoxPen.Core.Tests/Config/SettingsSelectionTests.cs`

**步骤：** 先为组合顺序、重复项、单字母拒绝和 JSON 兼容写失败测试；实现最小的规范化、显示和保存 API；运行 `dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~Config`。

### 任务 2：Windows 组合按键状态机与键名映射

**文件：**
- 修改：`src/VoxPen.Platform.Windows/Hooks/WindowsGlobalHotkey.cs`
- 测试：`tests/VoxPen.Core.Tests/Config/ShortcutSettingsTests.cs`

**步骤：** 先为允许的键名和组合语义写失败测试；让平台层将任意 SharpHook 键规范为配置键名、维护按下集合，并仅在完整组合按下/首键松开时发射事件；为录制暴露非抑制的观测事件。

### 任务 3：设置页快捷键录制与立即应用

**文件：**
- 修改：`src/VoxPen.App/ViewModels/MainWindowViewModel.cs`
- 修改：`src/VoxPen.App/Views/MainWindow.axaml`
- 修改：`src/VoxPen.App/Services/AppHost.cs`
- 修改：`src/VoxPen.App/App.axaml.cs`
- 修改：`README.md`

**步骤：** 删除单选下拉，新增开始/取消录制、组合文字和校验提示；将观测到的按下键组写入 ViewModel，松开后接受合法组合；保存完整列表并更新用户文档的语义与示例；构建 App。

### 任务 4：验证与审查

**文件：**
- 审查：本计划列出的全部文件

**步骤：** 运行完整 Core 测试与 App 构建；检查 `git diff --check`；按审查范围进行代码审查，不提交。

---

## 审查范围

详见：`docs/plans/2026-07-23-combination-shortcuts-review-scope.md`
