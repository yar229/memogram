using Memogram.Clients.Memos.Models;
using Memogram.Configs;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Memogram.Clients.Memos;

public class MemosClient
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly ResiliencePipeline<HttpResponseMessage> _retryPipeline;
    private readonly ILogger<MemosClient> _logger;

    private const int MaxRetryAttempts = 3;

    public MemosClient(string baseUrl,
        HttpClient? httpClient,
        ILogger<MemosClient> logger)
    {
        
        baseUrl = baseUrl.Replace("dns:", "", StringComparison.Ordinal);
        if (!baseUrl.StartsWith("http://", StringComparison.Ordinal) && !baseUrl.StartsWith("https://", StringComparison.Ordinal))
        {
            baseUrl = "http://" + baseUrl;
        }
        _baseUrl = baseUrl.TrimEnd('/');

        _httpClient = httpClient ?? new HttpClient();
        _logger = logger;

        _retryPipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = MaxRetryAttempts,
                Delay = TimeSpan.FromSeconds(1),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => r.StatusCode is
                        HttpStatusCode.RequestTimeout or
                        HttpStatusCode.TooManyRequests or
                        >= HttpStatusCode.InternalServerError),
                OnRetry = args =>
                {
                    var reason = args.Outcome.Exception is { } ex
                        ? ex.Message
                        : args.Outcome.Result is { } res
                            ? $"HTTP {(int)res.StatusCode}"
                            : "Unknown";

                    _logger?.LogWarning("Request failed ({Reason}). Retry {Attempt}/{MaxRetries} in {Delay:F1}s...",
                        reason, args.AttemptNumber + 1, MaxRetryAttempts, args.RetryDelay.TotalSeconds);

                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public MemosClient(MemogramConfig config, ILogger<MemosClient> logger) 
        :this(config.ServerAddr, new HttpClient(), logger)
    {
    }

    public MemosClient WithAuthentication(string accessToken)
    {
        var handler = new AuthenticatedHandler(accessToken);
        var client = new HttpClient(handler);
        return new MemosClient(_baseUrl, client, _logger);
    }

    public async Task<InstanceProfile> GetInstanceProfileAsync(CancellationToken ct = default)
    {
        var response = await RetryAsync(ct2 => _httpClient.GetAsync(Url("/api/v1/instance/profile"), ct2), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InstanceProfile>(cancellationToken: ct))!;
    }

    public async Task<User> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var response = await RetryAsync(ct2 => _httpClient.GetAsync(Url("/api/v1/auth/me"), ct2), ct);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<UserWrapper>(cancellationToken: ct);
        return wrapper?.User ?? throw new InvalidOperationException("No user in response");
    }

    public async Task<Memo> CreateMemoAsync(string content, string visibility = "PRIVATE", IEnumerable<string>? tags = null, CancellationToken ct = default)
    {
        bool doAddTags = tags?.Any() ?? false;
        StringBuilder? sb = null;
        if (doAddTags)
        {
            sb = new StringBuilder(content.Length + 10);
            foreach (var tag in tags!)
                sb.Append($"#{tag} ");
            sb.Append(content);
        }

        var body = new CreateMemoRequest
        {
            Content = doAddTags ? sb!.ToString() : content,
            Visibility = visibility,
        };
        var response = await RetryAsync(ct2 => _httpClient.PostAsJsonAsync(Url("/api/v1/memos"), body, ct2), ct);
        response.EnsureSuccessStatusCode();
        var memo = await response.Content.ReadFromJsonAsync<Memo>(cancellationToken: ct);
        return memo ?? throw new InvalidOperationException("No memo in response");
    }

    public async Task<Memo> GetMemoAsync(string name, CancellationToken ct = default)
    {
        var response = await RetryAsync(ct2 => _httpClient.GetAsync(Url($"/api/v1/{name}"), ct2), ct);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<Memo>(cancellationToken: ct);
        return wrapper ?? throw new InvalidOperationException("No memo in response");
    }

    public async Task<Memo> UpdateMemoAsync(Memo memo, CancellationToken ct = default)
    {
        var body = new UpdateMemoRequest
        {
            Content = memo.Content,
            Visibility = memo.Visibility,
            Pinned = memo.Pinned,
        };
        var response = await RetryAsync(ct2 => _httpClient.PatchAsJsonAsync(Url($"/api/v1/{memo.Name}"), body, ct2), ct);
        response.EnsureSuccessStatusCode();
        var wrapper = await response.Content.ReadFromJsonAsync<Memo>(cancellationToken: ct);
        return wrapper ?? throw new InvalidOperationException("No memo in response");
    }

    public async Task<List<Memo>> ListMemosAsync(int pageSize = 10, string? filter = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/memos?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(filter))
        {
            url += $"&filter={Uri.EscapeDataString(filter)}";
        }
        var response = await RetryAsync(ct2 => _httpClient.GetAsync(Url(url), ct2), ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ListMemosResponse>(cancellationToken: ct);
        return result?.Memos ?? new List<Memo>();
    }

    public async Task<CreateAttachmentResponse> CreateAttachmentAsync(string filename, string contentType, byte[] content, string? memoName = null, CancellationToken ct = default)
    {
        var body = new CreateAttachmentRequest
        {
            Filename = filename,
            Memo = memoName,
            Type = contentType,
            Content = content
        };
        var response = await RetryAsync(ct2 => _httpClient.PostAsJsonAsync(Url("/api/v1/attachments"), body, ct2), ct);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<CreateAttachmentResponse>(cancellationToken: ct);
        return res ?? throw new InvalidOperationException("No attachment in response");
    }

    private async Task<HttpResponseMessage> RetryAsync(Func<CancellationToken, Task<HttpResponseMessage>> request, CancellationToken ct)
    {
        return await _retryPipeline.ExecuteAsync(
            ct2 => new ValueTask<HttpResponseMessage>(request(ct2)), ct);
    }

    private class AuthenticatedHandler : DelegatingHandler
    {
        private readonly string _token;

        public AuthenticatedHandler(string token) : base(new HttpClientHandler())
        {
            _token = token;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private string Url(string path)
        => $"{_baseUrl}{path}";
}
