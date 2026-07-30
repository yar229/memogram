using Memogram.Configs;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace Memogram.Services.UserStore;

public class UserStoreService
{
    public UserStoreService(LocalStorageConfig config, ILogger<UserStoreService> logger)
    {
        _dataPath = config.Filename;
        _logger = logger;
    }

    private readonly string _dataPath;
    private readonly ConcurrentDictionary<long, string> _userAccessTokenCache = new();
    private readonly ILogger<UserStoreService> _logger;

    public void Init()
    {
        LoadFromFile();
    }

    public bool TryGetUserAccessToken(long userId, out string? accessToken)
    {
        return _userAccessTokenCache.TryGetValue(userId, out accessToken);
    }

    public void SetUserAccessToken(long userId, string accessToken)
    {
        _userAccessTokenCache[userId] = accessToken;
        SaveToFile();
    }

    private void LoadFromFile()
    {
        if (!File.Exists(_dataPath))
        {
            File.WriteAllText(_dataPath, string.Empty);
            return;
        }

        foreach (var line in File.ReadLines(_dataPath))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            var (userId, token) = ParseLine(trimmed);
            if (userId != 0 && !string.IsNullOrEmpty(token))
            {
                _userAccessTokenCache[userId] = token;
            }
        }
    }

    private void SaveToFile()
    {
        var dir = Path.GetDirectoryName(_dataPath) ?? ".";
        var tmpFile = Path.Combine(dir, $"memogram-{Guid.NewGuid():N}.tmp");

        try
        {
            var entries = _userAccessTokenCache.OrderBy(kv => kv.Key);
            using (var writer = new StreamWriter(tmpFile))
            {
                foreach (var entry in entries)
                {
                    writer.WriteLine($"{entry.Key}:{entry.Value}");
                }
            }
            File.Move(tmpFile, _dataPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tmpFile))
                File.Delete(tmpFile);
            throw;
        }
    }

    internal static (long userId, string accessToken) ParseLine(string line)
    {
        var idx = line.IndexOf(':');
        if (idx < 0)
            return (0, string.Empty);

        var userIdStr = line[..idx];
        var accessToken = line[(idx + 1)..];

        if (long.TryParse(userIdStr, out var userId) && !string.IsNullOrEmpty(accessToken))
        {
            return (userId, accessToken);
        }
        return (0, string.Empty);
    }
}
