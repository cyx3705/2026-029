using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HistoryJuno;

internal sealed record Sub2ApiCredentials(string Email, string Password)
{
    public static Sub2ApiCredentials LoadDefault()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("SUB2API_DEPLOY_ROOT");
        var root = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "sub2api-deploy")
            : configuredRoot.Trim();
        var path = Path.Combine(root, "CREDENTIALS.txt");
        if (!File.Exists(path))
        {
            throw new Sub2ApiAdminException(
                "找不到 Sub2API 管理凭据。请设置 SUB2API_DEPLOY_ROOT，或在默认部署目录放置 CREDENTIALS.txt。");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        if (!values.TryGetValue("ADMIN_EMAIL", out var email) || string.IsNullOrWhiteSpace(email)
            || !values.TryGetValue("ADMIN_PASSWORD", out var password) || string.IsNullOrWhiteSpace(password))
        {
            throw new Sub2ApiAdminException("Sub2API 管理凭据缺少 ADMIN_EMAIL 或 ADMIN_PASSWORD。");
        }

        return new Sub2ApiCredentials(email, password);
    }
}

internal sealed class Sub2ApiAdminException(string message) : Exception(message);

internal sealed record Sub2ApiAccount(
    long Id,
    string Name,
    string Platform,
    [property: JsonPropertyName("type")] string AccountType,
    string Status,
    [property: JsonPropertyName("error_message")] string? ErrorMessage,
    bool Schedulable,
    [property: JsonPropertyName("rate_limit_reset_at")] DateTimeOffset? RateLimitResetAt,
    [property: JsonPropertyName("overload_until")] DateTimeOffset? OverloadUntil,
    [property: JsonPropertyName("temp_unschedulable_until")] DateTimeOffset? TempUnschedulableUntil);

internal sealed record Sub2ApiUsageWindow(
    double Utilization,
    [property: JsonPropertyName("resets_at")] DateTimeOffset? ResetsAt);

internal sealed record Sub2ApiUsage(
    [property: JsonPropertyName("five_hour")] Sub2ApiUsageWindow? FiveHour,
    [property: JsonPropertyName("seven_day")] Sub2ApiUsageWindow? SevenDay);

internal sealed record Sub2ApiAccountSnapshot(
    Sub2ApiAccount Account,
    Sub2ApiUsage? Usage,
    bool UsageFailed);

internal sealed class Sub2ApiAdminClient : IDisposable
{
    private const int PageSize = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly Func<Sub2ApiCredentials> _credentials;
    private readonly bool _ownsHttpClient;

    internal Sub2ApiAdminClient(
        HttpClient http,
        Func<Sub2ApiCredentials> credentials,
        bool ownsHttpClient = false)
    {
        _http = http;
        _credentials = credentials;
        _ownsHttpClient = ownsHttpClient;
    }

    public static Sub2ApiAdminClient CreateDefault()
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:8080/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(12),
        };
        return new Sub2ApiAdminClient(http, Sub2ApiCredentials.LoadDefault, ownsHttpClient: true);
    }

    public async Task<IReadOnlyList<Sub2ApiAccountSnapshot>> LoadOpenAiOAuthAccountsAsync(
        CancellationToken cancellation)
    {
        var credentials = _credentials();
        var token = await LoginAsync(credentials, cancellation).ConfigureAwait(false);
        var accounts = await LoadAccountsAsync(token, cancellation).ConfigureAwait(false);
        var (usage, errors) = await LoadUsageAsync(token, accounts.Select(account => account.Id), cancellation)
            .ConfigureAwait(false);

        return accounts
            .Select(account => new Sub2ApiAccountSnapshot(
                account,
                usage.GetValueOrDefault(account.Id),
                errors.Contains(account.Id)))
            .ToArray();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    private async Task<string> LoginAsync(Sub2ApiCredentials credentials, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = credentials.Email, password = credentials.Password }),
        };
        var data = await SendForDataAsync(request, "Sub2API 管理登录失败", cancellation).ConfigureAwait(false);
        if (!TryReadString(data, "token", out var token)
            && !TryReadString(data, "access_token", out token))
        {
            throw new Sub2ApiAdminException("Sub2API 管理登录响应缺少访问令牌。");
        }

        return token;
    }

    private async Task<List<Sub2ApiAccount>> LoadAccountsAsync(string token, CancellationToken cancellation)
    {
        var accounts = new List<Sub2ApiAccount>();
        var page = 1;
        var pages = 1;
        do
        {
            using var request = Authorized(
                HttpMethod.Get,
                $"api/v1/admin/accounts?page={page}&page_size={PageSize}&platform=openai&type=oauth",
                token);
            var data = await SendForDataAsync(request, "Sub2API 账号列表读取失败", cancellation)
                .ConfigureAwait(false);
            if (!data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                throw new Sub2ApiAdminException("Sub2API 账号列表响应格式无效。");

            foreach (var item in items.EnumerateArray())
            {
                var account = item.Deserialize<Sub2ApiAccount>(JsonOptions);
                if (account != null
                    && string.Equals(account.Platform, "openai", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(account.AccountType, "oauth", StringComparison.OrdinalIgnoreCase))
                {
                    accounts.Add(account);
                }
            }

            pages = data.TryGetProperty("pages", out var pageCount) && pageCount.TryGetInt32(out var parsedPages)
                ? Math.Max(1, parsedPages)
                : 1;
            page++;
        }
        while (page <= pages);

        return accounts;
    }

    private async Task<(Dictionary<long, Sub2ApiUsage> Usage, HashSet<long> Errors)> LoadUsageAsync(
        string token,
        IEnumerable<long> accountIds,
        CancellationToken cancellation)
    {
        var ids = accountIds.Distinct().ToArray();
        var usage = new Dictionary<long, Sub2ApiUsage>();
        var errors = new HashSet<long>();
        if (ids.Length == 0)
            return (usage, errors);

        using var request = Authorized(HttpMethod.Post, "api/v1/admin/accounts/usage/batch", token);
        request.Content = JsonContent.Create(new { account_ids = ids, force = false });
        var data = await SendForDataAsync(request, "Sub2API 账号额度读取失败", cancellation)
            .ConfigureAwait(false);

        if (data.TryGetProperty("usage", out var usageObject) && usageObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in usageObject.EnumerateObject())
            {
                if (long.TryParse(property.Name, out var id))
                {
                    var item = property.Value.Deserialize<Sub2ApiUsage>(JsonOptions);
                    if (item != null)
                        usage[id] = item;
                }
            }
        }

        if (data.TryGetProperty("errors", out var errorObject) && errorObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in errorObject.EnumerateObject())
            {
                if (long.TryParse(property.Name, out var id))
                    errors.Add(id);
            }
        }

        return (usage, errors);
    }

    private async Task<JsonElement> SendForDataAsync(
        HttpRequestMessage request,
        string failureMessage,
        CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new Sub2ApiAdminException("连接 Sub2API 管理端超时。");
        }
        catch (HttpRequestException)
        {
            throw new Sub2ApiAdminException("连接不到 Sub2API 管理端，请先启动 Sub2API。");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new Sub2ApiAdminException($"{failureMessage}（HTTP {(int)response.StatusCode}）。");

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellation)
                    .ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("data", out var data))
                    throw new Sub2ApiAdminException($"{failureMessage}：响应格式无效。");
                return data.Clone();
            }
            catch (JsonException)
            {
                throw new Sub2ApiAdminException($"{failureMessage}：响应格式无效。");
            }
        }
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static bool TryReadString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? "";
        return value.Length > 0;
    }
}
