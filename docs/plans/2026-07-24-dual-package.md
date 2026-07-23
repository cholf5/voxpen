# 双发行包实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 为每个版本生成自包含与需 .NET 8 Runtime 的两种 Windows x64 单文件包。

**架构：** PowerShell 脚本集中处理两次发布、整理、压缩和哈希。Release 工作流只调用脚本并上传两组资产。

**技术栈：** PowerShell 7、Pester、.NET 8、GitHub Actions。

---

### 任务 1：编写双包失败测试

**文件：**
- 修改：`tests/scripts/Package.Tests.ps1`

**步骤 1：编写失败测试**

断言脚本显式包含 `--self-contained true` 与 `--self-contained false`、`requires-dotnet-8-runtime` 文件名，以及工作流上传两份 zip 和 SHA256。

**步骤 2：验证失败**

运行：`Invoke-Pester tests/scripts/Package.Tests.ps1 -EnableExit`

预期：FAIL，因为脚本与工作流尚未定义第二种包。

### 任务 2：实现两种发行包

**文件：**
- 修改：`scripts/package.ps1`
- 修改：`.github/workflows/release.yml`
- 修改：`.gitignore`
- 修改：`README.md`

**步骤 1：最少实现**

提取单个包的发布流程，分别调用自包含与框架依赖配置；工作流上传四个资产。README 指明默认包与运行时依赖包的适用对象。

**步骤 2：验证通过**

运行：`Invoke-Pester tests/scripts/Package.Tests.ps1 -EnableExit`

预期：PASS。

### 任务 3：端到端验证

**文件：**
- 验证：`scripts/package.ps1`
- 验证：`tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj`

**步骤 1：运行本地打包**

运行：`pwsh -File scripts/package.ps1`

预期：生成两份 zip 与对应 SHA256；第二个文件名包含 `requires-dotnet-8-runtime`。

**步骤 2：运行回归测试**

运行：`dotnet test tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj`

预期：退出码为 0。

---

## 审查范围

详见：`docs/plans/2026-07-24-dual-package-review-scope.md`
