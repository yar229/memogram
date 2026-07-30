using Memogram.Clients.Memos;
using Memogram.Clients.Memos.Models;
using Memogram.Configs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.RegularExpressions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Memogram;

public partial class MemogramService : IDisposable
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
    private readonly MemosClient _memosClient;
    private readonly MemogramConfig _config;
    private readonly ILogger<MemogramService> _logger;

    private readonly MemoryCache _mediaGroupCache = new(new MemoryCacheOptions());
    private readonly TimeSpan _mediaGroupCacheTtl;

    private InstanceProfile? _instanceProfile;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public InstanceProfile? InstanceProfile => _instanceProfile;

    public MemogramService(MemosClient memosClient,  MemogramConfig config, ILogger<MemogramService> logger)
    {
        _memosClient = memosClient;
        _config = config;
        _logger = logger;

        _mediaGroupCacheTtl = _config.MediaCacheTtl > TimeSpan.Zero ? _config.MediaCacheTtl : TimeSpan.FromSeconds(300);
    }

    public void Dispose()
    {
        _mediaGroupCache.Dispose();
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_instanceProfile is not null) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_instanceProfile is not null) return;
            _instanceProfile = await GetInstanceProfileAsync(ct);
        }
        finally
        {
            _initLock.Release();
        }
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


    public async Task<List<Memo>> SearchMemoAsync(string searchString, string accessToken, string userName, string userUsername, CancellationToken ct)
    {
        var filter = BuildMemoSearchFilter(searchString, userName, userUsername);
        var memos = await ListMemosAsync(accessToken!, pageSize: 10, filter: filter, ct);
        return memos;
    }



    public Task<Clients.Memos.Models.User> GetCurrentUserAsync(string accessToken, CancellationToken ct = default)
    {
        return _memosClient.GetCurrentUserAsync(accessToken, ct);
    }

    public Task<Memo> GetMemoAsync(string accessToken, string name, CancellationToken ct = default)
    {
        return _memosClient.GetMemoAsync(accessToken, name, ct);
    }

    public Task<Memo> UpdateMemoAsync(string accessToken, Memo memo, CancellationToken ct = default)
    {
        return _memosClient.UpdateMemoAsync(accessToken, memo, ct);
    }

    public Task<List<Memo>> ListMemosAsync(string accessToken, int pageSize = 10, string? filter = null, CancellationToken ct = default)
    {
        return _memosClient.ListMemosAsync(accessToken, pageSize, filter, ct);
    }

    public async Task<Memo> HandleMemoCreation(string accessToken, string? mediaGroupId, string content, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(mediaGroupId))
        {
            if (_mediaGroupCache.TryGetValue(mediaGroupId, out var cached) && cached is Memo cachedMemo)
                return cachedMemo;

            var createdMemo = await _memosClient.CreateMemoAsync(accessToken, content, tags: _config.TagsToAdd, ct: ct);
            _mediaGroupCache.Set(mediaGroupId, createdMemo, _mediaGroupCacheTtl);
            return createdMemo;
        }

        return await _memosClient.CreateMemoAsync(accessToken, content, tags: _config.TagsToAdd, ct: ct);
    }

    public async Task ProcessFileMessage(string accessToken, FileInfo file, long chatId, string fileId, Memo memo, CancellationToken ct)
    {
        await _memosClient.CreateAttachmentAsync(
            accessToken,
            filename: Path.GetFileName(file.FilePath),
            contentType: file.ContentType,
            content: file.Content,
            memoName: memo.Name,
            ct: ct);
    }

    public Task<InstanceProfile> GetInstanceProfileAsync(CancellationToken ct) 
        => _memosClient.GetInstanceProfileAsync(ct);

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
            content = FormatContent(content, entities);

        if (message.ForwardOrigin is not null)
            content = PrependForwardedFrom(message.ForwardOrigin, content);

        if (message.ReplyToMessage != null)
            content = PrependReplyToMessage(message.ReplyToMessage, content);

        return content;
    }

    public static string ExtractMemoUidFromName(string name)
    {
        var parts = name.Split('/');
        if (parts.Length != 2 || parts[0] != "memos" || string.IsNullOrEmpty(parts[1]))
            throw new ArgumentException($"Invalid memo name: {name}");

        return parts[1];
    }

    public static string BuildMemoSearchFilter(string searchString, string userName, string userUsername)
    {
        var filter = $"content.contains(\"{searchString}\")";

        var creator = userName;
        if (string.IsNullOrEmpty(creator) && !string.IsNullOrEmpty(userUsername))
            creator = "users/" + userUsername;
        if (string.IsNullOrEmpty(creator))
            return filter;

        return $"{filter} && creator == \"{creator}\"";
    }

    public static string FormatContent(string content, MessageEntity[] entities)
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

    private string PrependForwardedFrom(MessageOrigin origin, string content) 
        => $"\n> {FormatUserstring(origin)}: {FormatContentAsQuote(content)}";

    private string PrependReplyToMessage(Message msg, string content) 
        => $"\n> {FormatUserstring(msg)}: {PrepareMessageContent(msg)} \n\n {content}";

    private string FormatUserstring(Message msg)
    {
        var originName = string.IsNullOrEmpty(msg.From?.LastName)
                ? msg.From?.FirstName
                : $"{msg.From.FirstName} {msg.From.LastName}";

        return FormatUserstring(originName!, msg.From?.Username, msg.From?.IsBot);
    }

    private static string FormatContentAsQuote(string content) 
        => content.Replace("\n", "\n> ");

    private string FormatUserstring(MessageOrigin msg)
    {
        string originName = string.Empty;
        string? originUsername = null;
        bool isBot = false;

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
                isBot = false;
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
        return FormatUserstring(originName, originUsername, isBot);
    }
    private string FormatUserstring(string name, string? username, bool? isBot = false)
    {
        string? ava = isBot ?? false 
            ? _config.Icons?.Bot 
            : _config.Icons?.Human;
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

    [GeneratedRegex(@"(?s)^(\s*)(.*)(\s*)$")]
    private static partial Regex EntityRegex();

    public class FileInfo
    {
        public required string FilePath { get; init; }
        public required byte[] Content { get; init; }
        public required string ContentType { get; init; }
    }
}
