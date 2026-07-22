using SherpaOnnx;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;

namespace VoxPen.Platform.Windows.Recognition;

public sealed class FunAsrNanoEngine : SherpaOfflineAsrEngineBase
{
    public FunAsrNanoEngine(AsrConfig config) : base(config) { }
    public override string Name => "fun-asr-nano-onnx";
    public override EngineCapabilities Capabilities =>
        EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords;

    protected override OfflineRecognizerConfig CreateRecognizerConfig()
    {
        var result = CreateBaseConfig();
        result.ModelConfig.FunAsrNano.EncoderAdaptor = Path.Combine(Config.ModelDir, "encoder_adaptor.int8.onnx");
        result.ModelConfig.FunAsrNano.Embedding = Path.Combine(Config.ModelDir, "embedding.int8.onnx");
        result.ModelConfig.FunAsrNano.LLM = Path.Combine(Config.ModelDir, "llm.int8.onnx");
        result.ModelConfig.FunAsrNano.Tokenizer = Path.Combine(Config.ModelDir, "Qwen3-0.6B", "tokenizer.json");
        result.ModelConfig.FunAsrNano.Language = "auto";
        result.ModelConfig.FunAsrNano.Itn = 1;
        result.ModelConfig.FunAsrNano.MaxNewTokens = 512;
        result.ModelConfig.ModelType = "fun_asr_nano";
        return result;
    }
}
