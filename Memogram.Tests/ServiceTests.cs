using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Memogram.Tests;

public class ServiceTests
{
    [Fact]
    public void BuildMemoSearchFilter_WithUser_UsesResourceName()
    {
        var result = MemosUtils.BuildMemoSearchFilter("needle", new Clients.Memos.Models.User { Name = "users/alice", Username = "alice" });
        Assert.Equal("content.contains(\"needle\") && creator == \"users/alice\"", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_FallsBackToUsername()
    {
        var result = MemosUtils.BuildMemoSearchFilter("needle", new Clients.Memos.Models.User { Username = "alice" });
        Assert.Equal("content.contains(\"needle\") && creator == \"users/alice\"", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_AllowsNullUser()
    {
        var result = MemosUtils.BuildMemoSearchFilter("needle", null);
        Assert.Equal("content.contains(\"needle\")", result);
    }

    [Fact]
    public void FormatContent_MixedEntities_FormatsCorrectly()
    {
        var content = "See example.com and bold text link";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Url, Offset = 4, Length = 11 },
            new MessageEntity { Type = MessageEntityType.Bold, Offset = 20, Length = 4 },
            new MessageEntity { Type = MessageEntityType.TextLink, Offset = 30, Length = 4, Url = "https://example.com" },
        };

        var got = MemosUtils.FormatContent(content, entities);
        var want = "See [example.com](example.com) and **bold** text [link](https://example.com)";
        Assert.Equal(want, got);
    }

    [Fact]
    public void FormatContent_OutOfOrderEntities_FormatsCorrectly()
    {
        var content = "Italic and bold";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Bold, Offset = 11, Length = 4 },
            new MessageEntity { Type = MessageEntityType.Italic, Offset = 0, Length = 6 },
        };

        var got = MemosUtils.FormatContent(content, entities);
        var want = "*Italic* and **bold**";
        Assert.Equal(want, got);
    }

    [Fact]
    public void FormatContent_OverlappingEntities_FormatsCorrectly()
    {
        var content = "Overlap test";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Bold, Offset = 0, Length = 7 },
            new MessageEntity { Type = MessageEntityType.Italic, Offset = 5, Length = 4 },
        };

        var got = MemosUtils.FormatContent(content, entities);
        var want = "**Overlap** test";
        Assert.Equal(want, got);
    }
}
