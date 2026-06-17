namespace Scrcap.Core;

public static class StitchEngine
{
    private const ulong FnvOffset = 0xcbf29ce484222325;
    private const ulong FnvPrime = 0x100000001b3;

    public static ulong RowHash(ReadOnlySpan<byte> bytes)
    {
        var hash = FnvOffset;
        foreach (var value in bytes)
        {
            hash = (hash ^ value) * FnvPrime;
        }

        return hash;
    }

    public readonly record struct Alignment(int NewContentStart);

    public static Alignment? Align(
        IReadOnlyList<ulong> accumulated,
        IReadOnlyList<ulong> frame,
        int minOverlap = 16,
        double tolerance = 0.98)
    {
        if (accumulated.Count == 0 || frame.Count == 0)
        {
            return null;
        }

        if (frame.Count <= accumulated.Count
            && MatchRatio(Tail(accumulated, frame.Count), frame) >= tolerance)
        {
            return new Alignment(frame.Count);
        }

        var maxOverlap = Math.Min(accumulated.Count, frame.Count);
        if (maxOverlap < minOverlap)
        {
            return null;
        }

        for (var overlap = maxOverlap; overlap >= minOverlap; overlap--)
        {
            if (MatchRatio(Tail(accumulated, overlap), Head(frame, overlap)) >= tolerance)
            {
                return new Alignment(overlap);
            }
        }

        return null;
    }

    public static FixedEdges DetectFixedEdges(IReadOnlyList<IReadOnlyList<ulong>> frames)
    {
        if (frames.Count < 2)
        {
            return new FixedEdges(0, 0);
        }

        var first = frames[0];
        var minCount = frames.Min(frame => frame.Count);
        var top = 0;
        while (top < minCount && frames.All(frame => frame[top] == first[top]))
        {
            top++;
        }

        var bottom = 0;
        while (bottom < minCount - top
            && frames.All(frame => frame[frame.Count - 1 - bottom] == first[first.Count - 1 - bottom]))
        {
            bottom++;
        }

        return new FixedEdges(top, bottom);
    }

    private static IReadOnlyList<ulong> Tail(IReadOnlyList<ulong> values, int count) =>
        values.Skip(values.Count - count).Take(count).ToArray();

    private static IReadOnlyList<ulong> Head(IReadOnlyList<ulong> values, int count) =>
        values.Take(count).ToArray();

    private static double MatchRatio(IReadOnlyList<ulong> a, IReadOnlyList<ulong> b)
    {
        if (a.Count != b.Count || a.Count == 0)
        {
            return 0;
        }

        var equal = 0;
        for (var index = 0; index < a.Count; index++)
        {
            if (a[index] == b[index])
            {
                equal++;
            }
        }

        return (double)equal / a.Count;
    }
}

public readonly record struct FixedEdges(int Top, int Bottom);
