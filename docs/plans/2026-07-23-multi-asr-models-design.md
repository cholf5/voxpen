# 多 ASR 模型与应用内下载设计

## 目标

VoxPen 与上游 CapsWriter-Offline 当前支持范围保持一致：Paraformer、SenseVoice-Small、Fun-ASR-Nano、Qwen3-ASR。用户可在设置页选择模型，并通过应用内下载器获取、校验和安装模型，无需手动处理压缩包。

## 架构

`AsrConfig.Engine` 支持 `paraformer`、`sensevoice`、`fun_asr_nano`、`qwen_asr`；未配置该字段的历史 `config.json` 保持 Paraformer。Core 定义引擎和模型下载相关抽象、模型描述与验证规则，不引用 Windows 或推理库。Windows 平台层实现模型工厂和四种 `IAsrEngine`：Paraformer 与 SenseVoice 使用 ONNX；Fun-ASR-Nano 与 Qwen3-ASR 使用 ONNX 编码器与 GGUF 解码器。`AppHost` 以工厂创建当前配置的引擎，保持其作为唯一组合根。

Paraformer 继续使用外挂 CT-Transformer 标点模型。SenseVoice 与 Fun-ASR-Nano 声明自带标点和时间戳；Qwen3-ASR 声明自带标点但无时间戳，文件转录处明确处理这一能力差异。切换模型、模型目录与推理后端均在重启后生效。

## 模型下载

模型清单维护原版 GitHub Release 的固定资产 URL、预期目录、必需文件、大小和展示文案。下载任务写入模型目标同卷的 `.partial` 文件，并用 HTTP Range 续传；进度包含已下载字节、总字节、百分比、速度、剩余时间和状态。取消或网络失败保留临时文件，重试继续传输。

下载完成后压缩包先解压到同卷临时目录，校验完整模型目录后再落位到 `models/`。失败时清理仅由本任务创建的临时目录，不触碰已有模型。不会覆盖完整的已安装模型；用户必须显式删除后才可重新下载。下载器提供取消、重试和已安装状态。

## 界面与 CLI

设置页改为模型选择器与模型卡片，展示能力、体积、目录校验状态和下载操作。单个活动下载显示进度条、速度和剩余时间。CLI 增加 `--engine`，沿用相同的配置、模型工厂和验证规则。README 更新模型比较、磁盘空间和下载/恢复说明。

## 验证

以 TDD 完成配置迁移、模型目录校验、引擎工厂和下载状态机。HTTP、归档和文件系统通过接口隔离，测试使用临时目录与可控响应，不下载真实模型。每种引擎有能力与模型文件要求测试；下载覆盖新下载、续传、取消、损坏压缩包和安装失败；完成后运行全部 Core 测试、App 构建与既有 CLI 冒烟命令。
