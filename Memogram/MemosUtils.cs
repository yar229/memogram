using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram;

public static partial class MemosUtils
{
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

    public static string PrepareMessageContent(Message message)
    {
        var content = message.Text;
        var entities = message.Entities ?? Array.Empty<MessageEntity>();
        if (!string.IsNullOrEmpty(message.Caption))
        {
            content = message.Caption;
            entities = message.CaptionEntities ?? Array.Empty<MessageEntity>();
        }
        if (entities.Length > 0)
        {
            content = MemosUtils.FormatContent(content, entities);
        }

        if (message.ForwardOrigin is not null)
            content = MemosUtils.PrependForwardedFrom(message.ForwardOrigin, content);

        if (message.ReplyToMessage != null)
            content = MemosUtils.PrependReplyToMessage(message.ReplyToMessage, content);

        return content;
    }

    public static string ExtractMemoUidFromName(string name)
    {
        var parts = name.Split('/');
        if (parts.Length != 2 || parts[0] != "memos" || string.IsNullOrEmpty(parts[1]))
        {
            throw new ArgumentException($"Invalid memo name: {name}");
        }
        return parts[1];
    }

    public static string BuildMemoSearchFilter(string searchString, Clients.Memos.Models.User? user)
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

    private static string PrependForwardedFrom(MessageOrigin origin, string content)
    {
        return $"\n> {FormatUserstring(origin)}: {content}";
    }

    private static string PrependReplyToMessage(Message msg, string content)
    {
        return $"\n> {FormatUserstring(msg)}: {PrepareMessageContent(msg)} \n\n {content}";
    }

    private static string FormatUserstring(Message msg)
    {
        var originName = string.IsNullOrEmpty(msg.From?.LastName)
                ? msg.From?.FirstName
                : $"{msg.From.FirstName} {msg.From.LastName}";

        return FormatUserstring(originName, msg.From?.Username, msg.From?.IsBot);
    }

    private static string FormatUserstring(MessageOrigin msg)
    {
        string originName = string.Empty;
        string? originUsername = null;
        bool isBot;

        switch (msg)
        {
            case MessageOriginUser userOrigin:
                var user = userOrigin.SenderUser;
                originName = string.IsNullOrEmpty(user.LastName)
                    ? user.FirstName
                    : $"{user.FirstName} {user.LastName}";
                originUsername = user.Username;
                isBot = user.IsBot;
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
        return FormatUserstring(originName, originUsername);
    }
    private static string FormatUserstring(string name, string username, bool? isBot = false)
    {
        string ava = isBot ?? false ? "🤖" : "👤";
        if (!string.IsNullOrEmpty(username))
            return $"{ava}[{name}](https://t.me/{username})";
        return $"{ava}{name}";
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

    [GeneratedRegex(@"(?s)^(\s*)(.*?)(\s*)$")]
    private static partial Regex EntityRegex();
}
