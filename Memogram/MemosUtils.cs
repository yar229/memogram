using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram;

public static partial class MemosUtils
{
    public static string ExtractMemoUidFromName(string name)
    {
        var parts = name.Split('/');
        if (parts.Length != 2 || parts[0] != "memos" || string.IsNullOrEmpty(parts[1]))
        {
            throw new ArgumentException($"Invalid memo name: {name}");
        }
        return parts[1];
    }

    public static string PrependForwardedFrom(MessageOrigin origin, string content)
    {
        string originName = string.Empty;
        string? originUsername = null;

        switch (origin)
        {
            case MessageOriginUser userOrigin:
                var user = userOrigin.SenderUser;
                originName = string.IsNullOrEmpty(user.LastName)
                    ? user.FirstName
                    : $"{user.FirstName} {user.LastName}";
                originUsername = user.Username;
                break;
            case MessageOriginHiddenUser hiddenOrigin:
                originName = string.IsNullOrEmpty(hiddenOrigin.SenderUserName) ? "Hidden User" : hiddenOrigin.SenderUserName;
                break;
            case MessageOriginChat chatOrigin:
                originName = chatOrigin.SenderChat.Title ?? string.Empty;
                originUsername = chatOrigin.SenderChat.Username;
                break;
            case MessageOriginChannel channelOrigin:
                originName = channelOrigin.Chat.Title ?? string.Empty;
                originUsername = channelOrigin.Chat.Username;
                break;
        }

        if (!string.IsNullOrEmpty(originUsername))
        {
            return $"⏩[{originName}](https://t.me/{originUsername})\n>{content}";
        }
        return $"⏩{originName}\n>{content}";
    }

    internal static string FormatContent(string content, MessageEntity[] entities)
    {
        var sorted = entities.OrderBy(e => e.Offset).ThenBy(e => e.Length).ToList();

        var sb = new StringBuilder();
        int cursor = 0;

        foreach (var entity in sorted)
        {
            if (!IsSupportedEntity(entity.Type))
                continue;

            int start = entity.Offset;
            int end = entity.Offset + entity.Length;

            if (start < cursor)
                continue;
            if (start >= content.Length)
                break;
            if (end > content.Length)
                end = content.Length;

            sb.Append(content[cursor..start]);
            var segment = content[start..end];
            sb.Append(ApplyEntityFormatting(segment, entity));
            cursor = end;
        }

        sb.Append(content[cursor..]);
        return sb.ToString();
    }

    private static bool IsSupportedEntity(MessageEntityType? entityType)
        => entityType != null && EntityConverters.ContainsKey(entityType.Value);

    private static string ApplyEntityFormatting(string segment, MessageEntity entity)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return segment;

        var match = EntityRegex().Match(segment);
        if (!match.Success)
            return segment;

        var prefix = match.Groups[1].Value;
        var core = match.Groups[2].Value;
        var suffix = match.Groups[3].Value;

        return EntityConverters.TryGetValue(entity.Type, out var converter)
            ? converter(prefix, core, suffix, entity)
            : segment;
    }

    private static readonly Dictionary<MessageEntityType, Func<string, string, string, MessageEntity, string>> EntityConverters = new()
    {
        [MessageEntityType.Url] = (string p, string c, string s, MessageEntity entity) => $"{p}[{c}]({c}){s}",
        [MessageEntityType.TextLink] = (string p, string c, string s, MessageEntity entity) => $"{p}[{c}]({entity.Url}){s}",
        [MessageEntityType.Bold] = (string p, string c, string s, MessageEntity entity) => $"{p}**{c}**{s}",
        [MessageEntityType.Italic] = (string p, string c, string s, MessageEntity entity) => $"{p}*{c}*{s}",
        [MessageEntityType.Underline] = (string p, string c, string s, MessageEntity entity) => $"{p}<ins>{c}</ins>{s}",
        [MessageEntityType.Strikethrough] = (string p, string c, string s, MessageEntity entity) => $"{p}~~{c}~~{s}",
        [MessageEntityType.Spoiler] = (string p, string c, string s, MessageEntity entity) => $"{p}[{c}](#spoiler){s}",
        [MessageEntityType.DateTime] = (string p, string c, string s, MessageEntity entity) => $"{p}{c}({entity.UnixTime}){s}",
        [MessageEntityType.Blockquote] = (string p, string c, string s, MessageEntity entity) => $"{p}\n> {c}\n\n{s}",
        [MessageEntityType.Code] = (string p, string c, string s, MessageEntity entity) => $"{p}`{c}`{s}",
        [MessageEntityType.Pre] = (string p, string c, string s, MessageEntity entity) => $"{p}```\n{c}\n```{s}",
        [MessageEntityType.Mention] = (string p, string c, string s, MessageEntity entity) => $"{p}[{c}](https://t.me/{c[1..]}){s}",
    };

    internal static string BuildMemoSearchFilter(string searchString, Clients.Memos.Models.User? user)
    {
        var filter = $"content.contains(\"{searchString}\")";
        if (user is null)
            return filter;

        var creator = user.Name;
        if (string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(user.Username))
        {
            creator = "users/" + user.Username;
        }
        if (string.IsNullOrEmpty(creator))
            return filter;

        return $"{filter} && creator == \"{creator}\"";
    }

    [GeneratedRegex(@"(?s)^(\s*)(.*?)(\s*)$")]
    //[GeneratedRegex(@"\A(\s*)(.*?)(\s*)\Z")]
    //[GeneratedRegex(@"^(\s*)(.*?)(\s*)$")]
    private static partial Regex EntityRegex();
}
