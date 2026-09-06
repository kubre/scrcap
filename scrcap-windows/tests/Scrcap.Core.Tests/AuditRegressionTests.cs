using System.Text.Json;
using Scrcap.Core;
using Xunit.Abstractions;

namespace Scrcap.Core.Tests;

public sealed class AuditRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("LPT1.log", "_LPT1.log")]
    [InlineData("com9", "_com9")]
    [InlineData("COM¹", "_COM¹")]
    [InlineData("LPT².foo", "_LPT².foo")]
    [InlineData("CONIN$", "_CONIN$")]
    [InlineData("CON .log", "_CON .log")]
    [InlineData("COM10", "COM10")]
    [InlineData("shot. ", "shot")]
    [InlineData("..", "scrcap")]
    public void FilenamesCannotAddressWindowsDevices(string input, string expected) =>
        Assert.Equal(expected, FilenameGenerator.SafeFilenameStem(input));

    [Theory]
    [InlineData("{\"schemaVersion\":0}")]
    [InlineData("{\"schemaVersion\":-1}")]
    [InlineData("{\"schemaVersion\":-2147483648}")]
    [InlineData("{\"schemaVersion\":8,\"hotkeys\":null}")]
    [InlineData("{\"schemaVersion\":8,\"paletteHex\":null}")]
    [InlineData("{\"schemaVersion\":8,\"afterCapture\":null}")]
    [InlineData("{\"schemaVersion\":8,\"filenamePattern\":null}")]
    public void InvalidSettingsRecoverWithoutCrashingOrOverwriting(string json)
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, json);
            var store = new SettingsStore(directory);
            Assert.Equal("opt+shift+1", store.Settings.Hotkeys["captureRegion"]);
            Assert.False(store.Save());
            Assert.Equal(json, File.ReadAllText(path));
            Assert.True(store.Update(settings => settings.TextSize = 20));
            Assert.True(store.Save());
            Assert.Equal(20, new SettingsStore(directory).Settings.TextSize);
        });
    }

    [Fact]
    public void CurrentShortcutOrderIsNotMistakenForLegacyDefaults()
    {
        WithDirectory(directory =>
        {
            var store = new SettingsStore(directory);
            Assert.True(store.Update(settings =>
            {
                settings.Hotkeys["captureWindow"] = "opt+shift+3";
                settings.Hotkeys["captureFullscreen"] = "opt+shift+2";
            }));
            Assert.Equal("opt+shift+3", store.Settings.Hotkeys["captureWindow"]);
            Assert.Equal("opt+shift+3", new SettingsStore(directory).Settings.Hotkeys["captureWindow"]);
        });
    }

    [Fact]
    public void DerivedSettingsAreNotSerialized()
    {
        var json = JsonSerializer.Serialize(Settings.Defaults(), SettingsStore.JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("keymap", out _));
        Assert.False(document.RootElement.TryGetProperty("resolvedExportScale", out _));
    }

    [Fact]
    public void NullHotkeyAndColorValuesAreHandled()
    {
        Assert.False(KeyChord.TryParse(null, out _));
        Assert.Null(Settings.NormalizeHexColor(null));
    }

    [Fact]
    public void InvalidAlignmentArgumentsAreRejected()
    {
        ulong[] rows = [1, 2, 3];
        Assert.Throws<ArgumentOutOfRangeException>(() => StitchEngine.Align(rows, rows, minOverlap: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => StitchEngine.Align(rows, rows, minOverlap: -1));
        foreach (var tolerance in new[] { double.NaN, double.PositiveInfinity, -0.1, 1.1 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => StitchEngine.Align(rows, rows, tolerance: tolerance));
        }
    }

    [Fact]
    public void AlignmentMatchesReferenceAtToleranceBoundaries()
    {
        var random = new Random(0);
        foreach (var count in Enumerable.Range(1, 70))
        {
            var previous = Enumerable.Range(0, count).Select(_ => (ulong)random.Next(4)).ToArray();
            var frame = Enumerable.Range(0, count).Select(_ => (ulong)random.Next(4)).ToArray();
            foreach (var tolerance in new[] { 0.0, 0.5, 0.85, 0.98, 1.0 })
            {
                Assert.Equal(ReferenceAlign(previous, frame, 1, tolerance),
                    StitchEngine.Align(previous, frame, 1, tolerance));
            }
        }
    }

    [Fact]
    public void AlignmentHasNoPerCandidateAllocations()
    {
        // A 1080-row no-overlap fixture visits every candidate. Fixture size is
        // not a runtime budget. Measure the old algorithm and the replacement.
        var previous = Enumerable.Range(0, 1080).Select(value => (ulong)value).ToArray();
        var frame = Enumerable.Range(5000, 1080).Select(value => (ulong)value).ToArray();
        _ = ReferenceAlign(previous, frame, 16, 0.98);
        _ = StitchEngine.Align(previous, frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var reference = ReferenceAlign(previous, frame, 16, 0.98);
        var oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        var actual = StitchEngine.Align(previous, frame);
        var newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(reference, actual);
        output.WriteLine($"1080-row no-overlap: baseline allocated {oldBytes} bytes; replacement allocated {newBytes} bytes.");
        Assert.Equal(0, newBytes);
        Assert.True(oldBytes > newBytes);
    }

    private static StitchEngine.Alignment? ReferenceAlign(IReadOnlyList<ulong> previous, IReadOnlyList<ulong> frame, int minOverlap, double tolerance)
    {
        static double Ratio(IReadOnlyList<ulong> left, IReadOnlyList<ulong> right)
        {
            var equal = 0;
            for (var index = 0; index < left.Count; index++)
            {
                if (left[index] == right[index]) { equal++; }
            }
            return left.Count == 0 ? 0 : (double)equal / left.Count;
        }
        if (previous.Count == 0 || frame.Count == 0) { return null; }
        if (frame.Count <= previous.Count && Ratio(previous.Skip(previous.Count - frame.Count).ToArray(), frame) >= tolerance)
        {
            return new(frame.Count);
        }
        for (var overlap = Math.Min(previous.Count, frame.Count); overlap >= minOverlap; overlap--)
        {
            if (Ratio(previous.Skip(previous.Count - overlap).Take(overlap).ToArray(), frame.Take(overlap).ToArray()) >= tolerance)
            {
                return new(overlap);
            }
        }
        return null;
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "scrcap-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
