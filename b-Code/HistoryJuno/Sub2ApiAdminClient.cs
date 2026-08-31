using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
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

internal sealed record Sub2ApiGroup(long Id, string Name, string Status);

internal sealed record Sub2ApiProxySummary(long Id, string Name);

internal sealed record Sub2ApiProxy(
    long Id,
    string Name,
    string Status,
    [property: JsonPropertyName("ip_address")] string? IpAddress);

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
    [property: JsonPropertyName("temp_unschedulable_until")] DateTimeOffset? TempUnschedulableUntil,
    [property: JsonPropertyName("group_ids")] IReadOnlyList<long>? GroupIds = null,
    IReadOnlyList<Sub2ApiGroup>? Groups = null,
    [property: JsonPropertyName("proxy_id")] long? ProxyId = null,
    Sub2ApiProxySummary? Proxy = null);

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

internal sealed record Sub2ApiImportOutcome(
    int Created,
    int Failed,
    int Configured,
    long GroupId,
    long ProxyId);

internal sealed class Sub2ApiAdminClient : IDisposable
{
    private const int PageSize = 100;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ImportTimeout = TimeSpan.FromSeconds(120);
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
            Timeout = Timeout.InfiniteTimeSpan,
        };
        return new Sub2ApiAdminClient(http, Sub2ApiCredentials.LoadDefault, ownsHttpClient: true);
    }

    public async Task<IReadOnlyList<Sub2ApiAccountSnapshot>> LoadOpenAiOAuthAccountsAsync(
        CancellationToken cancellation)
    {
        var token = await LoginAsync(_credentials(), cancellation).ConfigureAwait(false);
        var accounts = await LoadAccountsAsync(token, onlyOpenAiOAuth: true, cancellation).ConfigureAwait(false);
        var (usage, errors) = await LoadUsageAsync(token, accounts.Select(account => account.Id), cancellation)
            .ConfigureAwait(false);

        return accounts
            .Select(account => new Sub2ApiAccountSnapshot(
                account,
                usage.GetValueOrDefault(account.Id),
                errors.Contains(account.Id)))
            .ToArray();
    }

    public async Task<IReadOnlyList<Sub2ApiGroup>> LoadGroupsAsync(CancellationToken cancellation)
    {
        var token = await LoginAsync(_credentials(), cancellation).ConfigureAwait(false);
        return await LoadGroupsAsync(token, cancellation).ConfigureAwait(false);
    }

    public async Task<Sub2ApiImportOutcome> ImportAccountsAsync(
        JsonElement package,
        long groupId,
        CancellationToken cancellation)
    {
        var normalized = NormalizeImportPackage(package, out var requestedAccounts);
        if (requestedAccounts == 0)
            throw new Sub2ApiAdminException("JSON 来源没有可导入的账号。");

        var token = await LoginAsync(_credentials(), cancellation).ConfigureAwait(false);
        var groups = await LoadGroupsAsync(token, cancellation).ConfigureAwait(false);
        if (!groups.Any(group => group.Id == groupId && IsActive(group.Status)))
            throw new Sub2ApiAdminException("所选导入分组不存在或已停用，请刷新后重新选择。");

        var proxies = await LoadProxiesAsync(token, cancellation).ConfigureAwait(false);
        var globalProxy = ResolveGlobalProxy(proxies);
        var beforeIds = (await LoadAccountsAsync(token, onlyOpenAiOAuth: false, cancellation).ConfigureAwait(false))
            .Select(account => account.Id)
            .ToHashSet();

        var importData = await ImportDataAsync(token, normalized, cancellation).ConfigureAwait(false);
        var created = ReadCount(importData, "account_created");
        var failed = ReadCount(importData, "account_failed");
        if (created == 0)
            return new Sub2ApiImportOutcome(0, failed, 0, groupId, globalProxy.Id);

        var after = await LoadAccountsAsync(token, onlyOpenAiOAuth: false, cancellation).ConfigureAwait(false);
        var imported = after.Where(account => !beforeIds.Contains(account.Id)).OrderBy(account => account.Id).ToArray();
        if (imported.Length == 0)
        {
            throw new Sub2ApiAdminException(
                $"Sub2API 报告已导入 {created} 个账号，但无法识别新账号，未执行分组和代理配置。");
        }

        var configured = 0;
        foreach (var account in imported)
        {
            try
            {
                await ConfigureAccountAsync(token, account.Id, groupId, globalProxy.Id, cancellation)
                    .ConfigureAwait(false);
                configured++;
            }
            catch (Sub2ApiAdminException)
            {
                throw new Sub2ApiAdminException(
                    $"已导入 {created} 个账号，但自动配置在第 {configured + 1} 个账号失败；已完成 {configured} 个。");
            }
        }

        return new Sub2ApiImportOutcome(created, failed, configured, groupId, globalProxy.Id);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    internal static Sub2ApiProxy ResolveGlobalProxy(IReadOnlyList<Sub2ApiProxy> proxies)
    {
        var active = proxies
            .Where(proxy => IsActive(proxy.Status) && !string.IsNullOrWhiteSpace(proxy.IpAddress))
            .GroupBy(proxy => proxy.IpAddress!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (active.Length == 0)
        {
            throw new Sub2ApiAdminException(
                "没有已取得 IP 的活动代理。请先在账号导入页执行“代理重连”，再刷新后导入。");
        }

        if (active.Length > 1)
        {
            throw new Sub2ApiAdminException(
                "检测到多个活动代理 IP，无法确定全局唯一代理。请先收敛为一个代理 IP 后再导入。");
        }

        return active[0].OrderBy(proxy => proxy.Id).First();
    }

    private static bool IsActive(string? status)
        => string.Equals(status, "active", StringComparison.OrdinalIgnoreCase);

    private static JsonElement NormalizeImportPackage(JsonElement package, out int accountCount)
    {
        JsonObject root;
        try
        {
            root = JsonNode.Parse(package.GetRawText()) as JsonObject
                ?? throw new Sub2ApiAdminException("JSON 来源根节点必须是对象。");
        }
        catch (JsonException)
        {
            throw new Sub2ApiAdminException("JSON 来源格式无效。");
        }

        if (root["data"] is not JsonObject data)
        {
            if (root["accounts"] is null && root["proxies"] is null)
                throw new Sub2ApiAdminException("JSON 来源缺少 data 节点或 accounts 数组。");
            data = root;
            root = new JsonObject
            {
                ["data"] = data,
                ["skip_default_group_bind"] = true,
            };
        }

        accountCount = data["accounts"] is JsonArray accounts ? accounts.Count : 0;
        if (data["exported_at"] is null || string.IsNullOrWhiteSpace(data["exported_at"]?.GetValue<string>()))
            data["exported_at"] = DateTimeOffset.UtcNow.ToString("O");
        return JsonSerializer.SerializeToElement(root, JsonOptions);
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

    private async Task<List<Sub2ApiAccount>> LoadAccountsAsync(
        string token,
        bool onlyOpenAiOAuth,
        CancellationToken cancellation)
    {
        var accounts = new List<Sub2ApiAccount>();
        var page = 1;
        var pages = 1;
        do
        {
            var filter = onlyOpenAiOAuth ? "&platform=openai&type=oauth" : "";
            using var request = Authorized(
                HttpMethod.Get,
                $"api/v1/admin/accounts?page={page}&page_size={PageSize}{filter}",
                token);
            var data = await SendForDataAsync(request, "Sub2API 账号列表读取失败", cancellation)
                .ConfigureAwait(false);
            foreach (var item in ReadItems(data, "Sub2API 账号列表响应格式无效"))
            {
                var account = item.Deserialize<Sub2ApiAccount>(JsonOptions);
                if (account == null)
                    continue;
                if (!onlyOpenAiOAuth
                    || (string.Equals(account.Platform, "openai", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(account.AccountType, "oauth", StringComparison.OrdinalIgnoreCase)))
                {
                    accounts.Add(account);
                }
            }

            pages = ReadPages(data);
            page++;
        }
        while (page <= pages);

        return accounts;
    }

    private async Task<List<Sub2ApiGroup>> LoadGroupsAsync(string token, CancellationToken cancellation)
    {
        var groups = new List<Sub2ApiGroup>();
        var page = 1;
        var pages = 1;
        do
        {
            using var request = Authorized(
                HttpMethod.Get,
                $"api/v1/admin/groups?page={page}&page_size={PageSize}",
                token);
            var data = await SendForDataAsync(request, "Sub2API 分组列表读取失败", cancellation)
                .ConfigureAwait(false);
            foreach (var item in ReadItems(data, "Sub2API 分组列表响应格式无效"))
            {
                var group = item.Deserialize<Sub2ApiGroup>(JsonOptions);
                if (group != null)
                    groups.Add(group);
            }

            pages = ReadPages(data);
            page++;
        }
        while (page <= pages);

        return groups;
    }

    private async Task<List<Sub2ApiProxy>> LoadProxiesAsync(string token, CancellationToken cancellation)
    {
        var proxies = new List<Sub2ApiProxy>();
        var page = 1;
        var pages = 1;
        do
        {
            using var request = Authorized(
                HttpMethod.Get,
                $"api/v1/admin/proxies?page={page}&page_size={PageSize}",
                token);
            var data = await SendForDataAsync(request, "Sub2API 代理列表读取失败", cancellation)
                .ConfigureAwait(false);
            foreach (var item in ReadItems(data, "Sub2API 代理列表响应格式无效"))
            {
                var proxy = item.Deserialize<Sub2ApiProxy>(JsonOptions);
                if (proxy != null)
                    proxies.Add(proxy);
            }

            pages = ReadPages(data);
            page++;
        }
        while (page <= pages);

        return proxies;
    }

    private async Task<JsonElement> ImportDataAsync(
        string token,
        JsonElement package,
        CancellationToken cancellation)
    {
        using var request = Authorized(HttpMethod.Post, "api/v1/admin/accounts/data", token);
        request.Content = JsonContent.Create(package, options: JsonOptions);
        return await SendForDataAsync(
                request,
                "Sub2API 账号导入失败",
                cancellation,
                ImportTimeout)
            .ConfigureAwait(false);
    }

    private async Task ConfigureAccountAsync(
        string token,
        long accountId,
        long groupId,
        long proxyId,
        CancellationToken cancellation)
    {
        using var detailRequest = Authorized(HttpMethod.Get, $"api/v1/admin/accounts/{accountId}", token);
        var detail = await SendForDataAsync(detailRequest, "Sub2API 账号详情读取失败", cancellation)
            .ConfigureAwait(false);
        if (!detail.TryGetProperty("credentials", out var credentials)
            || credentials.ValueKind != JsonValueKind.Object)
        {
            throw new Sub2ApiAdminException("Sub2API 账号详情缺少凭据对象，无法安全更新。");
        }

        using var updateRequest = Authorized(HttpMethod.Put, $"api/v1/admin/accounts/{accountId}", token);
        updateRequest.Content = JsonContent.Create(new
        {
            credentials = credentials.Clone(),
            group_ids = new[] { groupId },
            proxy_id = proxyId,
        });
        _ = await SendForDataAsync(updateRequest, "Sub2API 账号分组或代理更新失败", cancellation)
            .ConfigureAwait(false);
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
        CancellationToken cancellation,
        TimeSpan? timeout = null)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        bounded.CancelAfter(timeout ?? DefaultTimeout);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, bounded.Token)
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
                await using var stream = await response.Content.ReadAsStreamAsync(bounded.Token).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: bounded.Token)
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

    private static IEnumerable<JsonElement> ReadItems(JsonElement data, string error)
    {
        if (!data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new Sub2ApiAdminException(error + "。");
        return items.EnumerateArray();
    }

    private static int ReadPages(JsonElement data)
        => data.TryGetProperty("pages", out var pageCount) && pageCount.TryGetInt32(out var parsedPages)
            ? Math.Max(1, parsedPages)
            : 1;

    private static int ReadCount(JsonElement data, string name)
        => data.TryGetProperty(name, out var value) && value.TryGetInt32(out var count)
            ? Math.Max(0, count)
            : 0;

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
