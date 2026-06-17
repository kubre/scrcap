using Scrcap.Core;

namespace Scrcap.Core.Tests;

public sealed class StitchEngineTests
{
    [Fact]
    public void RowHashUsesFnv1A()
    {
        Assert.Equal(0xbe7a5e775165785dUL, StitchEngine.RowHash([1, 2, 3, 4]));
    }

    [Fact]
    public void AlignsLargestSuffixPrefixOverlap()
    {
        var accumulated = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var frame = Enumerable.Range(70, 80).Select(i => (ulong)i).ToArray();

        var alignment = StitchEngine.Align(accumulated, frame);

        Assert.Equal(30, alignment?.NewContentStart);
    }

    [Fact]
    public void AlignmentAllowsTwoPercentRowNoise()
    {
        var accumulated = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var frame = Enumerable.Range(50, 80).Select(i => (ulong)i).ToArray();
        frame[10] = 9_999;

        var alignment = StitchEngine.Align(accumulated, frame, tolerance: 0.98);

        Assert.Equal(50, alignment?.NewContentStart);
    }

    [Fact]
    public void AlignmentRequiresMinimumSixteenRowsByDefault()
    {
        var accumulated = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var frame = Enumerable.Range(90, 20).Select(i => (ulong)i).ToArray();

        var alignment = StitchEngine.Align(accumulated, frame);

        Assert.Null(alignment);
    }

    [Fact]
    public void IdenticalFrameReturnsNoNewRows()
    {
        var accumulated = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var frame = Enumerable.Range(20, 80).Select(i => (ulong)i).ToArray();

        var alignment = StitchEngine.Align(accumulated, frame);

        Assert.Equal(frame.Length, alignment?.NewContentStart);
    }

    [Fact]
    public void NonScrollableOrUnrelatedFrameDoesNotAlign()
    {
        var accumulated = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var frame = Enumerable.Range(1_000, 80).Select(i => (ulong)i).ToArray();

        var alignment = StitchEngine.Align(accumulated, frame);

        Assert.Null(alignment);
    }

    [Fact]
    public void DetectsFixedTopAndBottomRows()
    {
        ulong[] first = [1, 1, 10, 11, 12, 9];
        ulong[] second = [1, 1, 20, 21, 22, 9];
        ulong[] third = [1, 1, 30, 31, 32, 9];

        var edges = StitchEngine.DetectFixedEdges([first, second, third]);

        Assert.Equal(new FixedEdges(2, 1), edges);
    }
}
