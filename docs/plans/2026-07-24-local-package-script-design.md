# 本地打包脚本设计

## 目标

提供一个本地可运行的 Windows PowerShell 打包脚本，并让 Release 工作流复用它，确保预览包与正式发布包采用相同的发布参数、目录结构和校验文件。

## 方案

新增 `scripts/package.ps1`。脚本接受可选 `-Version`；未提供时从应用项目中读取版本（缺失则使用 `0.1.0`）并生成 `-dev.yyyyMMddHHmmss` 后缀。提供正式版本时校验 SemVer，并将其映射为合法的四段程序集版本。

脚本发布 Windows x64 自包含单文件应用，清理并重建对应的 `publish/win-x64` 与 `staging/VoxPen-<version>-win-x64` 目录，复制发布所需文件，产出 zip 和 SHA256 文件。清理只针对固定的仓库内目录，不处理 `models/` 或 junction。

Release 工作流仍负责版本输入校验、恢复依赖、运行测试、生成说明及创建 GitHub Release；打包阶段改为传入正式版本调用脚本。README 补充本地预览命令与产物说明。

## 验证

使用 Pester 检查脚本参数、发布属性、文件清单和工作流调用；运行一次脚本生成开发包，并检查 zip、SHA256 和解压后的必需文件；最后运行 Core 测试。
