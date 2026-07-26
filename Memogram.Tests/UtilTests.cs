using Memogram;
using Xunit;

namespace Memogram.Tests;

public class UtilTests
{
    [Theory]
    [InlineData("memos/abc123", "abc123")]
    [InlineData("memos/some-uuid", "some-uuid")]
    public void ExtractMemoUidFromName_ValidName_ReturnsUid(string name, string expected)
    {
        Assert.Equal(expected, Util.ExtractMemoUidFromName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("memos/")]
    [InlineData("/abc")]
    public void ExtractMemoUidFromName_InvalidName_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() => Util.ExtractMemoUidFromName(name));
    }
}
