# 多 ASR 模型与应用内下载实施计划

> **给 Claude：** 必需的子技能：使用 com-executing-plans 逐任务实施此计划。

**目标：** 在保持单进程、纯 C# 架构和旧配置兼容的前提下，支持 Paraformer、SenseVoice-Small、Fun-ASR-Nano、Qwen3-ASR，并提供带进度、取消和续传的应用内模型下载。

**架构：** 在 `VoxPen.Core` 定义模型标识、目录校验、下载状态和下载接口，在 `VoxPen.Platform.Windows` 实现引擎工厂、ONNX/GGUF 推理与 ZIP 下载器。`AppHost` 只依赖 `IAsrEngine` 和工厂，设置页只消费模型目录与下载任务的状态。模型的能力位继续决定是否加载外挂标点；所有模型选择与下载元数据来自同一份目录，避免 UI、CLI、启动校验出现分叉。

**技术栈：** .NET 8、Avalonia 12、CommunityToolkit.Mvvm、sherpa-onnx、ONNX Runtime、GGUF/llama.cpp 的 C# 绑定（经第 5 任务的兼容性验证后锁定）、`HttpClient`、`System.IO.Compression`、xUnit。

---

### 任务 1：模型目录、配置迁移与通用校验

**文件：**
- 创建：`src/VoxPen.Core/Config/AsrEngineKind.cs`
- 创建：`src/VoxPen.Core/Config/AsrModelDefinition.cs`
- 创建：`src/VoxPen.Core/Config/AsrModelCatalog.cs`
- 创建：`src/VoxPen.Core/Config/AsrModelValidator.cs`
- 修改：`src/VoxPen.Core/Config/AppConfig.cs:50-71`
- 修改：`src/VoxPen.Core/Config/ModelDirectoryValidator.cs:1-37`
- 创建：`tests/VoxPen.Core.Tests/Config/AsrModelCatalogTests.cs`
- 创建：`tests/VoxPen.Core.Tests/Config/AsrModelValidatorTests.cs`

**步骤 1：编写失败的模型目录测试**

覆盖四个键、默认目录、各模型必需文件，以及历史 `AsrConfig` 未写 `Engine` 时默认 Paraformer：

```csharp
[Theory]
[InlineData(AsrEngineKind.Paraformer, "model.onnx", "tokens.txt")]
[InlineData(AsrEngineKind.SenseVoice, "SenseVoice-Encoder.fp16.onnx", "tokenizer.bpe.model")]
public void Validate_requires_the_files_declared_by_the_catalog(
    AsrEngineKind kind, params string[] files) { /* 临时目录断言 */ }

[Fact]
public void AsrConfig_defaults_to_paraformer() =>
    new AsrConfig().Engine.Should().Be(AsrEngineKind.Paraformer);
```

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter "FullyQualifiedName~AsrModelCatalogTests|FullyQualifiedName~AsrModelValidatorTests"`

预期：FAIL，提示 `AsrEngineKind`、`AsrModelCatalog` 和 `AsrModelValidator` 不存在。

**步骤 3：实现最少模型元数据与校验**

定义字符串序列化安全的 `AsrEngineKind`，并将原版 Release 资产、模型根目录、必需文件、能力、显示名称、介绍、大小、下载 URL 记录为不可变 `AsrModelDefinition`。目录校验必须只使用该定义；保留 `ModelDirectoryValidator.Validate` 作为 Paraformer 的兼容包装，避免现有调用立即破坏。

**步骤 4：运行测试以验证它通过**

运行同一命令。

预期：PASS；原 `ModelDirectoryValidatorTests` 仍通过。

### 任务 2：抽象引擎工厂并替换 Paraformer 的硬编码装配

**文件：**
- 创建：`src/VoxPen.Platform.Windows/Recognition/WindowsAsrEngineFactory.cs`
- 修改：`src/VoxPen.App/Services/AppHost.cs:39-50,68-122,173-285,288-344,536-561`
- 修改：`src/VoxPen.Cli/Program.cs:20-130,355-368`
- 创建：`tests/VoxPen.Core.Tests/Recognition/WindowsAsrEngineFactoryTests.cs`

**步骤 1：编写失败的工厂测试**

```csharp
[Theory]
[InlineData(AsrEngineKind.Paraformer, "paraformer-onnx")]
[InlineData(AsrEngineKind.SenseVoice, "sensevoice-onnx")]
public void Create_returns_the_engine_selected_by_config(AsrEngineKind kind, string name)
{
    using var engine = WindowsAsrEngineFactory.Create(new AsrConfig { Engine = kind });
    engine.Name.Should().Be(name);
}

