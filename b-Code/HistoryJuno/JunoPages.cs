using System.Net.Sockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using HistoryVulcan.Core.Commands;

namespace HistoryJuno;

internal static class JunoPages
{
    private const string Owner = "HistoryJuno";
    private const int SchemaVersion = 1;
    internal const string AccountsView = "accounts";
    internal const string StatusView = "status";
    internal const string AccountsSection = "账号状态";
    internal const string ServicesSection = "服务状态";

    public const string StartAction = "juno.sub2api.start";
    public const string ProxyAction = "juno.proxy.connect";
    public const string StopAction = "juno.sub2api.stop";
    public const string RefreshAction = "juno.status.refresh";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string DescribeJson()
    {
        var description = new
        {
            schemaVersion = SchemaVersion,
            owner = Owner,
            pages = new[]
            {
                new
                {
                    id = "sub2api",
                    title = "Sub2API 控制面板",
                    placement = new { side = "right", visible = true, singleton = true },
                    content = new
                    {
                        type = "stack",
                        orientation = "vertical",
                        gap = "tight",
                        children = new object[]
                        {
                            new
                            {
                                type = "panel",
                                id = "controls",
                                text = "服务控制",
                                rows = new object[]
                                {
                                    new
                                    {
                                        mode = "even",
                                        widgets = new object[]
                                        {
                                            new { kind = "button", text = "启动 Sub2API", action = StartAction },
                                            new { kind = "button", text = "连接代理", action = ProxyAction },
                                            new { kind = "button", text = "关闭 Sub2API", action = StopAction },
                                            new { kind = "button", text = "刷新状态", action = RefreshAction },
                                        },
                                    },
                                    new
                                    {
                                        mode = "even",
                                        widgets = new object[]
                                        {
                                            new
                                            {
                                                kind = "textbox",
                                                id = "section",
                                                label = "页面",
                                                mode = "select",
                                                channel = "juno.section",
                                                options = new[] { AccountsSection, ServicesSection },
                                            },
                                        },
                                    },
                                },
                            },
                            new
                            {
                                type = "switch",
                                id = "juno-sections",
                                source = "{selection.juno.section.value}",
                                children = new object[]
                                {
                                    new
                                    {
                                        type = "table",
                                        @case = AccountsSection,
                                        id = "juno-accounts",
                                        dataSource = new
                                        {
                                            command = "juno.ui.data",
                                            args = new { view = AccountsView },
                                        },
                                        columns = new object[]
                                        {
                                            new { key = "account", title = "账号", width = "180" },
                                            new { key = "status", title = "状态", width = "100" },
                                            new { key = "fiveHour", title = "5h 额度", width = "180" },
                                            new { key = "sevenDay", title = "7d 额度", width = "180" },
                                        },
                                    },
                                    new
                                    {
                                        type = "table",
                                        @case = ServicesSection,
                                        id = "juno-status",
                                        dataSource = new
                                        {
                                            command = "juno.ui.data",
                                            args = new { view = StatusView },
                                        },
                                        columns = new object[]
                                        {
                                            new { key = "service", title = "服务", width = "180" },
                                            new { key = "state", title = "状态", width = "120" },
                                            new { key = "endpoint", title = "端点", width = "180" },
                                            new { key = "detail", title = "详情", width = "*" },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(description, JsonOptions);
    }

    public static string ActionsJson()
    {
        var actions = new
        {
            schemaVersion = SchemaVersion,
            owner = Owner,
            actions = new object[]
            {
                Action(StartAction, "启动 Sub2API", "juno.sub2api.start", "启动本机 Sub2API 服务。"),
                Action(ProxyAction, "连接代理", "juno.proxy.connect", "执行代理重连脚本。"),
                Action(StopAction, "关闭 Sub2API", "juno.sub2api.stop", "关闭本机 Sub2API 服务。", danger: true),
                Action(
                    RefreshAction,
                    "刷新状态",
                    "aurora.ui.refreshdata",
                    "重新读取账号额度与服务状态。",
                    args: new { page = "sub2api" }),
            },
        };
        return JsonSerializer.Serialize(actions, JsonOptions);
    }

    public static Task<CommandResult> ReadDataAsync(string? view, CancellationToken cancellation)
        => string.Equals(view, AccountsView, StringComparison.OrdinalIgnoreCase)
            ? ReadAccountDataAsync(cancellation)
            : string.IsNullOrWhiteSpace(view) || string.Equals(view, StatusView, StringComparison.OrdinalIgnoreCase)
                ? ReadStatusDataAsync()
                : Task.FromResult(CommandResult.Fail($"未知 Juno 数据视图: {view}"));

    private static async Task<CommandResult> ReadAccountDataAsync(CancellationToken cancellation)
    {
        try
        {
            using var client = Sub2ApiAdminClient.CreateDefault();
            var snapshots = await client.LoadOpenAiOAuthAccountsAsync(cancellation).ConfigureAwait(false);
            var rows = BuildAccountRows(snapshots, DateTimeOffset.Now);
            var json = JsonSerializer.Serialize(rows, JsonOptions);
            return CommandResult.Ok(json, json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Sub2ApiAdminException ex)
        {
            return CommandResult.Fail(ex.Message);
        }
        catch
        {
            return CommandResult.Fail("账号状态读取失败，请检查 Sub2API 管理端和部署配置。");
        }
    }

    private static async Task<CommandResult> ReadStatusDataAsync()
    {
        var rows = new List<Dictionary<string, string>>();
        rows.Add(await ProbeAsync("Sub2API 管理端", 8080, "http://127.0.0.1:8080").ConfigureAwait(false));
        rows.Add(await ProbeAsync("Sub2API 导入页", 9090, "http://127.0.0.1:9090").ConfigureAwait(false));
        rows.Add(await ProbeAsync("本地代理", 7890, "127.0.0.1:7890").ConfigureAwait(false));
        rows.Add(await ProbeAsync("代理转发", 17890, "127.0.0.1:17890").ConfigureAwait(false));

        var json = JsonSerializer.Serialize(rows, JsonOptions);
        return CommandResult.Ok(json, json);
    }

    private static object Action(
        string id,
        string title,
        string command,
        string summary,
        bool danger = false,
        object? args = null)
        => new { id, title, command, args, summary, danger };

    internal static IReadOnlyList<Dictionary<string, string>> BuildAccountRows(
        IReadOnlyList<Sub2ApiAccountSnapshot> snapshots,
        DateTimeOffset now)
        => snapshots.Select(snapshot => new Dictionary<string, string>
        {
            ["account"] = snapshot.Account.Name,
            ["status"] = FormatStatus(snapshot.Account, now),
            ["fiveHour"] = FormatQuota(snapshot.Usage?.FiveHour, snapshot.UsageFailed, now),
            ["sevenDay"] = FormatQuota(snapshot.Usage?.SevenDay, snapshot.UsageFailed, now),
        }).ToArray();

    internal static string FormatStatus(Sub2ApiAccount account, DateTimeOffset now)
    {
        if (account.RateLimitResetAt > now)
            return "限流";
        if (account.OverloadUntil > now)
            return "过载";
        if (string.Equals(account.Status, "error", StringComparison.OrdinalIgnoreCase))
            return "错误";
        if (account.TempUnschedulableUntil > now)
            return "临时不可调度";
        if (!string.Equals(account.Status, "active", StringComparison.OrdinalIgnoreCase))
            return "停用";
        return account.Schedulable ? "正常" : "暂停调度";
    }

    internal static string FormatQuota(
        Sub2ApiUsageWindow? window,
        bool failed,
        DateTimeOffset now)
    {
        if (failed)
            return "获取失败";
        if (window == null)
            return "-";

        var utilization = Math.Round(window.Utilization, MidpointRounding.AwayFromZero);
        var value = $"已用 {utilization:0}%";
        if (window.ResetsAt == null)
            return value;

        var remaining = window.ResetsAt.Value - now;
        if (remaining <= TimeSpan.Zero)
            return $"{value} · 已重置";

        var totalMinutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        var days = totalMinutes / (24 * 60);
        var hours = totalMinutes / 60 % 24;
        var minutes = totalMinutes % 60;
        var countdown = days > 0
            ? $"{days}d{hours}h"
            : hours > 0
                ? $"{hours}h{minutes}m"
                : $"{minutes}m";
        return $"{value} · {countdown} 后重置";
    }

    private static async Task<Dictionary<string, string>> ProbeAsync(
        string service,
        int port,
        string endpoint)
    {
        var open = false;
        try
        {
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            await client.ConnectAsync("127.0.0.1", port, timeout.Token).ConfigureAwait(false);
            open = true;
        }
        catch
        {
            open = false;
        }

        return new Dictionary<string, string>
        {
            ["service"] = service,
            ["state"] = open ? "运行中" : "未连接",
            ["endpoint"] = endpoint,
            ["detail"] = open ? $"端口 {port} 可访问。" : $"端口 {port} 未监听。",
        };
    }
}
