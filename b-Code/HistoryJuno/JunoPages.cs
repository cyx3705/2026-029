using System.Net.Sockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using HistoryVulcan.Core.Commands;

namespace HistoryJuno;

internal static class JunoPages
{
    private const string Owner = "HistoryJuno";
    private const int SchemaVersion = 1;
    private const string StatusView = "status";

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
                                },
                            },
                            new
                            {
                                type = "table",
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
                Action(RefreshAction, "刷新状态", "juno.sub2api.status", "重新探测服务与代理端口。"),
            },
        };
        return JsonSerializer.Serialize(actions, JsonOptions);
    }

    public static async Task<CommandResult> ReadDataAsync()
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
        bool danger = false)
        => new { id, title, command, summary, danger };

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
