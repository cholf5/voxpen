# 固定模型目录与下载文件释放 实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 让所有 ASR 和标点模型只使用程序约定目录，并修复模型下载完成后无法读取 `.partial` 包的问题。

**架构：** 保留 `config.json` 中的旧 `modelDir` 字段以兼容旧文件，但启动时由 Core 的目录约定覆盖其值。Avalonia 设置页只显示引擎选择、下载状态与固定目录的检测结果。安装器在删除下载包前显式释放压缩包读取流。

**技术栈：** .NET 8、Avalonia、xUnit、FluentAssertions。

---

### 任务 1：固化模型目录约定

**文件：**

- 创建：`src/VoxPen.Core/Config/ModelDirectoryConvention.cs`
- 修改：`src/VoxPen.App/Services/AppHost.cs`
- 测试：`tests/VoxPen.Core.Tests/Config/ModelDirectoryConventionTests.cs`

**步骤 1：编写失败的测试**

验证旧配置中的 ASR 与标点 `modelDir` 会被覆盖为当前引擎和固定标点目录。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~ModelDirectoryConventionTests`

预期：FAIL，因为目录约定类尚不存在。

**步骤 3：编写最少实现**

新增目录约定类；应用启动前调用它，再将相对目录解析到应用目录。

**步骤 4：运行测试以验证它通过**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~ModelDirectoryConventionTests`

预期：PASS。

### 任务 2：释放下载包读取流

**文件：**

- 修改：`src/VoxPen.Platform.Windows/Models/CompressedModelPackageInstaller.cs`
- 测试：`tests/VoxPen.Core.Tests/Models/CompressedModelPackageInstallerTests.cs`

**步骤 1：编写失败的测试**

构造最小 ZIP 包，验证安装完成后 `.partial` 包可被删除。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~CompressedModelPackageInstallerTests`

预期：FAIL，安装器在压缩包仍被占用时删除 `.partial` 包。

**步骤 3：编写最少实现**

由安装器显式持有压缩包读取流，并在删除 `.partial` 包前释放 archive 和读取流。

**步骤 4：运行测试以验证它通过**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~CompressedModelPackageInstallerTests`

预期：PASS。

### 任务 3：移除模型路径设置并更新文档

**文件：**

- 修改：`src/VoxPen.App/ViewModels/MainWindowViewModel.cs`
- 修改：`src/VoxPen.App/Views/MainWindow.axaml`
- 修改：`README.md`

**步骤 1：编写失败的测试**

任务 1 的目录约定测试覆盖路径不再可配置的核心行为。

**步骤 2：编写最少实现**

移除路径编辑、保存命令及标点模型设置区；保留 ASR 下载与固定目录状态。README 改为说明模型由程序固定到 `models/` 下的约定目录，手动复用须复制到该目录。

**步骤 3：构建并运行测试**

运行：`dotnet build src/VoxPen.App`，以及 `dotnet test tests/VoxPen.Core.Tests`。

预期：均以退出码 0 结束。

---

## 审查范围

详见：`docs/plans/2026-07-23-fixed-model-directories-review-scope.md`
