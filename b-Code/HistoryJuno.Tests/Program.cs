using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HistoryJuno;

var tests = new (string Name, Func<Task> Run)[]
{
    ("page protocol", TestPageProtocolAsync),
    ("quota formatting", TestQuotaFormattingAsync),
    ("status priority", TestStatusPriorityAsync),
    ("credentials", TestCredentialsAsync),
    ("account paging and usage", TestAccountPagingAndUsageAsync),
    ("account filters", TestAccountFiltersAsync),
    ("global proxy", TestGlobalProxyAsync),
    ("account import", TestAccountImportAsync),
    ("login redaction", TestLoginRedactionAsync),
    ("startup runner", TestStartupRunnerAsync),
    ("startup port readiness", TestStartupPortReadinessAsync),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static Task TestPageProtocolAsync()
{
    using var description = JsonDocument.Parse(JunoPages.DescribeJson());
    Equal(1, description.RootElement.GetProperty("pages").GetArrayLength());
    var page = description.RootElement.GetProperty("pages")[0];
    Equal("sub2api", page.GetProperty("id").GetString());
    Equal("Juno", page.GetProperty("title").GetString());
    var children = page.GetProperty("content").GetProperty("children");
    var rows = children[0].GetProperty("rows");
    Equal(2, rows.GetArrayLength());
    var selector = rows[1].GetProperty("widgets")[0];
    Equal("juno.section", selector.GetProperty("channel").GetString());
    Equal("账号管理", selector.GetProperty("options")[0].GetString());
    Equal("服务状态", selector.GetProperty("options")[1].GetString());

    Equal(3, selector.GetProperty("options").GetArrayLength());
    Equal("账号导入", selector.GetProperty("options")[2].GetString());
    var switchNode = children[1];
    Equal(3, switchNode.GetProperty("children").GetArrayLength());
    Equal("switch", switchNode.GetProperty("type").GetString());
    var accountStack = switchNode.GetProperty("children")[0];
    Equal("账号管理", accountStack.GetProperty("case").GetString());
    var accountChildren = accountStack.GetProperty("children");
    var filters = accountChildren[0].GetProperty("rows")[0].GetProperty("widgets");
    Equal("检索分组", filters[0].GetProperty("label").GetString());
    Equal("juno.ui.groups", filters[0].GetProperty("optionsSource").GetProperty("command").GetString());
    Equal("检索状态", filters[1].GetProperty("label").GetString());
    Equal("juno-accounts", accountChildren[1].GetProperty("id").GetString());
    Equal(
        "{selection.juno.account.group.value}",
        accountChildren[1].GetProperty("dataSource").GetProperty("args").GetProperty("group").GetString());
    var serviceTable = switchNode.GetProperty("children")[1];
    Equal("juno-status", serviceTable.GetProperty("id").GetString());
    Equal(4, serviceTable.GetProperty("columns").GetArrayLength());

    var importSection = switchNode.GetProperty("children")[2];
    Equal("账号导入", importSection.GetProperty("case").GetString());
    var importRows = importSection.GetProperty("children")[0].GetProperty("rows");
    Equal(2, importRows.GetArrayLength());
    Equal("juno-import-accounts", importSection.GetProperty("children")[1].GetProperty("id").GetString());
    var source = importRows[0].GetProperty("widgets")[0];
    Equal("sourcePicker", source.GetProperty("kind").GetString());
    Equal("JSON 来源", source.GetProperty("label").GetString());
    Equal("juno.import.select", source.GetProperty("selectCommand").GetString());
    var importGroup = importRows[1].GetProperty("widgets")[0];
    Equal("juno.ui.groups", importGroup.GetProperty("optionsSource").GetProperty("command").GetString());

    using var actions = JsonDocument.Parse(JunoPages.ActionsJson());
    var sourceCommit = actions.RootElement.GetProperty("actions").EnumerateArray()
        .Single(action => action.GetProperty("id").GetString() == source.GetProperty("commitAction").GetString());
    Equal("aurora.ui.panelset", sourceCommit.GetProperty("command").GetString());
    Equal("import-controls", sourceCommit.GetProperty("args").GetProperty("panel").GetString());
    Equal("jsonSource", sourceCommit.GetProperty("args").GetProperty("control").GetString());
    Equal("{value}", sourceCommit.GetProperty("args").GetProperty("value").GetString());
    var refresh = actions.RootElement.GetProperty("actions").EnumerateArray()
        .Single(action => action.GetProperty("id").GetString() == JunoPages.RefreshAction);
    Equal("aurora.ui.refreshdata", refresh.GetProperty("command").GetString());
    Equal("sub2api", refresh.GetProperty("args").GetProperty("page").GetString());
    foreach (var refreshAction in actions.RootElement.GetProperty("actions").EnumerateArray()
        .Where(action => action.GetProperty("command").GetString() == "aurora.ui.refreshdata"))
        Equal("sub2api", refreshAction.GetProperty("args").GetProperty("page").GetString());
    var import = actions.RootElement.GetProperty("actions").EnumerateArray()
        .Single(action => action.GetProperty("id").GetString() == JunoPages.ImportAction);
    Equal("juno.accounts.import", import.GetProperty("command").GetString());
    Equal("{jsonSource}", import.GetProperty("args").GetProperty("source").GetString());
    Equal("{selection.juno.import.group.value}", import.GetProperty("args").GetProperty("group").GetString());
    return Task.CompletedTask;
}

static Task TestQuotaFormattingAsync()
{
    var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    Equal("-", JunoPages.FormatQuota(null, false, now));
    Equal("获取失败", JunoPages.FormatQuota(null, true, now));
    Equal(
        "已用 34% · 2h18m 后重置",
        JunoPages.FormatQuota(new Sub2ApiUsageWindow(33.6, now.AddHours(2).AddMinutes(18)), false, now));
    Equal(
        "已用 105% · 6d2h 后重置",
        JunoPages.FormatQuota(new Sub2ApiUsageWindow(105.2, now.AddDays(6).AddHours(2)), false, now));
    Equal(
        "已用 0% · 已重置",
        JunoPages.FormatQuota(new Sub2ApiUsageWindow(0, now.AddMinutes(-1)), false, now));
    Equal("已用 50%", JunoPages.FormatQuota(new Sub2ApiUsageWindow(50, null), false, now));
    return Task.CompletedTask;
}

static Task TestStatusPriorityAsync()
{
    var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    var normal = Account(status: "active", schedulable: true);
    Equal("正常", JunoPages.FormatStatus(normal, now));
    Equal("暂停调度", JunoPages.FormatStatus(Account(status: "active", schedulable: false), now));
    Equal("停用", JunoPages.FormatStatus(Account(status: "inactive"), now));
    Equal("临时不可调度", JunoPages.FormatStatus(Account(temp: now.AddMinutes(5)), now));
    Equal("错误", JunoPages.FormatStatus(Account(status: "error", temp: now.AddMinutes(5)), now));
    Equal("过载", JunoPages.FormatStatus(Account(overload: now.AddMinutes(5), status: "error"), now));
    Equal("限流", JunoPages.FormatStatus(Account(rateLimit: now.AddMinutes(5), overload: now.AddMinutes(5)), now));
    return Task.CompletedTask;
}

static Task TestCredentialsAsync()
{
    var previous = Environment.GetEnvironmentVariable("SUB2API_DEPLOY_ROOT");
    var root = Path.Combine(Path.GetTempPath(), $"HistoryJuno-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        Environment.SetEnvironmentVariable("SUB2API_DEPLOY_ROOT", root);
        Throws<Sub2ApiAdminException>(() => Sub2ApiCredentials.LoadDefault());
        File.WriteAllText(
            Path.Combine(root, "CREDENTIALS.txt"),
            "ADMIN_EMAIL=admin@example.test\nADMIN_PASSWORD=secret-value\n",
            Encoding.UTF8);
        var credentials = Sub2ApiCredentials.LoadDefault();
        Equal("admin@example.test", credentials.Email);
        Equal("secret-value", credentials.Password);
    }
    finally
    {
        Environment.SetEnvironmentVariable("SUB2API_DEPLOY_ROOT", previous);
        Directory.Delete(root, recursive: true);
    }

    return Task.CompletedTask;
}

static async Task TestAccountPagingAndUsageAsync()
{
    var handler = new QueueHandler(
        Json(HttpStatusCode.OK, """{"data":{"token":"runtime-token"}}"""),
        Json(HttpStatusCode.OK, """
            {"data":{"items":[
              {"id":1,"name":"openai-one","platform":"openai","type":"oauth","status":"active","schedulable":true},
              {"id":2,"name":"anthropic","platform":"anthropic","type":"oauth","status":"active","schedulable":true}
            ],"pages":2}}
            """),
        Json(HttpStatusCode.OK, """
            {"data":{"items":[
              {"id":3,"name":"openai-two","platform":"openai","type":"oauth","status":"inactive","schedulable":false},
              {"id":4,"name":"api-key","platform":"openai","type":"apikey","status":"active","schedulable":true}
            ],"pages":2}}
            """),
        Json(HttpStatusCode.OK, """
            {"data":{"usage":{"1":{"five_hour":{"utilization":34,"resets_at":"2026-08-31T14:18:00Z"},"seven_day":{"utilization":70,"resets_at":null}}},"errors":{"3":"upstream unavailable"}}}
            """));
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080/") };
    using var client = new Sub2ApiAdminClient(
        http,
        () => new Sub2ApiCredentials("admin@example.test", "do-not-leak"));

    var snapshots = await client.LoadOpenAiOAuthAccountsAsync(CancellationToken.None);
    Equal(2, snapshots.Count);
    Equal("openai-one", snapshots[0].Account.Name);
    Equal(34d, snapshots[0].Usage?.FiveHour?.Utilization);
    Equal("openai-two", snapshots[1].Account.Name);
    True(snapshots[1].UsageFailed, "partial usage failure was not mapped");
    Equal(4, handler.Requests.Count);
    True(handler.Requests[1].PathAndQuery.Contains("platform=openai&type=oauth"), "server filter missing");
    True(handler.Requests[2].PathAndQuery.Contains("page=2"), "second page was not requested");
    Equal("Bearer runtime-token", handler.Requests[3].Authorization);
    True(handler.Requests[3].Body.Contains("\"account_ids\":[1,3]"), "batch usage IDs are incorrect");
}

static Task TestAccountFiltersAsync()
{
    var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
    var rows = JunoPages.BuildAccountRows(
        [
            new Sub2ApiAccountSnapshot(
                Account(groupId: 9, groupName: "free"),
                new Sub2ApiUsage(null, null),
                false),
            new Sub2ApiAccountSnapshot(
                Account(status: "inactive", groupId: 5, groupName: "paid"),
                null,
                false),
        ],
        now,
        "9 · free",
        "正常");
    Equal(1, rows.Count);
    Equal("free", rows[0]["group"]);
    Equal("win-proxy", rows[0]["proxy"]);
    True(JunoPages.TryParseGroupOption("5 · paid", out var groupId), "group option was not parsed");
    Equal(5L, groupId);
    True(!JunoPages.TryParseGroupOption(JunoPages.AllGroups, out _), "all-groups was parsed as an ID");
    return Task.CompletedTask;
}

static Task TestGlobalProxyAsync()
{
    var selected = Sub2ApiAdminClient.ResolveGlobalProxy(
    [
        new Sub2ApiProxy(4, "backup", "active", "203.0.113.10"),
        new Sub2ApiProxy(1, "primary", "active", "203.0.113.10"),
        new Sub2ApiProxy(2, "paused", "inactive", "203.0.113.20"),
    ]);
    Equal(1L, selected.Id);
    Throws<Sub2ApiAdminException>(() => Sub2ApiAdminClient.ResolveGlobalProxy(
    [
        new Sub2ApiProxy(1, "one", "active", "203.0.113.10"),
        new Sub2ApiProxy(2, "two", "active", "203.0.113.20"),
    ]));
    return Task.CompletedTask;
}

static async Task TestAccountImportAsync()
{
    var handler = new QueueHandler(
        Json(HttpStatusCode.OK, """{"data":{"access_token":"runtime-token"}}"""),
        Json(HttpStatusCode.OK, """{"data":{"items":[{"id":9,"name":"free","status":"active"}],"pages":1}}"""),
        Json(HttpStatusCode.OK, """{"data":{"items":[{"id":1,"name":"win-proxy","status":"active","ip_address":"203.0.113.10"}],"pages":1}}"""),
        Json(HttpStatusCode.OK, """{"data":{"items":[{"id":10,"name":"existing","platform":"openai","type":"oauth","status":"active","schedulable":true}],"pages":1}}"""),
        Json(HttpStatusCode.OK, """{"data":{"account_created":1,"account_failed":0,"proxy_created":0,"proxy_reused":0,"proxy_failed":0}}"""),
        Json(HttpStatusCode.OK, """{"data":{"items":[{"id":10,"name":"existing","platform":"openai","type":"oauth","status":"active","schedulable":true},{"id":11,"name":"new-account","platform":"openai","type":"oauth","status":"active","schedulable":true}],"pages":1}}"""),
        Json(HttpStatusCode.OK, """{"data":{"id":11,"credentials":{"refresh_token":"test-token"}}}"""),
        Json(HttpStatusCode.OK, """{"data":{"id":11}}"""));
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080/") };
    using var client = new Sub2ApiAdminClient(
        http,
        () => new Sub2ApiCredentials("admin@example.test", "do-not-leak"));
    using var package = JsonDocument.Parse("""
        {"data":{"accounts":[{"name":"new-account","platform":"openai","type":"oauth","credentials":{"refresh_token":"test-token"}}]}}
        """);

    var outcome = await client.ImportAccountsAsync(package.RootElement, 9, CancellationToken.None);
    Equal(1, outcome.Created);
    Equal(1, outcome.Configured);
    Equal(8, handler.Requests.Count);
    Equal("/api/v1/admin/accounts/data", handler.Requests[4].PathAndQuery);
    Equal("/api/v1/admin/accounts/11", handler.Requests[7].PathAndQuery);
    using var update = JsonDocument.Parse(handler.Requests[7].Body);
    Equal(9L, update.RootElement.GetProperty("group_ids")[0].GetInt64());
    Equal(1L, update.RootElement.GetProperty("proxy_id").GetInt64());
    Equal("test-token", update.RootElement.GetProperty("credentials").GetProperty("refresh_token").GetString());
}

static async Task TestLoginRedactionAsync()
{
    const string secret = "top-secret-password";
    var handler = new QueueHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
    {
        Content = new StringContent($"credential rejected: {secret}"),
    });
    using var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8080/") };
    using var client = new Sub2ApiAdminClient(http, () => new Sub2ApiCredentials("admin@example.test", secret));
    try
    {
        await client.LoadOpenAiOAuthAccountsAsync(CancellationToken.None);
        throw new InvalidOperationException("expected login failure");
    }
    catch (Sub2ApiAdminException ex)
    {
        True(!ex.Message.Contains(secret, StringComparison.Ordinal), "secret leaked through failure message");
        True(!ex.Message.Contains("credential rejected", StringComparison.Ordinal), "response body leaked");
    }
}

