# 自动标点模型接入方案（CT-Transformer）

**日期**：2026-07-22
**状态**：P1 / P2 / P3 已实施；P4（`punc_converter` 全角→半角）为可选后续

## 背景

当前 VoxPen 不做任何**自动**标点补全：`ParaformerEngine` 只输出 Paraformer-zh 词表里恰好带的 `，/。`（有时有、有时没有），后处理链 `hot-rule → phoneme-rag → trash-punc` 也只**删**不**加**。用户想要连续口述被断成通顺句子的话，目前只能靠念"逗号 / 句号 / 问号 / 回车"关键词（`hot-rule.txt:19-22`）手动打点。

上游 `HaujetZhao/CapsWriter-Offline`（Python）走的是**独立标点模型 + 独立 Formatter 阶段**的路线，同一份 sherpa-onnx `OfflinePunctuation` 模型：`sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12`。本方案与上游对齐，落到 C# 侧。

## 目标

- 连续语流 ASR 后自动补中英标点（`，。？！`、`, . ? !`）。
- **能力位驱动**：Paraformer 挂载标点模型；未来若接 SenseVoice / Fun-ASR-Nano 等自带标点的引擎，自动跳过。
- 与现有 `hot-rule.txt` 语音关键词（`逗号/句号/问号/回车`）不打架。
- 与 `TrashPuncCleaner` 的"末尾标点裁剪"策略共存。
- 模型缺失时静默降级为"无标点模式"，不影响其余功能。
- 保留 `config.json` 后向兼容，不改动既有键名。

## 非目标

- 不引入基于规则/启发式的自造标点（会和模型打架，且质量差）。
- 不改动流式识别的中间输出——标点只在整段结束时补，与上游一致（避免流式回显反复变化）。
- 不做代码编辑器专用的全角→半角转换（上游 `punc_converter.py` 的能力），可作为独立后续任务。
- 本方案不引入模型自动下载 / 校验，用户按 README 的 `models/` 说明放置。

## 现状快照

关键路径（引用当前主干实际行号）：

- `src/VoxPen.Platform.Windows/Recognition/ParaformerEngine.cs:58-71` — 唯一的 `IAsrEngine`，只挂 Paraformer。
- `src/VoxPen.Core/Abstractions/IAsrEngine.cs:7-27` — 合约只有 `RecognizeAsync → RecognitionResult`，无能力位。
- `src/VoxPen.Core/Recognition/RecognitionResult.cs:8` — XML 注释显式声明 `Text` "未经过热词/规则/末尾标点处理"。
- `src/VoxPen.App/Services/AppHost.cs:196-208` — 组装 pipeline，`RunPostProcess` 是后处理链入口。
- `src/VoxPen.App/Services/AppHost.cs:267-297` — 当前 `hot-rule → phoneme-rag → trash-punc` 顺序。
- `src/VoxPen.Core/Config/AppConfig.cs:71-84` — `AsrConfig`，无标点字段。
- `src/VoxPen.Core/Config/AppConfig.cs:105-121` — `PostprocessConfig`，无标点字段。
- `hot-rule.txt:19-22` — `逗号/句号/问号/回车` 关键词替换，规则本身已经用 `[，。]?` 邻位吞噬为"标点模型污染"预留了裕量（这一点是从上游直接抄的，落地本方案时不需要改）。

## 参考：上游流水线（对齐目标）

```
音频 → ASR（Paraformer） → 简单合并/对齐
     → [is_final] 标点模型 CT-Transformer
     → ITN（中文数字→阿拉伯，可开关）
     → 中英空格调整（可开关）
     → sync_tokens_from_text（重对齐时间戳）
     → 客户端 hot.txt（拼音热词模糊匹配）
     → 客户端 hot-rule.txt（正则，含语音关键词还原）
     → SendInput
```

关键点：**标点跑在 hot-rule 之前**。`hot-rule.txt` 的 `[，。]?` 护栏正是为此设计。

## 方案

采用"独立 `IPunctuator` 抽象 + 引擎能力位"的路径（等价于上游）。

### 1. 抽象层（`VoxPen.Core`）

