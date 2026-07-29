using Memogram.Clients.Memos;
using Memogram.Clients.Memos.Models;
using Memogram.Configs;
using Microsoft.Extensions.Logging;
using MimeDetective;
using System.Collections.Concurrent;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram;

public partial class MemogramService
{
    public class FileInfo
    {
        public string FilePath { get; set; }
        public byte[] Content { get; set; }
        public string ContentType { get; set; }
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
    private readonly MemosClient _memosClient;
    private readonly MemogramConfig _config;
    private readonly ILogger<MemogramService> _logger;

    private readonly object _mediaGroupMutex = new();
    private readonly ConcurrentDictionary<string, Memo> _mediaGroupCache = new();


    public InstanceProfile? InstanceProfile { get; set; }
    

    public MemogramService(MemosClient memosClient,  MemogramConfig config, ILogger<MemogramService> logger)
    {
        _memosClient = memosClient;
        _config = config;
        _logger = logger;

        //var baseUrl = _config.ServerAddr;
        //baseUrl = baseUrl.Replace("dns:", "", StringComparison.Ordinal);
        //if (!baseUrl.StartsWith("http://", StringComparison.Ordinal) && !baseUrl.StartsWith("https://", StringComparison.Ordinal))
        //{
        //    baseUrl = "http://" + baseUrl;
        //}
        //_memosClient = new MemosClient(baseUrl, logger: loggerFactory.CreateLogger<MemosClient>());

        InstanceProfile = GetInstanceProfileAsync(CancellationToken.None).Result; //TODO:!!!!
    }


    public string BaseUrl
    {
        get
        {
            var baseUrl = _config.ServerAddr;
            if (InstanceProfile?.InstanceUrl is { Length: > 0 })
                baseUrl = InstanceProfile.InstanceUrl;
            return baseUrl;
        }
    }


    public Task<Clients.Memos.Models.User> GetCurrentUserAsync(string accessToken, CancellationToken ct = default)
    {
        var memoClient = _memosClient.WithAuthentication(accessToken);
        return memoClient.GetCurrentUserAsync(ct);
    }

    public Task<Memo> GetMemoAsync(string accessToken, string name, CancellationToken ct = default)
    {
        var memoClient = _memosClient.WithAuthentication(accessToken);
        return memoClient.GetMemoAsync(name, ct);
    }

    public Task<Memo> UpdateMemoAsync(string accessToken, Memo memo, CancellationToken ct = default)
    {
        var memoClient = _memosClient.WithAuthentication(accessToken);
        return memoClient.UpdateMemoAsync(memo, ct);
    }

    public Task<List<Memo>> ListMemosAsync(string accessToken, int pageSize = 10, string? filter = null, CancellationToken ct = default)
    {
        var memoClient = _memosClient.WithAuthentication(accessToken);
        return memoClient.ListMemosAsync(pageSize, filter, ct);
    }

    public async Task<Memo> HandleMemoCreation(string accessToken, string? mediaGroupId, string content, CancellationToken ct)
    {
        var memoClient = _memosClient.WithAuthentication(accessToken!);

        if (!string.IsNullOrEmpty(mediaGroupId))
        {
            lock (_mediaGroupMutex)
            {
                if (_mediaGroupCache.TryGetValue(mediaGroupId, out var cached))
                {
                    return cached;
                }
            }

            var memo = await memoClient.CreateMemoAsync(content, tags: _config.TagsToAdd, ct: ct);
            _mediaGroupCache[mediaGroupId] = memo;
            return memo;
        }

        return await memoClient.CreateMemoAsync(content, tags: _config.TagsToAdd, ct: ct);
    }

    public async Task ProcessFileMessage(string accessToken, FileInfo file, long chatId, string fileId, Memo memo, CancellationToken ct)
    {
        var memosClient = _memosClient.WithAuthentication(accessToken!);
        //try
        {
            //var file = await _tgService.GetFile(bot, fileId, ct);

            //if (string.IsNullOrEmpty(file.ContentType) || MediaTypeNames.Application.Octet.Equals(file.ContentType, StringComparison.OrdinalIgnoreCase))
            //{
            //    var bestMatch = _contentInspector.Inspect(file.Content).ByMimeType().FirstOrDefault();
            //    if (null != bestMatch && !string.IsNullOrEmpty(bestMatch.MimeType))
            //        file.ContentType = bestMatch.MimeType;
            //}

            await memosClient.CreateAttachmentAsync(
                filename: Path.GetFileName(file.FilePath),
                contentType: file.ContentType,
                content: file.Content,
                memoName: memo.Name,
                ct: ct
            );
        }
        //catch (Exception ex)
        //{
        //    await _tgService.SendError(chatId, new InvalidOperationException($"Failed to save attachment: {ex.Message}"), ct);
        //}
    }

    public Task<InstanceProfile> GetInstanceProfileAsync(CancellationToken ct)
    {
        return _memosClient.GetInstanceProfileAsync(ct);
    }
    public string PrepareMessageContent(Message message)
    {
        var content = message.Text ?? string.Empty;
        var entities = message.Entities ?? Array.Empty<MessageEntity>();
        if (!string.IsNullOrEmpty(message.Caption))
        {
            content = message.Caption;
            entities = message.CaptionEntities ?? Array.Empty<MessageEntity>();
        }
        if (entities.Length > 0)
        {
            content = FormatContent(content, entities);
        }

        if (message.ForwardOrigin is not null)
            content = PrependForwardedFrom(message.ForwardOrigin, content);

        if (message.ReplyToMessage != null)
            content = PrependReplyToMessage(message.ReplyToMessage, content);

        return content;
    }

    public string ExtractMemoUidFromName(string name)
    {
        var parts = name.Split('/');
        if (parts.Length != 2 || parts[0] != "memos" || string.IsNullOrEmpty(parts[1]))
        {
            throw new ArgumentException($"Invalid memo name: {name}");
        }
        return parts[1];
    }

    public string BuildMemoSearchFilter(string searchString, Clients.Memos.Models.User? user)
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

    internal string FormatContent(string content, MessageEntity[] entities)
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

    private  string PrependForwardedFrom(MessageOrigin origin, string content)
    {
        return $"\n> {FormatUserstring(origin)}: {FormatContentAsQuote(content)}";
    }

    private string PrependReplyToMessage(Message msg, string content)
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

    private static string FormatContentAsQuote(string content)
    {
        return content.Replace("\n", "\n> ");

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
