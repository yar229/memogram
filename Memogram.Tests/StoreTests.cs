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

    [Fact]
    public void SaveAndLoad_PersistsTokens()
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

            Assert.False(reloaded.TryGetUserAccessToken(99, out _));
        }
        finally
        {
            if (File.Exists(dataPath))
                File.Delete(dataPath);
        }
    }

    [Fact]
    public void SaveAndLoad_OverwritesExistingToken()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"memogram-test-{Guid.NewGuid():N}.txt");
        try
        {
            var store = new UserStoreService(dataPath);
            store.Init();
            store.SetUserAccessToken(1, "old-token");
            store.SetUserAccessToken(1, "new-token");

            var reloaded = new UserStoreService(dataPath);
            reloaded.Init();

            Assert.True(reloaded.TryGetUserAccessToken(1, out var token));
            Assert.Equal("new-token", token);
        }
        finally
        {
            if (File.Exists(dataPath))
                File.Delete(dataPath);
        }
    }

    [Fact]
    public void Init_CreatesFileIfNotExists()
    {
        var dataPath = Path.Combine(Path.GetTempPath(), $"memogram-test-{Guid.NewGuid():N}.txt");
        try
        {
            Assert.False(File.Exists(dataPath));
            var store = new UserStoreService(dataPath);
            store.Init();
            Assert.True(File.Exists(dataPath));
            Assert.Equal(0, new FileInfo(dataPath).Length);
        }
        finally
        {
            if (File.Exists(dataPath))
                File.Delete(dataPath);
        }
    }
}
