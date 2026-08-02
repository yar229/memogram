using Memogram.Configs;
using Microsoft.Extensions.Caching.Memory;

namespace Memogram.Services.Telegram;

public class MemoLinkCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(1);
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _ttl;

    public MemoLinkCache(IMemoryCache cache, TelegramConfig config)
    {
        _cache = cache;
        _ttl = config.CacheMessageForEditTime > TimeSpan.Zero ? config.CacheMessageForEditTime : DefaultTtl;
    }

    public void Record(long chatId, int messageId, string memoName)
    {
        _cache.Set(Key(chatId, messageId), memoName, _ttl);
    }

    public bool TryGetMemoName(long chatId, int messageId, out string? memoName)
    {
        return _cache.TryGetValue(Key(chatId, messageId), out memoName);
    }

    private static string Key(long chatId, int messageId) => $"memo-link:{chatId}:{messageId}";
}
