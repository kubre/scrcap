using Scrcap.Core;

namespace Scrcap.Core.Tests;

public sealed class FilenameGeneratorTests
{
    [Fact]
    public void ExpandsDateTimeTokensAndAppendsPng()
    {
        var now = new DateTimeOffset(2026, 6, 17, 9, 8, 7, TimeSpan.Zero);

        var filename = FilenameGenerator.Filename("shot-{date}-{time}", now);

        Assert.Equal("shot-2026-06-17-09.08.07.png", filename);
    }

    [Theory]
    [InlineData("a/b:c\\d", "a-b-c-d")]
    [InlineData("\n\t", "scrcap")]
    [InlineData("  okay name  ", "okay name")]
    public void SafeStemRemovesWindowsUnsafeCharacters(string input, string expected)
    {
        Assert.Equal(expected, FilenameGenerator.SafeFilenameStem(input));
    }
}
