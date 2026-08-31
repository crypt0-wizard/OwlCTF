using OwlCTF.Services;

namespace OwlCTF.Tests;

public sealed class ViewFormattingTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KiB")]
    [InlineData(1536, "1.5 KiB")]
    [InlineData(8146944, "7.8 MiB")]
    [InlineData(1073741824, "1 GiB")]
    public void FileSizesUseTheMostReadableUnit(long bytes, string expected) =>
        Assert.Equal(expected, FileSizeDisplay.Format(bytes));
}
