using Memogram.Configs;
using Microsoft.Extensions.Configuration;
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
        var config = new TelegramConfig { BotToken = "", SearchReplyMessagesTrim = 200, DoReplyToMessage = false, Reactions = new TelegramConfig.ReactionsConfig()  };
        Assert.Throws<InvalidOperationException>(config.Validate);
    }

    [Fact]
    public void TelegramConfig_Validate_PassesWithValidBotToken()
    {
        var config = new TelegramConfig { BotToken = "123:abc", SearchReplyMessagesTrim = 200, DoReplyToMessage = false, Reactions = new TelegramConfig.ReactionsConfig() };
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

    [Fact]
    public void LocalStorageConfig_BindsFromFileKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LocalStorage:File"] = "custom.txt"
            })
            .Build();

        var config = configuration.GetSection(LocalStorageConfig.SectionName).Get<LocalStorageConfig>();

        Assert.NotNull(config);
        Assert.Equal("custom.txt", config!.Filename);
    }

    //[Fact]
    //public void HealthConfig_Validate_ThrowsOnInvalidPort()
    //{
    //    var config = new WebConfig { Port = 70000 };
    //    Assert.Throws<InvalidOperationException>(config.Validate);
    //}

    //[Fact]
    //public void HealthConfig_Validate_ThrowsOnPathWithoutLeadingSlash()
    //{
    //    var config = new WebConfig { Port = 8080, Path = "health" };
    //    Assert.Throws<InvalidOperationException>(config.Validate);
    //}

    //[Fact]
    //public void HealthConfig_Validate_ThrowsOnInvalidTimeout()
    //{
    //    var config = new WebConfig { Port = 8080, HealthCheckTimeoutSeconds = 0 };
    //    Assert.Throws<InvalidOperationException>(config.Validate);
    //}

    //[Fact]
    //public void HealthConfig_Validate_PassesWithValidValues()
    //{
    //    var config = new WebConfig { Port = 8080, Path = "/health", HealthCheckTimeoutSeconds = 10 };
    //    config.Validate();
    //}
}
