using System.Text.Json;
using FluentAssertions;
using VoxPen.Core.Config;
using Xunit;

namespace VoxPen.Core.Tests.Config;

/// <summary>
/// <see cref="PunctuationConfig"/> 的默认值与 JSON 反序列化兼容性。
/// 老 config.json 缺失 <c>punctuation</c> 段时应回退到默认值，不能报错。
/// </summary>
public sealed class PunctuationConfigTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void Defaults_match_upstream_ct_transformer_layout()
    {
        var cfg = new PunctuationConfig();

        // 对齐上游 HaujetZhao/CapsWriter-Offline 的 models/Punct-CT-Transformer 目录，
        // 用户可直接复用同一份 model.onnx。
        cfg.ModelDir.Should().Be(
            "models/Punct-CT-Transformer/sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12");
        cfg.NumThreads.Should().Be(2);
        cfg.Provider.Should().Be("cpu");
    }

    [Fact]
    public void AppConfig_defaults_include_punctuation_section()
    {
        var cfg = new AppConfig();

        cfg.Punctuation.Should().NotBeNull();
        cfg.Punctuation.ModelDir.Should().NotBeNullOrEmpty();
        cfg.Punctuation.NumThreads.Should().BeGreaterThan(0);
    }

    [Fact]
    public void AppConfig_deserialization_backfills_punctuation_when_missing()
    {
        // 模拟一份"旧版" config.json：完全没有 punctuation 字段
        const string legacyJson = """
        {
          "shortcut": { "key": "caps_lock" },
          "asr": { "engine": "paraformer" }
        }
        """;

        var cfg = JsonSerializer.Deserialize<AppConfig>(legacyJson, Options);

        cfg.Should().NotBeNull();
        cfg!.Punctuation.Should().NotBeNull();
        cfg.Punctuation.ModelDir.Should().Be(new PunctuationConfig().ModelDir);
        cfg.Punctuation.NumThreads.Should().Be(new PunctuationConfig().NumThreads);
        cfg.Punctuation.Provider.Should().Be(new PunctuationConfig().Provider);
    }

    [Fact]
    public void AppConfig_deserialization_preserves_user_overrides()
    {
        const string userJson = """
        {
          "punctuation": {
            "modelDir": "D:/models/custom-punc",
            "numThreads": 4,
            "provider": "cpu"
          }
        }
        """;

        var cfg = JsonSerializer.Deserialize<AppConfig>(userJson, Options);

        cfg.Should().NotBeNull();
        cfg!.Punctuation.ModelDir.Should().Be("D:/models/custom-punc");
        cfg.Punctuation.NumThreads.Should().Be(4);
        cfg.Punctuation.Provider.Should().Be("cpu");
    }

    [Fact]
    public void AppConfig_roundtrip_serialization_keeps_punctuation()
    {
        var original = new AppConfig();
        original.Punctuation.NumThreads = 3;
        original.Punctuation.ModelDir = "models/punc-alt";

        var json = JsonSerializer.Serialize(original, Options);
        var restored = JsonSerializer.Deserialize<AppConfig>(json, Options);

        restored.Should().NotBeNull();
        restored!.Punctuation.NumThreads.Should().Be(3);
        restored.Punctuation.ModelDir.Should().Be("models/punc-alt");
    }
}
