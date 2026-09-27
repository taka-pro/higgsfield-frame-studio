using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HiggsfieldStudio.Core;

public sealed class ApiException(int statusCode) : Exception($"API HTTP {statusCode}。401/403はキー・権限、402は残高、422は入力条件、429は混雑を確認してください。")
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ApiClient : IDisposable
{
    public const string Base = "https://api.higgsfield.ai/";
    private readonly HttpClient http;
    private readonly string credentials;
    // Memory only: a new client/session never inherits another session's uploads.
    private readonly Dictionary<string, string> uploadedImages = new();
    public event Action<string>? Trace;
    public ApiClient(string keyId, string secret, HttpMessageHandler? handler = null)
    {
        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(secret) || keyId.Contains(':') || keyId.Any(char.IsWhiteSpace) || secret.Any(char.IsWhiteSpace))
            throw new ArgumentException("APIキーのIDとSecretを設定してください。");
        credentials = $"{keyId}:{secret}";
        // No default authentication header: storage/CDN requests must never receive the API key.
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(3) };
    }
    public static Uri RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("HTTPS URLが必要です。");
        return uri;
    }
    public static Uri RequireApi(string url)
    {
        var uri = RequireHttps(url);
        if (uri.Host != "api.higgsfield.ai" || !uri.IsDefaultPort) throw new InvalidDataException("API応答の接続先が正しくありません。");
        return uri;
    }
    public static Uri RequireStatus(string url)
    {
        var uri = RequireHttps(url);
        if (uri.Host == "api.higgsfield.ai") return RequireApi(url);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host != "platform.higgsfield.ai" || !uri.IsDefaultPort || segments.Length != 3
            || segments[0] != "requests" || !Guid.TryParseExact(segments[1], "D", out _) || segments[2] != "status"
            || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("API応答の状態確認先が正しくありません。");
        return uri;
    }
    public static string SafeJson(JsonObject obj)
    {
        var copy = obj.DeepClone();
        Scrub(copy);
        return copy.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
    private static void Scrub(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(x => x.Key).ToArray())
            {
                if (key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase) || key.Contains("authorization", StringComparison.OrdinalIgnoreCase) || key.Contains("key", StringComparison.OrdinalIgnoreCase))
                    obj[key] = "[非表示]";
                else if (key.EndsWith("url", StringComparison.OrdinalIgnoreCase) || key.EndsWith("urls", StringComparison.OrdinalIgnoreCase)) obj[key] = "[メディア／接続URLを非表示]";
                else Scrub(obj[key]);
            }
        }
        else if (node is JsonArray array) foreach (var child in array) Scrub(child);
    }
    private void Log(string text)
    {
        // Additional redaction in case an upstream field happens to echo credentials.
        foreach (var s in credentials.Split(':')) if (!string.IsNullOrEmpty(s)) text = text.Replace(s, "[非表示]");
        Trace?.Invoke(text);
    }
    private async Task<JsonObject> Send(HttpMethod method, string url, JsonObject? body, CancellationToken ct, bool statusRequest = false)
    {
        using var request = new HttpRequestMessage(method, statusRequest ? RequireStatus(url) : RequireApi(url));
        request.Headers.Authorization = new AuthenticationHeaderValue("Key", credentials);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        Log($"{method} {request.RequestUri!.AbsolutePath}\n" + (body is null ? "" : SafeJson(body)));
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) { Log($"HTTP {(int)response.StatusCode}"); throw new ApiException((int)response.StatusCode); }
        var text = await response.Content.ReadAsStringAsync(ct);
        var json = JsonNode.Parse(text) as JsonObject ?? throw new InvalidDataException("API応答がJSONオブジェクトではありません。");
        Log($"HTTP {(int)response.StatusCode}\n{SafeJson(json)}");
        return json;
    }
    public Task<JsonObject> EstimateAsync(string endpoint, JsonObject body, CancellationToken ct) => Send(HttpMethod.Post, Base + "estimate/" + endpoint, body, ct);
    // Deliberately no automatic POST retries: a timeout may still mean the server accepted the generation.
    public Task<JsonObject> SubmitAsync(string endpoint, JsonObject body, CancellationToken ct) => Send(HttpMethod.Post, Base + endpoint, body, ct);
    public Task<JsonObject> StatusAsync(string statusUrl, CancellationToken ct) => Send(HttpMethod.Get, statusUrl, null, ct, statusRequest: true);

    public async Task<string> UploadAsync(string path, CancellationToken ct)
    {
        var type = Path.GetExtension(path).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => throw new ArgumentException("PNGまたはJPEGを選んでください。") };
        var info = new FileInfo(path);
        if (!info.Exists || info.Length == 0 || info.Length > 30 * 1024 * 1024) throw new ArgumentException("画像は30MB以下のPNG/JPEGを選んでください（アプリ上限）。");
        using var file = File.OpenRead(path);
        var cacheKey = type + ":" + Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
        ct.ThrowIfCancellationRequested();
        if (uploadedImages.TryGetValue(cacheKey, out var cachedUrl))
        {
            Log("アップロード済み画像を再利用（画像URLは非表示）");
            return cachedUrl;
        }
        file.Position = 0;
        var json = await Send(HttpMethod.Post, Base + "files/generate-upload-url", new JsonObject { ["content_type"] = type }, ct);
        var uploadUri = RequireHttps(json["upload_url"]?.ToString() ?? "");
        var publicUrl = json["public_url"]?.ToString() ?? "";
        RequireHttps(publicUrl);
        using var request = new HttpRequestMessage(HttpMethod.Put, uploadUri) { Content = new StreamContent(file) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(type);
        if (json["upload_headers"] is JsonObject headers)
        {
            foreach (var pair in headers)
            {
                if (pair.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("アップロードヘッダーが不正です。");
                if (pair.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    request.Content.Headers.Remove(pair.Key);
                    request.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value?.ToString());
                }
                else request.Headers.TryAddWithoutValidation(pair.Key, pair.Value?.ToString());
            }
        }
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"画像アップロード HTTP {(int)response.StatusCode}");
        uploadedImages[cacheKey] = publicUrl;
        Log("画像アップロード完了（署名URL・認証情報は非表示）");
        return publicUrl;
    }
    public async Task PollAsync(Job job, Action<Job> update, CancellationToken ct)
    {
        var until = DateTime.UtcNow.AddMinutes(30);
        var delay = 2.0;
        var failures = 0;
        while (!job.Terminal)
        {
            if (DateTime.UtcNow >= until) throw new TimeoutException("確認を30分で中断しました。履歴から状態確認を再開できます。新しい生成は送信しないでください。");
            ct.ThrowIfCancellationRequested();
            try
            {
                job.Apply(await StatusAsync(job.StatusUrl, ct));
                update(job);
                failures = 0;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is ApiException a && (a.StatusCode >= 500 || a.StatusCode == 429))
            {
                if (++failures >= 5) throw new InvalidOperationException("状態確認の通信に失敗しました。履歴から再開できます。", ex);
            }
            if (job.Terminal) return;
            await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            delay = Math.Min(delay * 1.5, 10);
        }
    }
    public async Task DownloadAsync(string url, string destination, CancellationToken ct)
    {
        var uri = RequireHttps(url);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".part";
        try
        {
            for (int redirects = 0; redirects <= 5; redirects++)
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
                {
                    uri = RequireHttps(new Uri(uri, location).ToString());
                    continue;
                }
                response.EnsureSuccessStatusCode();
                await using (var output = File.Create(temp)) await response.Content.CopyToAsync(output, ct);
                File.Move(temp, destination, true);
                return;
            }
            throw new IOException("動画URLのリダイレクト回数が多すぎます。");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Dispose() => http.Dispose();
}