新增文件：

- `src/VoxPen.Core/Abstractions/IPunctuator.cs`
  ```csharp
  public interface IPunctuator : IAsyncDisposable
  {
      Task LoadAsync(CancellationToken ct = default);
      /// <summary>给一段无标点/半标点文本补上标点。加载失败或空串直接原样返回。</summary>
      string AddPunctuation(string text);
  }
  ```

- `src/VoxPen.Core/Abstractions/EngineCapabilities.cs`
  ```csharp
  [Flags]
  public enum EngineCapabilities
  {
      None       = 0,
      Punctuation = 1 << 0,
      Timestamps  = 1 << 1,
      Streaming   = 1 << 2,
      Hotwords    = 1 << 3,
  }
  ```

- 修改 `src/VoxPen.Core/Abstractions/IAsrEngine.cs`：新增只读属性
  ```csharp
  EngineCapabilities Capabilities { get; }
  ```
  Paraformer 实现返回 `Timestamps`（不含 `Punctuation`）；未来 SenseVoice 返回 `Punctuation | Timestamps`。默认接口实现放 `EngineCapabilities.None`，避免破坏可能存在的三方实现。

- 修改 `RecognitionResult.Text` XML 注释：把 "未经过热词/规则/末尾标点处理" 改为 "未经过热词/规则处理；是否包含标点取决于 ASR 引擎能力"。

### 2. 配置（`VoxPen.Core/Config/AppConfig.cs`）

在 `AppConfig` 上加一段新配置块，**不改动**任何现有字段：

```csharp
public sealed class PunctuationConfig
{
    /// <summary>标点模型目录（相对应用根目录）。空字符串或路径不存在时禁用标点补全。</summary>
    public string ModelDir { get; set; } =
        "models/Punct-CT-Transformer/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12";

    /// <summary>推理线程数。</summary>
    public int NumThreads { get; set; } = 2;

    /// <summary>Provider：cpu / directml（当前仅 cpu）。</summary>
    public string Provider { get; set; } = "cpu";
}
```

`AppConfig` 新增 `public PunctuationConfig Punctuation { get; set; } = new();`。`AppHost.LoadOrCreateConfig` 反序列化时字段缺失自动填默认，符合"配置向后兼容"的约束。

**不加 `Enabled` 布尔**——对齐上游"删模型即禁用"策略，行为更简单，可预测（用户看不到"打开开关但没模型"这种坑）。

### 3. Windows 实现（`VoxPen.Platform.Windows`）

新增 `src/VoxPen.Platform.Windows/Recognition/SherpaPunctuator.cs`：

```csharp
public sealed class SherpaPunctuator : IPunctuator
{
    private readonly string _modelPath;   // 具体到 model.onnx
    private readonly int _numThreads;
    private readonly string _provider;
    private OfflinePunctuation? _engine;

    public Task LoadAsync(CancellationToken ct = default)
    {
        var cfg = new OfflinePunctuationConfig
        {
            Model = new OfflinePunctuationModelConfig
            {
                CtTransformer = _modelPath,
                NumThreads = _numThreads,
                Provider = _provider,
            },
        };
        _engine = new OfflinePunctuation(cfg);
        return Task.CompletedTask;
    }

    public string AddPunctuation(string text)
    {
        if (_engine is null || string.IsNullOrWhiteSpace(text)) return text;
        return _engine.AddPunct(text);
    }
    // Dispose 略
}
```

- 模型加载失败（找不到 `model.onnx`、ONNX 兼容问题）由 `AppHost` 层 `try/catch` 后降级为 `NullPunctuator`（no-op），日志输出：`[punc] 标点模型加载失败，进入无标点模式：<原因>`，对齐上游。
- 若目录里不存在 `model.onnx`，直接注入 `NullPunctuator`，不打 error（配置字段可能就是"不启用"）。

新增 `src/VoxPen.Core/Postprocess/NullPunctuator.cs`：`AddPunctuation(text) => text`。

### 4. 装配（`AppHost.cs`）

在 `AppHost.Create` 里、`pipeline` 组装之前根据引擎能力位决定注入哪种 `IPunctuator`：

