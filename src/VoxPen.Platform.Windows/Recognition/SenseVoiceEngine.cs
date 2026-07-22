using SherpaOnnx;
using VoxPen.Core.Abstractions;
using VoxPen.Core.Config;

namespace VoxPen.Platform.Windows.Recognition;

public sealed class SenseVoiceEngine : SherpaOfflineAsrEngineBase
{
    public SenseVoiceEngine(AsrConfig config) : base(config) { }
    public override string Name => "sensevoice-onnx";
    public override EngineCapabilities Capabilities =>
        EngineCapabilities.Punctuation | EngineCapabilities.Timestamps | EngineCapabilities.Hotwords;

    protected override OfflineRecognizerConfig CreateRecognizerConfig()
    {
        var result = CreateBaseConfig();
        result.ModelConfig.Tokens = Path.Combine(Config.ModelDir, "tokens.txt");
        result.ModelConfig.SenseVoice.Model = Path.Combine(Config.ModelDir, "model.int8.onnx");
        result.ModelConfig.SenseVoice.Language = "auto";
        result.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
        result.ModelConfig.ModelType = "sense_voice";
        return result;
    }
}
