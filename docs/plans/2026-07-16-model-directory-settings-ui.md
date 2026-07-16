# 模型目录设置与状态 UI 实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 在设置页编辑模型目录，并按与 Paraformer 引擎一致的文件规则自动显示模型是否有效。

**架构：** Core 提供模型目录校验器，统一检查目录、模型 ONNX 文件和 `tokens.txt`；AppHost 负责保存目录并暴露当前引擎加载状态。ViewModel 在文本框修改或失焦时异步校验，UI 以 ✅/❌ 显示结果，保存后提示重启。

**技术栈：** .NET 8、C#、Avalonia、CommunityToolkit.Mvvm、xUnit。

---

### 任务 1：添加模型目录校验测试和实现

**文件：**
- 创建：`src/VoxPen.Core/Config/ModelDirectoryValidator.cs`
- 创建：`tests/VoxPen.Core.Tests/Config/ModelDirectoryValidatorTests.cs`
- 修改：`src/VoxPen.Platform.Windows/Recognition/ParaformerEngine.cs`

先测试目录不存在、缺少模型文件、缺少 tokens、完整目录四种情况；再实现校验器，并让 Paraformer 引擎复用它。

### 任务 2：接入配置保存和 ViewModel 自动检测

**文件：**
- 修改：`src/VoxPen.App/Services/AppHost.cs`
- 修改：`src/VoxPen.App/ViewModels/MainWindowViewModel.cs`

增加模型目录保存入口、目录文本属性、状态图标属性、状态说明和取消旧检测任务的异步检测逻辑。保存只修改配置文件，不在运行中替换已加载引擎。

### 任务 3：更新设置页和文档

**文件：**
- 修改：`src/VoxPen.App/Views/MainWindow.axaml`
- 修改：`README.md`

将只读模型目录替换为文本框、保存按钮、状态图标和检测说明。

### 任务 4：验证和审查

运行 `dotnet test`、`dotnet build src/VoxPen.App/VoxPen.App.csproj`、`git diff --check`，并进行代码审查。

---

## 审查范围

详见：`docs/plans/2026-07-16-model-directory-settings-ui-review-scope.md`