```csharp
IPunctuator punctuator = NullPunctuator.Instance;
if ((asr.Capabilities & EngineCapabilities.Punctuation) == 0)
{
    var absDir = Path.Combine(appBaseDir, config.Punctuation.ModelDir);
    var modelFile = Path.Combine(absDir, "model.onnx");
    if (File.Exists(modelFile))
    {
        try
        {
            var p = new SherpaPunctuator(modelFile,
                                         config.Punctuation.NumThreads,
                                         config.Punctuation.Provider);
            await p.LoadAsync().ConfigureAwait(false);   // 见下方“加载时机”
            punctuator = p;
        }
        catch (Exception ex)
        {
            emit($"[punc] 标点模型加载失败，进入无标点模式：{ex.Message}");
        }
    }
}
```

修改 `RunPostProcess` 顺序，把标点放到 **hot-rule 之前**：

```
punctuator → hot-rule → phoneme-rag → trash-punc
```

理由：`hot-rule.txt:19-22` 的 `[，。]?` 邻位吞噬正是设计来吸掉标点模型在"逗号/句号/问号/回车"周围可能多加的标点。顺序颠倒会失去这层护栏。

### 5. 加载时机

标点模型是 ONNX，首次 `new OfflinePunctuation(...)` 也不算轻量（~150–300 ms）。策略：

- 主入口 `LoadAndStartAsync()` 里，Paraformer 模型加载完成后紧跟着加载标点模型，日志和现有的 `模型加载完成，耗时 X ms` 对齐。
- 加载失败**不阻塞** ASR，走降级路径。
- 热重载（`config.json` 中 `Punctuation.ModelDir` 变了）**不支持**——和 `ModelDir` 一样归入"需要重启"，UI 提示 "重启后生效"。这一条在 README "Hot-reload contract" 段补一行。

### 6. 冲突与边界

| 场景 | 处理 |
|---|---|
| 用户念"逗号" | ASR 出 `逗号` → 标点模型可能加成 `，逗号，` → `hot-rule.txt:19` 正则吞掉两侧再替换成 `，`。无需改规则文件。 |
| 短句 `打开微信` | 标点模型可能加成 `打开微信。` → `TrashPuncCleaner` 按长度阈值 `TrashPuncThreshold=8` 剥掉。既有行为无变化。 |
| 强制去标点应用（如 `WeiXin.exe` 一行发送） | `TrashPuncApps` 逻辑不变，末尾 `，。` 仍会被剥掉；句中标点保留，符合"聊天窗口一行发送但句中可读"的直觉。 |
| 中英混说 | CT-Transformer 词表 zh-en 通用，支持。 |
| 空文本 / 纯符号 | `SherpaPunctuator.AddPunctuation` 短路返回原串。 |
| 模型加载失败 | `NullPunctuator` 降级，其它功能不受影响，日志一次性给用户。 |

### 7. 目录约定

模型放置路径与上游一致：

```
models/
  paraformer/               # 现有
  Punct-CT-Transformer/
    sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12/
      model.onnx
```

用户已经装过上游 Python 项目的话，可以直接复用其 `models/Punct-CT-Transformer/...` 目录（结构完全一致）。README `models/` 一节增加对应下载说明和目录树示例，`.gitignore` 无需改（`models/` 已经忽略）。

## 兼容性

- `config.json`：仅新增 `punctuation` 段，旧配置反序列化补默认，零破坏。
- `hot-rule.txt` / `hot.txt`：无格式变化。
- `IAsrEngine`：新增只读属性 `Capabilities`。由于同项目内只有 `ParaformerEngine` 一个实现，直接改；如担心三方实现兼容，可给接口写默认实现返回 `None`。
- `RecognitionResult` 结构不变，仅注释语义调整。
- CLI (`test-postprocess` 等) 不受影响；`test-postprocess` 输出可选加一行 "标点：启用/未启用"。
- 单文件发布体积：额外 ~280 MB 模型不进 exe，仍走 `models/` 侧载；exe 本身增量 ~0（`OfflinePunctuation` API 已在 `sherpa-onnx` 依赖里，无需新增 NuGet 包）。

