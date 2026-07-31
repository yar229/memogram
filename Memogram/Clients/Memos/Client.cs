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

    public async Task<InstanceProfile> GetInstanceProfileAsync(CancellationToken ct = default)
    {
        var response = await RetryAsync(ct2 => _httpClient.GetAsync(Url("/api/v1/instance/profile"), ct2), ct);
        await ThrowIfNotSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<InstanceProfile>(cancellationToken: ct))!;
    }

    public async Task<User> GetCurrentUserAsync(string accessToken, CancellationToken ct = default)
    {
        var response = await RetryAsync(ct2 => SendWithAuthAsync(HttpMethod.Get, Url("/api/v1/auth/me"), accessToken, ct2), ct);
        await ThrowIfNotSuccessAsync(response, ct);
        var wrapper = await response.Content.ReadFromJsonAsync<UserWrapper>(cancellationToken: ct);
        return wrapper?.User ?? throw new InvalidOperationException("No user in response");
    }

    public async Task<Memo> CreateMemoAsync(string accessToken, string content, string visibility = "PRIVATE", IEnumerable<string>? tags = null, CancellationToken ct = default)
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
        var response = await RetryAsync(ct2 => SendWithAuthAsync(HttpMethod.Post, Url("/api/v1/memos"), accessToken, body, ct2), ct);
        await ThrowIfNotSuccessAsync(response, ct);
        var memo = await response.Content.ReadFromJsonAsync<Memo>(cancellationToken: ct);
        return memo ?? throw new InvalidOperationException("No memo in response");
    }

    public async Task<Memo> GetMemoAsync(string accessToken, string name, CancellationToken ct = default)
    {
        var response = await RetryAsync(ct2 => SendWithAuthAsync(HttpMethod.Get, Url($"/api/v1/{name}"), accessToken, ct2), ct);
        await ThrowIfNotSuccessAsync(response, ct);
        var wrapper = await response.Content.ReadFromJsonAsync<Memo>(cancellationToken: ct);
        return wrapper ?? throw new InvalidOperationException("No memo in response");
    }

    public async Task<Memo> UpdateMemoAsync(string accessToken, Memo memo, CancellationToken ct = default)
    {
        var body = new UpdateMemoRequest
        {
            Content = memo.Content,
            Visibility = memo.Visibility,
            Pinned = memo.Pinned,
        };
        var response = await RetryAsync(ct2 => SendWithAuthAsync(HttpMethod.Patch, Url($"/api/v1/{memo.Name}"), accessToken, body, ct2), ct);
        await ThrowIfNotSuccessAsync(response, ct);
        var wrapper = await response.Content.ReadFromJsonAsync<Memo>(cancellationToken: ct);
        return wrapper ?? throw new InvalidOperationException("No memo in response");
    }

    public async Task<List<Memo>> ListMemosAsync(string accessToken, int pageSize = 10, string? filter = null, CancellationToken ct = default)
    {
        var url = $"/api/v1/memos?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(filter))
        {
            url += $"&filter={Uri.EscapeDataString(filter)}";
        }
        var response = await RetryAsync(ct2 => SendWithAuthAsync(HttpMethod.Get, Url(url), accessToken, ct2), ct);
        await ThrowIfNotSuccessAsync(response, ct);
        var result = await response.Content.ReadFromJsonAsync<ListMemosResponse>(cancellationToken: ct);
        return result?.Memos ?? new List<Memo>();
    }

    public async Task<CreateAttachmentResponse> CreateAttachmentAsync(string accessToken, string filename, string contentType, Stream content, string? memoName = null, CancellationToken ct = default)
    {
        var body = new CreateAttachmentRequest
        {
            Filename = filename,
            Memo = memoName,
            Type = contentType,
            Content = content
        };
        var response = await RetryAsync(ct2 => SendWithAuthContentAsync(HttpMethod.Post, Url("/api/v1/attachments"), accessToken, new AttachmentJsonContent(body), ct2), ct);
        await ThrowIfNotSuccessAsync(response, ct);
        var res = await response.Content.ReadFromJsonAsync<CreateAttachmentResponse>(cancellationToken: ct);
        return res ?? throw new InvalidOperationException("No attachment in response");
    }

    private static async Task ThrowIfNotSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            // Best effort: fall back to the status line only.
        }

        var message = string.IsNullOrEmpty(body)
            ? $"Request failed with status {(int)response.StatusCode} ({response.ReasonPhrase})."
            : $"Request failed with status {(int)response.StatusCode} ({response.ReasonPhrase}). Response: {body}";

        throw new HttpRequestException(message, inner: null, response.StatusCode);
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(HttpMethod method, string url, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await _httpClient.SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendWithAuthContentAsync(HttpMethod method, string url, string accessToken, HttpContent content, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = content;
        return await _httpClient.SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync<T>(HttpMethod method, string url, string accessToken, T body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Content = JsonContent.Create(body);
        return await _httpClient.SendAsync(request, ct);
    }

    private async Task<HttpResponseMessage> RetryAsync(Func<CancellationToken, Task<HttpResponseMessage>> request, CancellationToken ct)
    {
        return await _retryPipeline.ExecuteAsync(
            ct2 => new ValueTask<HttpResponseMessage>(request(ct2)), ct);
    }

    private string Url(string path)
        => $"{_baseUrl}{path}";
}
