using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Scrcap.Windows.UI.Editor;

namespace Scrcap.Rendering.Tests;

public sealed class PixelateAuditTests
{
    [Fact]
    public void InPlacePixelationPreservesBlockSamplesAlphaAndPartialEdgeBlocks()
    {
        WpfTestHost.Run(() =>
        {
            const int width = 7;
            const int height = 5;
            var original = Enumerable.Range(0, width * height * 4).Select(index => (byte)index).ToArray();
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, original, width * 4);
            source.Freeze();
            var renderer = new PixelateRenderer();
            var bounds = new Int32Rect(1, 1, 5, 3);
            var result = renderer.Render(source, bounds, blockSize: 2, exportScale: 1);
            var pixels = new byte[5 * 3 * 4];
            result.CopyPixels(pixels, 5 * 4, 0);
            for (var y = 0; y < 3; y++)
            {
                for (var x = 0; x < 5; x++)
                {
                    var sourceOffset = ((1 + y / 2 * 2) * width + 1 + x / 2 * 2) * 4;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        Assert.Equal(original[sourceOffset + channel], pixels[(y * 5 + x) * 4 + channel]);
                    }
                }
            }
            Assert.True(result.IsFrozen);
            Assert.Same(result, renderer.Render(source, bounds, 2, 1));
            var unchanged = new byte[original.Length];
            source.CopyPixels(unchanged, width * 4, 0);
            Assert.Equal(original, unchanged);
        });
    }
}