[Fact]
public void Create_rejects_an_unknown_engine() { /* Assert.Throws */ }
```

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~WindowsAsrEngineFactoryTests`

预期：FAIL，提示工厂不存在。

**步骤 3：最小化重构组合根与 CLI**

将 `_asr`、构造参数和公开状态改为 `IAsrEngine`；`Create` 使用工厂，`ValidateModelDirectory` 按当前 `Engine` 查目录定义，`IsModelLoadedFor` 同时比较引擎和绝对路径。为 CLI 解析 `--engine paraformer|sensevoice|fun_asr_nano|qwen_asr`，再调用同一工厂；`--model` 只覆盖当前引擎的目录。错误消息列出四个合法值。

**步骤 4：运行测试与编译**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~WindowsAsrEngineFactoryTests; dotnet build src/VoxPen.App; dotnet build src/VoxPen.Cli`

预期：测试和两个构建均通过；Paraformer 保持原行为。

### 任务 3：可测试的下载状态、接口和模型安装协调器

**文件：**
- 创建：`src/VoxPen.Core/Models/ModelDownloadState.cs`
- 创建：`src/VoxPen.Core/Models/ModelDownloadProgress.cs`
- 创建：`src/VoxPen.Core/Models/IModelPackageDownloader.cs`
- 创建：`src/VoxPen.Core/Models/IModelPackageInstaller.cs`
- 创建：`src/VoxPen.Core/Models/ModelInstallCoordinator.cs`
- 创建：`tests/VoxPen.Core.Tests/Models/ModelInstallCoordinatorTests.cs`

**步骤 1：编写失败的状态机测试**

```csharp
[Fact]
public async Task Install_reports_downloading_then_verifying_then_completed()
{
    var states = new List<ModelDownloadState>();
    await coordinator.InstallAsync(definition, progress => states.Add(progress.State));
    states.Should().ContainInOrder(ModelDownloadState.Downloading,
        ModelDownloadState.Verifying, ModelDownloadState.Installing,
        ModelDownloadState.Completed);
}

[Fact]
public async Task Cancel_keeps_partial_package_for_a_later_resume() { /* fake downloader */ }
```

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~ModelInstallCoordinatorTests`

预期：FAIL，提示下载抽象与协调器不存在。

**步骤 3：实现与平台无关的协调逻辑**

定义精确状态（`Idle/Downloading/Verifying/Installing/Completed/Canceled/Failed`）和进度字段（字节、总量、百分比、每秒速率、预计剩余、错误）。协调器接收目录定义、安装根目录、下载器和安装器；取消必须抛出 `OperationCanceledException` 并保留 `.partial`，安装失败不得覆盖已有完整模型。

**步骤 4：运行测试以验证它通过**

运行同一命令。

预期：PASS；测试使用内存 fake，不发出真实 HTTP 请求。

### 任务 4：Windows HTTP Range 下载与安全 ZIP 安装

**文件：**
- 创建：`src/VoxPen.Platform.Windows/Models/HttpRangeModelPackageDownloader.cs`
- 创建：`src/VoxPen.Platform.Windows/Models/ZipModelPackageInstaller.cs`
- 创建：`tests/VoxPen.Core.Tests/Models/HttpRangeModelPackageDownloaderTests.cs`
- 创建：`tests/VoxPen.Core.Tests/Models/ZipModelPackageInstallerTests.cs`