static async Task TestStartupRunnerAsync()
{
    True(
        Sub2ApiStartupRunner.TryConvertToWslPath(@"C:\Users\Test User\deploy", out var wslPath),
        "Windows deploy path was not mapped to WSL");
    Equal("/mnt/c/Users/Test User/deploy", wslPath);
    var composeCommand = Sub2ApiStartupRunner.BuildComposeCommand("/mnt/c/Users/Test User/deploy");
    True(composeCommand.Contains("docker compose up -d", StringComparison.Ordinal), "compose start is missing");
    True(!composeCommand.Contains("curl", StringComparison.Ordinal), "startup still contains the blocking proxy probe");

    var steps = new List<string>();
    var startup = await Sub2ApiStartupRunner.RunStartupStepsAsync(
        "/mnt/c/deploy",
        (step, _, _, _) =>
        {
            steps.Add(step);
            return Task.FromResult(
                steps.Count == 1
                    ? new StartupResult(false, "docker returned 1")
                    : new StartupResult(true, "compose started"));
        },
        CancellationToken.None);
    True(startup.Success, "compose success did not override the Docker service exit code");
    Equal(2, steps.Count);

    var previous = Environment.GetEnvironmentVariable("SUB2API_TOOL_ROOT");
    var root = Path.Combine(Path.GetTempPath(), $"HistoryJuno-runner-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    try
    {
        Environment.SetEnvironmentVariable("SUB2API_TOOL_ROOT", root);
        File.WriteAllText(Path.Combine(root, "slow.ps1"), "Start-Sleep -Seconds 5", Encoding.UTF8);
        var started = DateTimeOffset.UtcNow;
        var result = await Sub2ApiProcessRunner.RunAsync(
            "slow.ps1",
            [],
            CancellationToken.None,
            new ScriptRunOptions(TimeSpan.FromMilliseconds(300)));
        True(!result.Success, "timed out script was reported as successful");
        Equal(-2, result.ExitCode);
        True(result.Output.Contains("已终止", StringComparison.Ordinal), "timeout is not actionable");
        True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(4), "timeout did not bound execution");
    }
    finally
    {
        Environment.SetEnvironmentVariable("SUB2API_TOOL_ROOT", previous);
        Directory.Delete(root, recursive: true);
    }
}

