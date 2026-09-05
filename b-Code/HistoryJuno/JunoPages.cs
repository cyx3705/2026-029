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
    internal const string AccountsSection = "账号管理";
    internal const string ServicesSection = "服务状态";
    internal const string AllGroups = "全部分组";
    internal const string AllStatuses = "全部状态";

    public const string StartAction = "juno.sub2api.start";
    public const string ProxyAction = "juno.proxy.connect";
    public const string StopAction = "juno.sub2api.stop";
    public const string RefreshAction = "juno.status.refresh";
    public const string ImportAction = "juno.accounts.import";
    public const string ImportRefreshAction = "juno.import.refresh";

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
            pages = new object[]
            {
                new
                {
                    id = "sub2api",
                    title = "Juno",
                    placement = new { side = "right", visible = true, singleton = true },
                    content = new
                    {
                        type = "stack",
                        orientation = "vertical",
                        gap = "tight",
                        children = new object[]
                        {
                            ServiceControls(),
                            new
                            {
                                type = "switch",
                                id = "juno-sections",
                                source = "{selection.juno.section.value}",
                                children = new object[]
                                {
                                    new
                                    {
                                        type = "stack",
                                        @case = AccountsSection,
                                        orientation = "vertical",
                                        gap = "tight",
                                        children = new object[]
                                        {
                                            AccountFilters(),
                                            AccountTable("juno-accounts", new
                                            {
                                                view = AccountsView,
                                                group = "{selection.juno.account.group.value}",
                                                status = "{selection.juno.account.status.value}",
                                            }),
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
                new
                {
                    id = "juno-import",
                    title = "账号导入",
                    placement = new
                    {
                        side = "tab",
                        tabTarget = "sub2api",
                        visible = true,
                        singleton = true,
                    },
                    content = new
                    {
                        type = "stack",
                        orientation = "vertical",
                        gap = "tight",
                        children = new object[]
                        {
                            ImportControls(),
                            AccountTable("juno-import-accounts", new { view = AccountsView }),
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
                Action(ProxyAction, "代理重连", "juno.proxy.connect", "刷新全局代理转发并同步代理 IP。"),
                Action(StopAction, "关闭 Sub2API", "juno.sub2api.stop", "关闭本机 Sub2API 服务。", danger: true),
                Action(
                    RefreshAction,
                    "刷新",
                    "aurora.ui.refreshdata",
                    "重新读取账号与服务状态。",
                    args: new { page = "sub2api" }),
                Action(
                    ImportRefreshAction,
                    "刷新",
                    "aurora.ui.refreshdata",
                    "重新读取导入页账号和动态分组。",
                    args: new { page = "juno-import" }),
                Action(
                    ImportAction,
                    "导入账号",
                    "juno.accounts.import",
                    "从 JSON 来源导入账号，并绑定所选分组与全局唯一代理。",
                    args: new
                    {
                        source = "{jsonSource}",
                        group = "{selection.juno.import.group.value}",
                    }),
                Action(
                    "juno.import.source",
                    "选择 JSON 来源",
                    "aurora.ui.panelset",
                    "更新导入来源路径，账号导入仍需单独确认。",
                    args: new { panel = "import-controls", control = "jsonSource", value = "{value}" }),
            },
        };
        return JsonSerializer.Serialize(actions, JsonOptions);
    }

    public static Task<CommandResult> ReadDataAsync(
        string? view,
        string? group,
        string? status,
        CancellationToken cancellation)
        => string.Equals(view, AccountsView, StringComparison.OrdinalIgnoreCase)
            ? ReadAccountDataAsync(group, status, cancellation)
            : string.IsNullOrWhiteSpace(view) || string.Equals(view, StatusView, StringComparison.OrdinalIgnoreCase)
                ? ReadStatusDataAsync()
                : Task.FromResult(CommandResult.Fail($"未知 Juno 数据视图: {view}"));

    public static async Task<CommandResult> ReadGroupOptionsAsync(
        string? purpose,
        CancellationToken cancellation)
    {
        try
        {
            using var client = Sub2ApiAdminClient.CreateDefault();
            var groups = await client.LoadGroupsAsync(cancellation).ConfigureAwait(false);
            var values = groups
                .Where(group => string.Equals(group.Status, "active", StringComparison.OrdinalIgnoreCase))
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => new Dictionary<string, string> { ["value"] = GroupOption(group) })
                .ToList();
            if (string.Equals(purpose, "filter", StringComparison.OrdinalIgnoreCase))
                values.Insert(0, new Dictionary<string, string> { ["value"] = AllGroups });
            var json = JsonSerializer.Serialize(values, JsonOptions);
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
            return CommandResult.Fail("分组列表读取失败，请检查 Sub2API 管理端和部署配置。");
        }
    }

    internal static bool TryParseGroupOption(string? value, out long groupId)
    {
        groupId = 0;
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, AllGroups, StringComparison.OrdinalIgnoreCase))
            return false;
        var separator = value.IndexOf('·');
        var id = separator < 0 ? value : value[..separator];
        return long.TryParse(id.Trim(), out groupId) && groupId > 0;
    }

    private static object ServiceControls()
        => new
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
                        new { kind = "button", text = "代理重连", action = ProxyAction },
                        new { kind = "button", text = "关闭 Sub2API", action = StopAction },
                        new { kind = "button", text = "刷新", icon = "refresh-cw", action = RefreshAction },
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
                            label = "视图",
                            mode = "select",
                            channel = "juno.section",
                            options = new[] { AccountsSection, ServicesSection },
                        },
                    },
                },
            },
        };

    private static object AccountFilters()
        => new
        {
            type = "panel",
            id = "account-filters",
            text = "账号检索",
            rows = new object[]
            {
                new
                {
                    mode = "even",
                    widgets = new object[]
                    {
                        new
                        {
                            kind = "textbox",
                            id = "accountGroup",
                            label = "检索分组",
                            mode = "select",
                            value = AllGroups,
                            channel = "juno.account.group",
                            optionsSource = new
                            {
                                command = "juno.ui.groups",
                                args = new { purpose = "filter" },
                            },
                        },
                        new
                        {
                            kind = "textbox",
                            id = "accountStatus",
                            label = "检索状态",
                            mode = "select",
                            value = AllStatuses,
                            channel = "juno.account.status",
                            options = new[]
                            {
                                AllStatuses,
                                "正常",
                                "暂停调度",
                                "停用",
                                "临时不可调度",
                                "错误",
                                "过载",
                                "限流",
                            },
                        },
                    },
                },
            },
        };

    private static object ImportControls()
        => new
        {
            type = "panel",
            id = "import-controls",
            text = "导入控制",
            rows = new object[]
            {
                new
                {
                    mode = "flex",
                    widgets = new object[]
                    {
                        new
                        {
                            kind = "sourcePicker",
                            id = "jsonSource",
                            label = "JSON 来源",
                            selectCommand = "juno.import.select",
                            commitAction = "juno.import.source",
                            minWidth = 260,
                            flex = true,
                        },
                    },
                },
                new
                {
                    mode = "flex",
                    widgets = new object[]
                    {
                        new
                        {
                            kind = "textbox",
                            id = "importGroup",
                            label = "导入分组",
                            mode = "select",
                            channel = "juno.import.group",
                            optionsSource = new
                            {
                                command = "juno.ui.groups",
                                args = new { purpose = "import" },
                            },
                            minWidth = 220,
                            flex = true,
                        },
                        new { kind = "button", text = "导入账号", action = ImportAction },
                    },
                },
                new
                {
                    mode = "even",
                    widgets = new object[]
                    {
                        new { kind = "button", text = "启动 Sub2API", action = StartAction },
                        new { kind = "button", text = "代理重连", action = ProxyAction },
                        new { kind = "button", text = "刷新", icon = "refresh-cw", action = ImportRefreshAction },
                    },
                },
            },
        };

    private static object AccountTable(string id, object args)
        => new
        {
            type = "table",
            id,
            dataSource = new
            {
                command = "juno.ui.data",
                args,
            },
            columns = new object[]
            {
                new { key = "account", title = "账号", width = "180" },
                new { key = "group", title = "分组", width = "140" },
                new { key = "status", title = "状态", width = "100" },
                new { key = "proxy", title = "代理", width = "120" },
                new { key = "fiveHour", title = "5h 额度", width = "180" },
                new { key = "sevenDay", title = "7d 额度", width = "180" },
            },
        };

    private static async Task<CommandResult> ReadAccountDataAsync(
        string? group,
        string? status,
        CancellationToken cancellation)
    {
        try
        {
            using var client = Sub2ApiAdminClient.CreateDefault();
            var snapshots = await client.LoadOpenAiOAuthAccountsAsync(cancellation).ConfigureAwait(false);
            var rows = BuildAccountRows(snapshots, DateTimeOffset.Now, group, status);
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
        DateTimeOffset now,
        string? group = null,
        string? status = null)
    {
        var hasGroupFilter = TryParseGroupOption(group, out var groupId);
        var statusFilter = string.IsNullOrWhiteSpace(status)
            || string.Equals(status, AllStatuses, StringComparison.OrdinalIgnoreCase)
                ? null
                : status.Trim();

        return snapshots
            .Where(snapshot => !hasGroupFilter || snapshot.Account.Groups?.Any(item => item.Id == groupId) == true)
            .Select(snapshot => new Dictionary<string, string>
            {
                ["account"] = snapshot.Account.Name,
                ["group"] = FormatGroups(snapshot.Account.Groups),
                ["status"] = FormatStatus(snapshot.Account, now),
                ["proxy"] = snapshot.Account.Proxy?.Name ?? "-",
                ["fiveHour"] = FormatQuota(snapshot.Usage?.FiveHour, snapshot.UsageFailed, now),
                ["sevenDay"] = FormatQuota(snapshot.Usage?.SevenDay, snapshot.UsageFailed, now),
            })
            .Where(row => statusFilter == null
                || string.Equals(row["status"], statusFilter, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

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

    private static string GroupOption(Sub2ApiGroup group)
        => $"{group.Id} · {group.Name}";

    private static string FormatGroups(IReadOnlyList<Sub2ApiGroup>? groups)
        => groups == null || groups.Count == 0
            ? "-"
            : string.Join("、", groups.Select(group => group.Name));

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
