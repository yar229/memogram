using Memogram.Clients.Memos.Models;
using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace Memogram.Clients.Memos;

public class MemosClient
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;

    public MemosClient(string baseUrl, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
    }

    public MemosClient WithAuthentication(string accessToken)
    {
        var handler = new AuthenticatedHandler(accessToken);
        var client = new HttpClient(handler);
        return new MemosClient(_baseUrl, client);
    }

    private string Url(string path) => $"{_baseUrl}{path}";

    public async Task<InstanceProfile> GetInstanceProfileAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(Url("/api/v1/instance/profile"), ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<InstanceProfile>(cancellationToken: ct))!;
    }

    public async Task<User> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(Url("/api/v1/users/me"), ct);
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
            foreach (var tag in tags)
                sb.Append($"#{tag} ");
            sb.Append(content);
        }

        var body = new CreateMemoRequest
        {
            Content = doAddTags ? sb!.ToString() : content,
            Visibility = visibility,
        };
        var response = await _httpClient.PostAsJsonAsync(Url("/api/v1/memos"), body, ct);
        response.EnsureSuccessStatusCode();
        var memo = await response.Content.ReadFromJsonAsync<Memo>(cancellationToken: ct);
        return memo ?? throw new InvalidOperationException("No memo in response");
    }

    public async Task<Memo> GetMemoAsync(string name, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync(Url($"/api/v1/{name}"), ct);
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
        var response = await _httpClient.PatchAsJsonAsync(Url($"/api/v1/{memo.Name}"), body, ct);
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
        var response = await _httpClient.GetAsync(Url(url), ct);
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
        var response = await _httpClient.PostAsJsonAsync(Url("/api/v1/attachments"), body, ct);
        response.EnsureSuccessStatusCode();
        var res = await response.Content.ReadFromJsonAsync<CreateAttachmentResponse>(cancellationToken: ct);
        return res ?? throw new InvalidOperationException("No attachment in response");
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
}