## 测试

`tests/VoxPen.Core.Tests/Postprocess/` 下新增：

- `NullPunctuatorTests.cs` — 空实现幂等。
- `PostProcessOrderingTests.cs` — 断言 `punctuator → hot-rule → phoneme-rag → trash-punc` 顺序：注入 fake `IPunctuator`（把整段文本改成 `A，B。`）+ 现有 hot-rule 规则 + trash-punc，验证：
  - "逗号"关键词在 punc 之后仍被规则正确替换。
  - 短句末尾标点被 `TrashPuncCleaner` 按阈值剥掉。
- `AsrCapabilitiesTests.cs` — 断言 `ParaformerEngine.Capabilities` 不含 `Punctuation`。

不为 `SherpaPunctuator` 写在线单测（依赖真实 ONNX 模型），改为在 `VoxPen.Cli` 里加一条 `test-punc <text>` 冒烟命令，在有模型的机器上手工验证：
- `test-punc "今天天气不错我们出去走走"` → `今天天气不错，我们出去走走。`
- `test-punc ""` → 空串。
- 无模型时提示 "无标点模式" 并原样输出。

集成回归：`dotnet run --project src/VoxPen.Cli -- test-postprocess`（现有）保证既有链路不炸；`--file <wav>` 手工听感对比一次开/关。

## 分阶段落地

1. **P1 — 抽象与配置**：`IPunctuator` / `EngineCapabilities` / `PunctuationConfig` / `NullPunctuator`，`ParaformerEngine.Capabilities` 返回 `Timestamps`，`AppHost` 走 no-op 注入。零行为变化。跑通 `dotnet test`。✅ **已实施**
2. **P2 — Windows 实现**：`SherpaPunctuator`，`AppHost` 按配置加载并注入，后处理顺序调整为 punc-first。手工在 `models/Punct-CT-Transformer/...` 放模型验证。✅ **已实施**（缺模型时静默降级为 `NullPunctuator`）
3. **P3 — CLI 与文档**：`test-punc` 冒烟命令，README "config"、"models" 两节补条目，README "Hot-reload contract" 补一行"标点模型路径变更需重启"。✅ **已实施**
4. **P4 — 可选**：`punc_converter` 端口（针对代码编辑器把全角→半角），走前台窗口白名单。独立任务，不阻塞本方案。⏸ **未启动**

## 风险与备选

- **风险 1：CT-Transformer 对短促指令过度加点**。缓解：`TrashPuncCleaner.Threshold` 已经是 8 字，短句末尾会被清；如果用户反馈句中乱加点，考虑在 `PunctuationConfig` 加 `MinTextLength`（不启动标点模型的最短字符数），默认 0（无限制），作为后续微调。
- **风险 2：ONNX Runtime 版本不匹配**。`OfflinePunctuation` 与 `OfflineRecognizer` 都在 `sherpa-onnx` NuGet 里，版本一致，无额外依赖冲突风险。
- **风险 3：SharpHook / Paste 路径下"逗号"关键词位置飘移**。已由 `hot-rule.txt:19-22` 的正则前后 `[，。]?` 吞噬处理，本方案不需改规则。
- **备选（弃）**：把标点塞进 `ParaformerEngine` 内部（早前讨论中的"方案 A"）。舍弃理由：与未来 SenseVoice / Fun-ASR-Nano 的能力位设计不符，需在每个引擎实现里重复标点加载逻辑；上游 Python 项目也没走这条路。

## 参考

- 上游流水线与模型选型：`HaujetZhao/CapsWriter-Offline`，`core/server/formatter/text_formatter.py`、`core/server/engines/ct_transformer/punc_engine.py`、`core/server/worker/model_loader.py`、`core/server/worker/pipeline.py`。
- 模型：sherpa-onnx `sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12`，k2-fsa 官网预训练页。
- 本仓相关：`src/VoxPen.App/Services/AppHost.cs:196-297`、`src/VoxPen.Platform.Windows/Recognition/ParaformerEngine.cs:19-137`、`hot-rule.txt:19-22`。