**步骤 1：编写失败的续传与解压安全测试**

以测试 HTTP handler 模拟服务器，断言已有 `.partial` 时请求带 `Range: bytes=<length>-`；用临时 ZIP 验证只在必需文件齐全后落位；构造 `../escape.txt` 条目，断言抛错且未写出目标根目录。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter "FullyQualifiedName~HttpRangeModelPackageDownloaderTests|FullyQualifiedName~ZipModelPackageInstallerTests"`

预期：FAIL，提示 Windows 下载器与安装器不存在。

**步骤 3：实现最少 Windows I/O**

下载器使用 `HttpClient.SendAsync(..., ResponseHeadersRead)`，从 `.partial` 长度请求范围；服务器忽略范围并返回 200 时安全重写该文件。安装器在目标同卷创建 GUID 临时目录，拒绝 Zip Slip，解压后递归查找且只接受目录定义对应的完整模型目录；使用显式、已验证路径移动到 `models/`。仅删除本次创建的临时目录和完成的压缩包；取消、网络失败和校验失败保留 `.partial`。

**步骤 4：运行测试以验证它通过**

运行同一命令。

预期：PASS；无测试文件留在工作区。

### 任务 5：GGUF C# 运行时兼容性闸门

**文件：**
- 修改：`src/VoxPen.Platform.Windows/VoxPen.Platform.Windows.csproj`
- 创建：`src/VoxPen.Platform.Windows/Recognition/Gguf/GgufDecoder.cs`
- 创建：`tests/VoxPen.Core.Tests/Recognition/GgufDecoderContractTests.cs`
- 创建：`docs/plans/2026-07-23-gguf-runtime-spike.md`

**步骤 1：先记录可验收的绑定契约**

测试仅针对项目自有接口：它必须加载 q5/k GGUF、允许 ONNX 编码器提供的嵌入作为解码提示、允许 CPU/Vulkan GPU 层配置、支持取消和确定性释放。

```csharp
public interface IGgufDecoder : IDisposable
{
    Task<string> DecodeAsync(ReadOnlyMemory<float> promptEmbeddings,
        GgufDecodeOptions options, CancellationToken cancellationToken);
}
```

**步骤 2：运行契约测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~GgufDecoderContractTests`

预期：FAIL，提示 `IGgufDecoder` / `GgufDecodeOptions` 不存在。

**步骤 3：验证并锁定依赖**

评估仅能满足“外部嵌入注入 + Qwen GGUF + Vulkan/CPU”的 C# 绑定；将选择、版本、原生 DLL 发布方式及一个使用原版 Qwen3/Fun-ASR 小样本的加载/解码结果记录在 spike 文档。若候选绑定不暴露嵌入注入 API，停止本任务并报告阻塞；不得以 Python 旁车或伪造“已支持”替代已确认的纯 C# 目标。

**步骤 4：实现最小适配器并验证**

将唯一通过闸门的绑定封装在 `GgufDecoder` 内，避免其类型穿透平台边界；运行契约测试和 `dotnet publish src/VoxPen.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/gguf-smoke`。

预期：测试通过；发布目录包含且能解析必需原生 DLL。

### 任务 6：SenseVoice-Small 引擎

**文件：**
- 创建：`src/VoxPen.Platform.Windows/Recognition/SenseVoiceEngine.cs`
- 修改：`src/VoxPen.Platform.Windows/Recognition/WindowsAsrEngineFactory.cs`
- 创建：`tests/VoxPen.Core.Tests/Recognition/SenseVoiceEngineCapabilitiesTests.cs`

**步骤 1：编写失败的能力测试**

