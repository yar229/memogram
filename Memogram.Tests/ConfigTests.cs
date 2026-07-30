using Memogram.Configs;
using Xunit;

namespace Memogram.Tests;

public class ConfigTests
{
    [Fact]
    public void MemogramConfig_Validate_ThrowsOnEmptyServerAddr()
    {
        var config = new MemogramConfig { ServerAddr = "", MediaCacheTtl = TimeSpan.FromSeconds(10) };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void MemogramConfig_Validate_ThrowsOnWhitespaceServerAddr()
    {
        var config = new MemogramConfig { ServerAddr = "   ", MediaCacheTtl = TimeSpan.FromSeconds(10) };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void MemogramConfig_Validate_PassesWithValidServerAddr()
    {
        var config = new MemogramConfig { ServerAddr = "http://localhost:5230", MediaCacheTtl = TimeSpan.FromSeconds(10) };
        config.Validate();
    }

    [Fact]
    public void TelegramConfig_Validate_ThrowsOnEmptyBotToken()
    {
        var config = new TelegramConfig { BotToken = "" };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void TelegramConfig_Validate_PassesWithValidBotToken()
    {
        var config = new TelegramConfig { BotToken = "123:abc" };
        config.Validate();
    }

    [Fact]
    public void LocalStorageConfig_Validate_SetsDefaultFilenameWhenEmpty()
    {
        var config = new LocalStorageConfig { Filename = "" };
        config.Validate();
        Assert.EndsWith("data.txt", config.Filename);
    }

    [Fact]
    public void LocalStorageConfig_Validate_SetsDefaultFilenameWhenWhitespace()
    {
        var config = new LocalStorageConfig { Filename = "   " };
        config.Validate();
        Assert.EndsWith("data.txt", config.Filename);
    }

    [Fact]
    public void LocalStorageConfig_Validate_CreatesFileIfNotExists()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"memogram-test-{Guid.NewGuid():N}.txt");
        try
        {
            Assert.False(File.Exists(tempFile));
            var config = new LocalStorageConfig { Filename = tempFile };
            config.Validate();
            Assert.True(File.Exists(tempFile));
            Assert.EndsWith(tempFile, config.Filename);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void LocalStorageConfig_Validate_ConvertsToFullPath()
    {
        var config = new LocalStorageConfig { Filename = "test.txt" };
        config.Validate();
        Assert.True(Path.IsPathRooted(config.Filename));
    }
}
