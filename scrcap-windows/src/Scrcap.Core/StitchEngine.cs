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
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minOverlap);
        if (!double.IsFinite(tolerance) || tolerance < 0 || tolerance > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Must be finite and between 0 and 1.");
        }

        if (accumulated.Count == 0 || frame.Count == 0)
        {
            return null;
        }

        if (frame.Count <= accumulated.Count
            && Matches(accumulated, frame, frame.Count, tolerance))
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
            if (Matches(accumulated, frame, overlap, tolerance))
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

    private static bool Matches(IReadOnlyList<ulong> accumulated, IReadOnlyList<ulong> frame, int count, double tolerance)
    {
        var start = accumulated.Count - count;
        var possibleMatches = count;
        for (var index = 0; index < count; index++)
        {
            if (accumulated[start + index] != frame[index])
            {
                possibleMatches--;
                // Once too many rows differ, no later row can rescue this
                // candidate. Keep the original division semantics at exact
                // tolerance boundaries rather than rounding a mismatch quota.
                if ((double)possibleMatches / count < tolerance)
                {
                    return false;
                }
            }
        }

        return true;
    }

}

public readonly record struct FixedEdges(int Top, int Bottom);
