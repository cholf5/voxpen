using VoxPen.Core.Abstractions;

namespace VoxPen.Core.Config;

/// <summary>VoxPen 支持的模型清单，下载、校验、UI 和 CLI 共用此数据源。</summary>
public static class AsrModelCatalog
{
    private const string UpstreamReleaseBaseUrl =
        "https://github.com/HaujetZhao/CapsWriter-Offline/releases/download/models/";
    private const string SherpaReleaseBaseUrl =
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/";

    private static readonly IReadOnlyList<AsrModelDefinition> Definitions =
    [
        new(
            AsrEngineKind.Paraformer,
            "Paraformer",
            "models/paraformer",
            ["model.onnx", "tokens.txt"],
            EngineCapabilities.Timestamps,
            "Paraformer.zip",
            new Uri(UpstreamReleaseBaseUrl + "Paraformer.zip"),
            239_979_687,
            "速度最快；需要外挂标点模型。"),
        new(
            AsrEngineKind.SenseVoice,
            "SenseVoice-Small",
            "models/sensevoice",
            ["model.int8.onnx", "tokens.txt"],
            EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords,
            "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09.tar.bz2",
            new Uri(SherpaReleaseBaseUrl + "sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2025-09-09.tar.bz2"),
            165_783_878,
            "高速多语种识别，支持 DirectML。"),
        new(
            AsrEngineKind.FunAsrNano,
            "Fun-ASR-Nano",
            "models/fun-asr-nano",
            ["encoder_adaptor.int8.onnx", "embedding.int8.onnx", "llm.int8.onnx",
                "Qwen3-0.6B/tokenizer.json"],
            EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords,
            "sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2",
            new Uri(SherpaReleaseBaseUrl + "sherpa-onnx-funasr-nano-int8-2025-12-30.tar.bz2"),
            994_050_000,
            "准确度与速度兼顾，支持 GPU 解码。"),
        new(
            AsrEngineKind.QwenAsr,
            "Qwen3-ASR",
            "models/qwen3-asr",
            ["conv_frontend.onnx", "encoder.int8.onnx", "decoder.int8.onnx",
                "tokenizer/vocab.json", "tokenizer/merges.txt", "tokenizer/tokenizer_config.json"],
            EngineCapabilities.Punctuation,
            "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2",
            new Uri(SherpaReleaseBaseUrl + "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2"),
            878_702_423,
            "准确度最高；不提供字级时间戳。"),
    ];

    public static IReadOnlyList<AsrModelDefinition> All => Definitions;

    public static AsrModelDefinition Get(AsrEngineKind kind) => Definitions.First(model => model.Kind == kind);
}
