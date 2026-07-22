using SherpaOnnx;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;

namespace VoxPen.Platform.Windows.Recognition;

public sealed class Qwen3AsrEngine : SherpaOfflineAsrEngineBase
{
    public Qwen3AsrEngine(AsrConfig config) : base(config) { }
    public override string Name => "qwen3-asr-onnx";
    public override EngineCapabilities Capabilities => EngineCapabilities.Punctuation;

    protected override OfflineRecognizerConfig CreateRecognizerConfig()
    {
        var result = CreateBaseConfig();
        result.ModelConfig.Qwen3Asr.ConvFrontend = Path.Combine(Config.ModelDir, "conv_frontend.onnx");
        result.ModelConfig.Qwen3Asr.Encoder = Path.Combine(Config.ModelDir, "encoder.int8.onnx");
        result.ModelConfig.Qwen3Asr.Decoder = Path.Combine(Config.ModelDir, "decoder.int8.onnx");
        result.ModelConfig.Qwen3Asr.Tokenizer = Path.Combine(Config.ModelDir, "tokenizer");
        result.ModelConfig.Qwen3Asr.MaxTotalLen = 512;
        result.ModelConfig.Qwen3Asr.MaxNewTokens = 512;
        result.ModelConfig.ModelType = "qwen3_asr";
        return result;
    }
}