```csharp
[Fact]
public void SenseVoice_declares_native_punctuation_and_timestamps()
{
    using var engine = new SenseVoiceEngine(new AsrConfig { Engine = AsrEngineKind.SenseVoice });
    engine.Capabilities.Should().Be(EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords);
}
```

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~SenseVoiceEngineCapabilitiesTests`

预期：FAIL，提示 `SenseVoiceEngine` 不存在。

**步骤 3：实现 SenseVoice ONNX 适配器**

以已有 `ParaformerEngine` 的串行化、取消、float32 音频和释放规则为模板；使用验证器返回的 encoder/decoder/tokenizer 路径建立 recognizer，移除模型输出中的语言/情绪事件标记，填充文本和可用时间戳。工厂按 `sensevoice` 返回该实现。

**步骤 4：运行测试与模型冒烟**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~SenseVoiceEngineCapabilitiesTests`

若已安装 SenseVoice 模型，运行：`dotnet run --project src/VoxPen.Cli -- --engine sensevoice --file <16k-wav>`。

预期：能力测试通过；有模型时 CLI 输出文本且不加载外挂标点。

### 任务 7：Fun-ASR-Nano 与 Qwen3-ASR 引擎

**文件：**
- 创建：`src/VoxPen.Platform.Windows/Recognition/FunAsrNanoEngine.cs`
- 创建：`src/VoxPen.Platform.Windows/Recognition/Qwen3AsrEngine.cs`
- 创建：`src/VoxPen.Platform.Windows/Recognition/FunAsrNanoPromptBuilder.cs`
- 创建：`src/VoxPen.Platform.Windows/Recognition/Qwen3AsrPromptBuilder.cs`
- 修改：`src/VoxPen.Platform.Windows/Recognition/WindowsAsrEngineFactory.cs`
- 创建：`tests/VoxPen.Core.Tests/Recognition/FunAsrNanoEngineCapabilitiesTests.cs`
- 创建：`tests/VoxPen.Core.Tests/Recognition/Qwen3AsrEngineCapabilitiesTests.cs`
- 创建：`tests/VoxPen.Core.Tests/Recognition/AsrPromptBuilderTests.cs`

**步骤 1：编写失败的能力和提示词测试**

断言 Fun-ASR-Nano 为 `Punctuation | Timestamps | Hotwords`，Qwen3-ASR 仅为 `Punctuation`；提示词测试覆盖空上下文、语言选择和热词上限，使用原版公开协议的固定 token/文本夹具。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter "FullyQualifiedName~FunAsrNano|FullyQualifiedName~Qwen3Asr|FullyQualifiedName~AsrPromptBuilder"`

预期：FAIL，提示两个引擎和提示词构造器不存在。

**步骤 3：实现 Fun-ASR-Nano**

加载 Encoder-Adaptor ONNX、CTC ONNX、tokens 和 GGUF；先由 CTC 从热词索引挑选最多 20 个候选，再以原版格式构造 prompt embedding 交给 `IGgufDecoder`，解析文本、内置标点和 token 时间戳。引擎内部保持单实例串行解码与统一释放。

**步骤 4：实现 Qwen3-ASR**

加载 frontend/backend ONNX 和 q4/q5 GGUF；按原版协议将音频 embedding、语言与上下文转换为解码 prompt，并限制单段至配置的 `ChunkSizeSeconds`。结果只返回文本；不得编造 token 时间戳。工厂注册两个键。

**步骤 5：运行测试与可选真模型冒烟**

运行：`dotnet test tests/VoxPen.Core.Tests --filter "FullyQualifiedName~FunAsrNano|FullyQualifiedName~Qwen3Asr|FullyQualifiedName~AsrPromptBuilder"`

若模型已安装，运行：
`dotnet run --project src/VoxPen.Cli -- --engine fun_asr_nano --file <16k-wav>`

`dotnet run --project src/VoxPen.Cli -- --engine qwen_asr --file <16k-wav>`

预期：单元测试通过；真模型命令能得到非空文本。

### 任务 8：AppHost、设置页和下载任务绑定

**文件：**
- 修改：`src/VoxPen.App/Services/AppHost.cs:65-170,208-327`
- 修改：`src/VoxPen.App/ViewModels/MainWindowViewModel.cs:20-286`
- 修改：`src/VoxPen.App/Views/MainWindow.axaml:93-160`
- 创建：`src/VoxPen.App/Services/ModelDownloadService.cs`
- 创建：`tests/VoxPen.Core.Tests/Models/ModelDownloadProgressTests.cs`

**步骤 1：编写失败的可观察下载状态测试**

以 fake 协调器驱动服务，断言新下载、取消、网络失败后重试和完成安装均生成正确的可展示进度；不在 ViewModel 测试中调用网络或实际解压。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter "FullyQualifiedName~ModelDownloadProgressTests"`

