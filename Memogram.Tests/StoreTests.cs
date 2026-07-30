using Memogram.Services.UserStore;
using Xunit;

namespace Memogram.Tests;

public class StoreTests
{
    [Fact]
    public void ParseLine_ValidLine_ReturnsUserAndToken()
    {
        var (userId, token) = UserStoreService.ParseLine("123:abc:def");
        Assert.Equal(123, userId);
        Assert.Equal("abc:def", token);
    }

    [Fact]
    public void ParseLine_NoColon_ReturnsEmpty()
    {
        var (userId, token) = UserStoreService.ParseLine("invalidline");
        Assert.Equal(0, userId);
        Assert.Equal(string.Empty, token);
    }

    [Fact]
    public void ParseLine_NonNumericUserId_ReturnsEmpty()
    {
        var (userId, token) = UserStoreService.ParseLine("abc:token");
        Assert.Equal(0, userId);
        Assert.Equal(string.Empty, token);
    }

    [Fact]
    public void ParseLine_EmptyToken_ReturnsEmpty()
    {
        var (userId, token) = UserStoreService.ParseLine("42:");
        Assert.Equal(0, userId);
        Assert.Equal(string.Empty, token);
    }

    [Fact]
    public void ParseLine_EmptyLine_ReturnsEmpty()
    {
        var (userId, token) = UserStoreService.ParseLine("");
        Assert.Equal(0, userId);
        Assert.Equal(string.Empty, token);
    }

    [Fact]
    public void ParseLine_OnlyColon_ReturnsEmpty()
    {
        var (userId, token) = UserStoreService.ParseLine(":");
        Assert.Equal(0, userId);
        Assert.Equal(string.Empty, token);
    }
}
