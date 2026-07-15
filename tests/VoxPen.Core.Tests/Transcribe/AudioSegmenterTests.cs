using VoxPen.Core.Transcribe;
using FluentAssertions;
using Xunit;

namespace VoxPen.Core.Tests.Transcribe;

public class AudioSegmenterTests
{
    [Fact]
    public void EmptyInput_YieldsNothing()
    {
        var segs = AudioSegmenter.Segment(Array.Empty<float>(), 16000, 60, 4).ToList();
        segs.Should().BeEmpty();
    }

    [Fact]
    public void ShorterThanSegment_YieldsOne()
    {
        var samples = new float[16000 * 3]; // 3s
        var segs = AudioSegmenter.Segment(samples, 16000, 60, 4).ToList();
        segs.Should().HaveCount(1);
        segs[0].Samples.Length.Should().Be(samples.Length);
        segs[0].OffsetSeconds.Should().Be(0.0);
    }

    [Fact]
    public void ExactlyOneSegment_YieldsOne()
    {
        var samples = new float[16000 * 60]; // 60s
        var segs = AudioSegmenter.Segment(samples, 16000, 60, 4).ToList();
        segs.Should().HaveCount(1);
        segs[0].Samples.Length.Should().Be(samples.Length);
    }

    [Fact]
    public void MultipleSegments_HaveOverlapAndCorrectOffsets()
    {
        // 130s @ 16k, seg=60s stride=56s → offsets 0, 56, 112; last ends at 130
        var samples = new float[16000 * 130];
        var segs = AudioSegmenter.Segment(samples, 16000, 60, 4).ToList();
        segs.Should().HaveCount(3);
        segs[0].OffsetSeconds.Should().Be(0.0);
        segs[1].OffsetSeconds.Should().BeApproximately(56.0, 1e-6);
        segs[2].OffsetSeconds.Should().BeApproximately(112.0, 1e-6);
        segs[0].Samples.Length.Should().Be(16000 * 60);
        segs[1].Samples.Length.Should().Be(16000 * 60);
        segs[2].Samples.Length.Should().Be(16000 * (130 - 112));
    }

    [Fact]
    public void ZeroOverlap_YieldsBackToBack()
    {
        var samples = new float[16000 * 90];
        var segs = AudioSegmenter.Segment(samples, 16000, 30, 0).ToList();
        segs.Should().HaveCount(3);
        segs[0].OffsetSeconds.Should().Be(0.0);
        segs[1].OffsetSeconds.Should().BeApproximately(30.0, 1e-6);
        segs[2].OffsetSeconds.Should().BeApproximately(60.0, 1e-6);
    }

    [Fact]
    public void OverlapGreaterOrEqualDuration_Throws()
    {
        Action a = () => AudioSegmenter.Segment(new float[100], 16000, 1, 1).ToList();
        a.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NegativeOverlap_Throws()
    {
        Action a = () => AudioSegmenter.Segment(new float[100], 16000, 1, -0.1).ToList();
        a.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SamplesAreIndependentCopies()
    {
        var samples = Enumerable.Range(0, 16000 * 3).Select(i => (float)i).ToArray();
        var segs = AudioSegmenter.Segment(samples, 16000, 1, 0).ToList();
        segs.Should().HaveCount(3);
        // 修改原数组不影响已产出的段
        Array.Clear(samples);
        segs[1].Samples[0].Should().Be(16000f);
    }
}