预期：FAIL，提示下载服务/状态投影不存在。

**步骤 3：实现服务与设置绑定**

AppHost 暴露模型清单、当前选择、保存引擎+目录的原子配置操作和下载服务。ViewModel 增加 `SelectedAsrModel`、模型卡集合、下载状态属性及 `DownloadModelCommand`、`CancelModelDownloadCommand`、`RetryModelDownloadCommand`；命令运行期间禁用冲突的操作。XAML 用 `ComboBox` 选择模型，展示描述、能力、大小、状态、`ProgressBar`、速度、剩余时间、失败原因和对应按钮。模型完成后自动重新校验并提示“已安装，重启后生效”。

**步骤 4：运行测试与 App 构建**

运行：`dotnet test tests/VoxPen.Core.Tests --filter FullyQualifiedName~ModelDownloadProgressTests; dotnet build src/VoxPen.App`

预期：测试通过，Avalonia XAML 编译通过。

### 任务 9：README、CLI 合同与全量验证

**文件：**
- 修改：`README.md:20-140,189-216,286-360`
- 修改：`README.zh-CN.md`（若该文件仍存在且内容与英文 README 分叉）
- 修改：`src/VoxPen.Cli/Program.cs:20-130,355-368`
- 创建：`tests/VoxPen.Core.Tests/Config/AsrConfigCompatibilityTests.cs`

**步骤 1：编写失败的兼容性与 CLI 参数测试**

反序列化缺失 `asr.engine` 的 JSON，断言 Paraformer；覆盖四个合法 `--engine` 值和非法值提示。将 CLI 参数解析提取为可测方法，避免通过 `Main` 断言控制台输出。

**步骤 2：运行测试以验证它失败**

运行：`dotnet test tests/VoxPen.Core.Tests --filter "FullyQualifiedName~AsrConfigCompatibilityTests"`

预期：FAIL，提示参数解析或兼容性行为未实现。

**步骤 3：更新用户文档和 CLI 帮助**

README 明确四模型的能力、磁盘空间、GPU 前提、下载/恢复/取消语义、Qwen 无时间戳限制和“模型切换后重启生效”。移除手动下载为唯一途径的叙述，但保留用户已下载模型可手动放置的说明。CLI 示例改为 `--engine <name> --model <dir>`。

**步骤 4：执行全量验证**

运行：

```powershell
dotnet test tests/VoxPen.Core.Tests
dotnet build src/VoxPen.App
dotnet build src/VoxPen.Cli
dotnet run --project src/VoxPen.Cli -- test-postprocess
dotnet run --project src/VoxPen.Cli -- test-hotword
dotnet run --project src/VoxPen.Cli -- test-diary
dotnet run --project src/VoxPen.Cli -- test-merger
```

预期：所有测试和构建通过，四个无模型 CLI 冒烟命令均返回 0。真模型下载与识别则在每个安装包可用时分别记录结果。

---

## 审查范围

详见：`docs/plans/2026-07-23-multi-asr-models-review-scope.md`
