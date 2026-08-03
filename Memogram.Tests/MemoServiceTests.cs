using Memogram.Services.Memos;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Xunit;

namespace Memogram.Tests;

public class MemoServiceTests
{
    [Theory]
    [InlineData("memos/abc123", "abc123")]
    [InlineData("memos/some-uuid", "some-uuid")]
    [InlineData("memos/42", "42")]
    public void ExtractMemoUidFromName_ValidName_ReturnsUid(string name, string expected)
    {
        Assert.Equal(expected, MemogramService.ExtractMemoUidFromName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("memos/")]
    [InlineData("/abc")]
    [InlineData("users/abc")]
    public void ExtractMemoUidFromName_InvalidName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => MemogramService.ExtractMemoUidFromName(name));
    }

    [Fact]
    public void BuildMemoSearchFilter_WithUserName_UsesUserName()
    {
        var result = MemogramService.BuildMemoSearchFilter("needle", "users/alice", "alice");
        Assert.Equal("content.contains(\"needle\") && creator == \"users/alice\"", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_EmptyUserName_FallsBackToUserUsername()
    {
        var result = MemogramService.BuildMemoSearchFilter("needle", "", "alice");
        Assert.Equal("content.contains(\"needle\") && creator == \"users/alice\"", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_EmptyUserNameAndUsername_ReturnsContentOnly()
    {
        var result = MemogramService.BuildMemoSearchFilter("needle", "", "");
        Assert.Equal("content.contains(\"needle\")", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_EscapesSpecialChars()
    {
        var result = MemogramService.BuildMemoSearchFilter("hello \"world\"", "users/me", "me");
        Assert.Equal("content.contains(\"hello \\\"world\\\"\") && creator == \"users/me\"", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_EscapesBackslash()
    {
        var result = MemogramService.BuildMemoSearchFilter("a\\b", "users/me", "me");
        Assert.Equal("content.contains(\"a\\\\b\") && creator == \"users/me\"", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_EscapesControlChars()
    {
        var result = MemogramService.BuildMemoSearchFilter("line1\nline2\t\"x\"", "users/me", "me");
        Assert.Equal("content.contains(\"line1\\nline2\\t\\\"x\\\"\") && creator == \"users/me\"", result);
    }

    [Fact]
    public void BuildMemoSearchFilter_NullUserName_UsesUserUsername()
    {
        var result = MemogramService.BuildMemoSearchFilter("test", null!, "bob");
        Assert.Equal("content.contains(\"test\") && creator == \"users/bob\"", result);
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

        var got = MemogramService.FormatContent(content, entities);
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

        var got = MemogramService.FormatContent(content, entities);
        var want = "*Italic* and **bold**";
        Assert.Equal(want, got);
    }

    [Fact]
    public void FormatContent_OverlappingEntities_SkipsOverlap()
    {
        var content = "Overlap test";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Bold, Offset = 0, Length = 7 },
            new MessageEntity { Type = MessageEntityType.Italic, Offset = 5, Length = 4 },
        };

        var got = MemogramService.FormatContent(content, entities);
        var want = "**Overlap** test";
        Assert.Equal(want, got);
    }

    [Fact]
    public void FormatContent_EmptyEntities_ReturnsContent()
    {
        var content = "plain text";
        var got = MemogramService.FormatContent(content, []);
        Assert.Equal(content, got);
    }

    [Fact]
    public void FormatContent_EntityOutOfBounds_SkipsEntity()
    {
        var content = "short";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Bold, Offset = 10, Length = 5 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal(content, got);
    }

    [Fact]
    public void FormatContent_UnsupportedEntityType_Ignores()
    {
        var content = "hello there";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.CustomEmoji, Offset = 0, Length = 5 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal(content, got);
    }

    [Fact]
    public void FormatContent_SpoilerEntity_FormatsCorrectly()
    {
        var content = "Contains a spoiler";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Spoiler, Offset = 9, Length = 7 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("Contains [a spoil](#spoiler)er", got);
    }

    [Fact]
    public void FormatContent_CodeEntity_FormatsCorrectly()
    {
        var content = "use code inline";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Code, Offset = 4, Length = 4 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("use `code` inline", got);
    }

    [Fact]
    public void FormatContent_PreEntity_FormatsCorrectly()
    {
        var content = "block:\ncode block\nend";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Pre, Offset = 7, Length = 10 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("block:\n```\ncode block\n```\nend", got);
    }

    [Fact]
    public void FormatContent_BlockquoteEntity_FormatsCorrectly()
    {
        var content = "before\nquoted text\nafter";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Blockquote, Offset = 7, Length = 11 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("before\n\n> quoted text\n\n\nafter", got);
    }

    [Fact]
    public void FormatContent_UnderlineEntity_FormatsCorrectly()
    {
        var content = "some underlined text";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Underline, Offset = 5, Length = 10 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("some <ins>underlined</ins> text", got);
    }

    [Fact]
    public void FormatContent_StrikethroughEntity_FormatsCorrectly()
    {
        var content = "strike this";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Strikethrough, Offset = 0, Length = 11 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("~~strike this~~", got);
    }

    [Fact]
    public void FormatContent_MentionEntity_FormatsCorrectly()
    {
        var content = "hello @user";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Mention, Offset = 6, Length = 5 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("hello [@user](https://t.me/user)", got);
    }

    [Fact]
    public void FormatContent_WhitespacePreserved()
    {
        var content = "  bold text  ";
        var entities = new[]
        {
            new MessageEntity { Type = MessageEntityType.Bold, Offset = 2, Length = 4 },
        };

        var got = MemogramService.FormatContent(content, entities);
        Assert.Equal("  **bold** text  ", got);
    }
}
