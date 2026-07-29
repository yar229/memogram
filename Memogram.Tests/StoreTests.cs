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
    public void SaveAndLoadUserAccessTokens()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"memogram-test-{Guid.NewGuid():N}.txt");
        try
        {
            var store = new UserStoreService(dataPath);
            store.Init();

            store.SetUserAccessToken(42, "token-one");
            store.SetUserAccessToken(7, "token:two");

            var reloaded = new UserStoreService(dataPath);
            reloaded.Init();

            Assert.True(reloaded.TryGetUserAccessToken(42, out var token1));
            Assert.Equal("token-one", token1);

            Assert.True(reloaded.TryGetUserAccessToken(7, out var token2));
            Assert.Equal("token:two", token2);
        }
        finally
        {
            if (File.Exists(dataPath))
                File.Delete(dataPath);
        }
    }
}
