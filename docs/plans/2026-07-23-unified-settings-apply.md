# 统一设置保存与即时应用实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 将设置页改为统一保存，并让快捷键和模型选择在保存后立即生效。

**架构：** Core 新增平台无关的设置选择写入操作。AppHost 拆分为模型预加载与监听启动两阶段；App 在候选模型预加载成功后替换宿主，ViewModel 通过回调请求切换并重新绑定新宿主。

**技术栈：** .NET 8、C#、Avalonia、CommunityToolkit.Mvvm、xUnit。

---

### 任务 1：测试并实现设置选择写入

**文件：**
- 创建：`src/VoxPen.Core/Config/SettingsSelection.cs`
- 创建：`tests/VoxPen.Core.Tests/Config/SettingsSelectionTests.cs`

**步骤 1：编写失败的测试**

测试 `SettingsSelection.Apply` 会同时更新 `shortcut.key`、`shortcut.keys`、ASR 引擎和对应的默认模型目录。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~SettingsSelectionTests`

预期：FAIL，提示 `SettingsSelection` 尚不存在。

**步骤 3：编写最少实现**

新增平台无关的 `SettingsSelection.Apply(AppConfig, string, AsrEngineKind)`，复用模型目录目录约定。

**步骤 4：运行测试以验证它通过**

运行同一测试命令。

### 任务 2：实现候选宿主切换与统一保存 UI

**文件：**
- 修改：`src/VoxPen.App/Services/AppHost.cs`
- 修改：`src/VoxPen.App/App.axaml.cs`
- 修改：`src/VoxPen.App/ViewModels/MainWindowViewModel.cs`
- 修改：`src/VoxPen.App/Views/MainWindow.axaml`

**步骤 1：拆分宿主启动**

将模型加载和快捷键启动拆为可独立调用的阶段，使候选宿主能在旧监听器仍工作时完成模型预加载。

**步骤 2：替换宿主**

App 保存配置后创建候选宿主；候选预加载成功才释放旧宿主并启动候选宿主，随后重绑 ViewModel、日志和浮窗事件。

**步骤 3：收敛界面保存操作**

移除单独的快捷键保存命令；下载完成后不自动切换模型；新增底部唯一“保存并应用”按钮及统一状态文本。

**步骤 4：构建验证**

运行：`dotnet build src/VoxPen.App`

预期：Build succeeded。

### 任务 3：更新用户文档并验证

**文件：**
- 修改：`README.md`

**步骤 1：更新设置与热重载说明**

说明设置页保存快捷键和模型后立即生效；保留手工编辑 config.json 的 3 秒热重载语义。

**步骤 2：完整验证**

运行：`dotnet test tests/VoxPen.Core.Tests` 和 `dotnet build src/VoxPen.App`。

---

## 审查范围

详见：`docs/plans/2026-07-23-unified-settings-apply-review-scope.md`
