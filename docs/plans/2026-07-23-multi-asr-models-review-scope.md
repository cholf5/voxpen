# 审查范围

**关联计划**: docs/plans/2026-07-23-multi-asr-models.md
**开始时间**: 2026-07-23 00:00:00 +08:00

## 预期修改文件

- src/VoxPen.Core/Config/AppConfig.cs
- src/VoxPen.Core/Config/AsrEngineKind.cs
- src/VoxPen.Core/Config/AsrModelDefinition.cs
- src/VoxPen.Core/Config/AsrModelCatalog.cs
- src/VoxPen.Core/Config/AsrModelValidator.cs
- src/VoxPen.Core/Config/ModelDirectoryValidator.cs
- src/VoxPen.Core/Models/ModelDownloadState.cs
- src/VoxPen.Core/Models/ModelDownloadProgress.cs
- src/VoxPen.Core/Models/IModelPackageDownloader.cs
- src/VoxPen.Core/Models/IModelPackageInstaller.cs
- src/VoxPen.Core/Models/ModelInstallCoordinator.cs
- src/VoxPen.Platform.Windows/VoxPen.Platform.Windows.csproj
- src/VoxPen.Platform.Windows/Recognition/WindowsAsrEngineFactory.cs
- src/VoxPen.Platform.Windows/Recognition/SenseVoiceEngine.cs
- src/VoxPen.Platform.Windows/Recognition/FunAsrNanoEngine.cs
- src/VoxPen.Platform.Windows/Recognition/Qwen3AsrEngine.cs
- src/VoxPen.Platform.Windows/Recognition/FunAsrNanoPromptBuilder.cs
- src/VoxPen.Platform.Windows/Recognition/Qwen3AsrPromptBuilder.cs
- src/VoxPen.Platform.Windows/Recognition/Gguf/GgufDecoder.cs
- src/VoxPen.Platform.Windows/Models/HttpRangeModelPackageDownloader.cs
- src/VoxPen.Platform.Windows/Models/ZipModelPackageInstaller.cs
- src/VoxPen.App/Services/AppHost.cs
- src/VoxPen.App/Services/ModelDownloadService.cs
- src/VoxPen.App/ViewModels/MainWindowViewModel.cs
- src/VoxPen.App/Views/MainWindow.axaml
- src/VoxPen.Cli/Program.cs
- README.md
- README.zh-CN.md
- docs/plans/2026-07-23-gguf-runtime-spike.md
- tests/VoxPen.Core.Tests/Config/AsrModelCatalogTests.cs
- tests/VoxPen.Core.Tests/Config/AsrModelValidatorTests.cs
- tests/VoxPen.Core.Tests/Config/AsrConfigCompatibilityTests.cs
- tests/VoxPen.Core.Tests/Recognition/WindowsAsrEngineFactoryTests.cs
- tests/VoxPen.Core.Tests/Recognition/GgufDecoderContractTests.cs
- tests/VoxPen.Core.Tests/Recognition/SenseVoiceEngineCapabilitiesTests.cs
- tests/VoxPen.Core.Tests/Recognition/FunAsrNanoEngineCapabilitiesTests.cs
- tests/VoxPen.Core.Tests/Recognition/Qwen3AsrEngineCapabilitiesTests.cs
- tests/VoxPen.Core.Tests/Recognition/AsrPromptBuilderTests.cs
- tests/VoxPen.Core.Tests/Models/ModelInstallCoordinatorTests.cs
- tests/VoxPen.Core.Tests/Models/HttpRangeModelPackageDownloaderTests.cs
- tests/VoxPen.Core.Tests/Models/ZipModelPackageInstallerTests.cs
- tests/VoxPen.Core.Tests/Models/ModelDownloadProgressTests.cs

## 设计文档

- docs/plans/2026-07-23-multi-asr-models-design.md
