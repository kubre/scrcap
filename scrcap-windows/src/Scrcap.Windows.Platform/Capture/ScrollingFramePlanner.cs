using Scrcap.Core;

namespace Scrcap.Windows.Platform.Capture;

internal static class ScrollingFramePlanner
{
    public static int DetectStickyHeaderRows(IReadOnlyList<ulong> firstFrameHashes, IReadOnlyList<ulong> nextFrameHashes, int maxStickyRows)
    {
        var limit = Math.Min(Math.Min(firstFrameHashes.Count, nextFrameHashes.Count), Math.Max(0, maxStickyRows));
        var stickyRows = 0;
        while (stickyRows < limit && firstFrameHashes[stickyRows] == nextFrameHashes[stickyRows])
        {
            stickyRows++;
        }

        return stickyRows;
    }

    public static int? FindNewContentStart(IReadOnlyList<ulong> accumulatedHashes, IReadOnlyList<ulong> nextFrameHashes, int stickyRows)
    {
        if (nextFrameHashes.Count == 0)
        {
            return null;
        }

        var clampedStickyRows = Math.Clamp(stickyRows, 0, nextFrameHashes.Count);
        var searchableRows = nextFrameHashes.Skip(clampedStickyRows).ToArray();
        if (searchableRows.Length == 0)
        {
            return nextFrameHashes.Count;
        }

        if (StitchEngine.Align(accumulatedHashes, searchableRows) is { } alignment)
        {
            return clampedStickyRows + alignment.NewContentStart;
        }

        if (FindSuffixPrefixOverlap(accumulatedHashes, searchableRows) is { } overlap)
        {
            return clampedStickyRows + overlap;
        }

        if (RowsEqual(accumulatedHashes.TakeLast(nextFrameHashes.Count).ToArray(), nextFrameHashes))
        {
            return nextFrameHashes.Count;
        }

        return null;
    }

    public static bool RowsEqual(IReadOnlyList<ulong> left, IReadOnlyList<ulong> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int? FindSuffixPrefixOverlap(IReadOnlyList<ulong> accumulatedHashes, IReadOnlyList<ulong> nextFrameHashes)
    {
        var maxOverlap = Math.Min(accumulatedHashes.Count, nextFrameHashes.Count);
        for (var overlap = maxOverlap; overlap >= 2; overlap--)
        {
            var matches = true;
            var accumulatedStart = accumulatedHashes.Count - overlap;
            for (var index = 0; index < overlap; index++)
            {
                if (accumulatedHashes[accumulatedStart + index] != nextFrameHashes[index])
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return overlap;
            }
        }

        return null;
    }
}
