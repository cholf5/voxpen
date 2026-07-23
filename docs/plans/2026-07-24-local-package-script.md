# 本地打包脚本实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 新增可本地预览发布包的脚本，并由 Release 工作流复用它。

**架构：** `scripts/package.ps1` 集中维护发布、整理、压缩和哈希逻辑。GitHub Actions 仅传入正式版本并负责 CI 专有的测试、说明与发布动作。

**技术栈：** PowerShell 7、Pester、.NET 8、GitHub Actions。

---

### 任务 1：为打包接口编写失败测试

**文件：**
- 创建：`tests/scripts/Package.Tests.ps1`
- 测试：`tests/scripts/Package.Tests.ps1`

**步骤 1：编写失败的测试**

断言脚本存在，包含可选 `Version` 参数、`dotnet publish`、单文件发布属性、zip 与 SHA256 输出，以及发布内容清单。

**步骤 2：运行测试以验证它失败**

运行：`Invoke-Pester tests/scripts/Package.Tests.ps1`

预期：FAIL，原因是 `scripts/package.ps1` 尚不存在。

### 任务 2：实现并复用打包脚本

**文件：**
- 创建：`scripts/package.ps1`
- 修改：`.github/workflows/release.yml`
- 修改：`README.md`

**步骤 1：编写最少实现**

实现自动开发版本、正式版本校验、发布、固定目录安全清理、整理、压缩与 SHA256。以 `-Version $env:VERSION` 从发布工作流调用脚本。

**步骤 2：运行测试以验证它通过**

运行：`Invoke-Pester tests/scripts/Package.Tests.ps1`

预期：PASS。

### 任务 3：端到端验证

**文件：**
- 验证：`scripts/package.ps1`
- 验证：`tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj`

**步骤 1：生成本地开发包**

运行：`pwsh -File scripts/package.ps1`

预期：生成带 `-dev.` 后缀的 zip、SHA256 和包含 exe、README、首次运行说明的阶段目录。

**步骤 2：运行回归测试**

运行：`dotnet test tests/VoxPen.Core.Tests/VoxPen.Core.Tests.csproj`

预期：退出码为 0。

---

## 审查范围

详见：`docs/plans/2026-07-24-local-package-script-review-scope.md`