static async Task TestStartupPortReadinessAsync()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    try
    {
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        True(
            await JunoCommandCatalog.WaitForPortsAsync(
                [port],
                TimeSpan.FromSeconds(1),
                CancellationToken.None),
            "listening port was not detected");
    }
    finally
    {
        listener.Stop();
    }
}

static Sub2ApiAccount Account(
    string status = "active",
    bool schedulable = true,
    DateTimeOffset? rateLimit = null,
    DateTimeOffset? overload = null,
    DateTimeOffset? temp = null,
    long groupId = 0,
    string groupName = "")
    => new(
        1,
        "account",
        "openai",
        "oauth",
        status,
        null,
        schedulable,
        rateLimit,
        overload,
        temp,
        groupId > 0 ? [groupId] : null,
        groupId > 0 ? [new Sub2ApiGroup(groupId, groupName, "active")] : null,
        1,
        new Sub2ApiProxySummary(1, "win-proxy"));

static HttpResponseMessage Json(HttpStatusCode status, string json)
    => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"expected '{expected}', actual '{actual}'");
}

static void True(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"expected {typeof(TException).Name}");
}

file sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);

    public List<RequestSnapshot> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content == null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RequestSnapshot(
            request.RequestUri?.PathAndQuery ?? "",
            request.Headers.Authorization?.ToString() ?? "",
            body));
        if (_responses.Count == 0)
            throw new InvalidOperationException("unexpected HTTP request");
        return _responses.Dequeue();
    }
}

file sealed record RequestSnapshot(string PathAndQuery, string Authorization, string Body);
