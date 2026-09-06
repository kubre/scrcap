using System.Collections.Concurrent;
using System.Text;
using Scrcap.Core;

namespace Scrcap.Core.Tests;

public sealed class ScreenshotFileWriterTests
{
    [Fact]
    public void ConcurrentCapturesNeverOverwriteEachOther()
    {
        WithDirectory(directory =>
        {
            var saved = new ConcurrentDictionary<string, byte[]>();
            Parallel.For(0, 16, index =>
            {
                var bytes = Encoding.UTF8.GetBytes($"screenshot-{index}");
                var path = ScreenshotFileWriter.WriteUnique(bytes, directory, "shot.png");
                Assert.True(saved.TryAdd(path, bytes));
            });
            Assert.Equal(16, saved.Count);
            foreach (var (path, expected) in saved) { Assert.Equal(expected, File.ReadAllBytes(path)); }
            Assert.Equal(16, Directory.GetFiles(directory).Length);
        });
    }

    [Fact]
    public void ExplicitReplacementIsCompleteAndEmptyPayloadPreservesOriginal()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "shot.png");
            File.WriteAllText(path, "original");
            Assert.Throws<ArgumentException>(() => ScreenshotFileWriter.WriteReplacing([], path));
            Assert.Equal("original", File.ReadAllText(path));
            ScreenshotFileWriter.WriteReplacing(Encoding.UTF8.GetBytes("replacement"), path);
            Assert.Equal("replacement", File.ReadAllText(path));
            Assert.Single(Directory.GetFiles(directory));
        });
    }

    [Fact]
    public void FailedPublicationCleansStagingFile()
    {
        WithDirectory(directory =>
        {
            var path = Path.Combine(directory, "occupied");
            Directory.CreateDirectory(path);
            Assert.ThrowsAny<IOException>(() => ScreenshotFileWriter.WriteReplacing([1, 2, 3], path));
            Assert.Empty(Directory.GetFiles(directory));
        });
    }

    [Fact]
    public void CounterNumbersSurviveCropGapsAndUndo()
    {
        var document = new AnnotationDocument(100, 100);
        document.AppendShape(new Shape(new ShapeKind.Counter(1), 0, ShapeSize.Small, new(10, 10), new(10, 10)));
        document.AppendShape(new Shape(new ShapeKind.Counter(2), 0, ShapeSize.Small, new(60, 60), new(60, 60)));
        Assert.True(document.Crop(new CoreRect(50, 50, 50, 50)));
        Assert.Single(document.Shapes);
        Assert.Equal(3, document.NextCounterNumber);
        document.Undo();
        Assert.Equal(3, document.NextCounterNumber);
        document.Undo();
        Assert.Equal(2, document.NextCounterNumber);
    }

    private static void WithDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "scrcap-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
