using HistoryVulcan.Core.Commands;
using System.Net.Sockets;
using System.Text.Json;

namespace HistoryJuno;

internal static class JunoCommandCatalog
{
    private const string Domain = "juno";
    private const string Owner = "HistoryJuno";
    private static int startInProgress;
    private static int importInProgress;

    public static void Register(CommandRegistry registry, CommandBus bus)
    {
        registry.Register(Internal(
            "juno.ui.describe",
            "返回 Sub2API 控制面板页面描述。",
            _ => CommandResult.Ok(JunoPages.DescribeJson(), JunoPages.DescribeJson())));

        registry.Register(Internal(
            "juno.ui.actions",
            "返回 Sub2API 控制面板动作声明。",
            _ => CommandResult.Ok(JunoPages.ActionsJson(), JunoPages.ActionsJson())));

        registry.Register(new CommandDescriptor
        {
            Name = "juno.ui.data",
            Domain = Domain,
            CommandClass = "ui",
            Summary = "返回 Sub2API 控制面板账号或服务状态行。",
            Readonly = true,
            HiddenReason = "界面内部协议，对模型无意义",
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "view",
                    Description = "取数视图：accounts / status。",
                    Required = false,
                    Position = 0,
                },
                new ParameterSpec
                {
                    Name = "group",
                    Description = "账号管理分组筛选值。",
                    Required = false,
                },
                new ParameterSpec
                {
                    Name = "status",
                    Description = "账号管理状态筛选值。",
                    Required = false,
                },
            ],
            Handler = context => JunoPages.ReadDataAsync(
                context.GetString("view"),
                context.GetString("group"),
                context.GetString("status"),
                context.Cancellation),
        });

        registry.Register(new CommandDescriptor
        {
            Name = "juno.ui.groups",
            Domain = Domain,
            CommandClass = "ui",
            Summary = "返回 Juno 分组选择候选。",
            Readonly = true,
            HiddenReason = "界面内部协议，对模型无意义",
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "purpose",
                    Description = "候选用途：filter / import。",
                    Required = false,
                    Position = 0,
                },
            ],
            Handler = context => JunoPages.ReadGroupOptionsAsync(
                context.GetString("purpose"),
                context.Cancellation),
        });

        registry.Register(new CommandDescriptor
        {
            Name = "juno.import.select",
            Domain = Domain,
            CommandClass = "import",
            Summary = "为 Juno 账号导入页选择 JSON 来源。",
            Level = CommandLevel.Run,
            HiddenReason = "界面内部文件选择动作，对模型无意义",
            Handler = context => JsonSourcePicker.SelectAsync(context.Cancellation),
        });

        registry.Register(new CommandDescriptor
        {
            Name = "juno.accounts.import",
            Domain = Domain,
            CommandClass = "accounts",
            Summary = "导入账号，并设置所选分组和全局唯一代理。",
            Level = CommandLevel.Ask,
            HiddenReason = "导入来源包含本机账号凭据，只允许 Juno 页面调用",
            Parameters =
            [
                new ParameterSpec
                {
                    Name = "source",
                    Description = "本机 JSON 来源文件。",
                    Required = true,
                    Position = 0,
                },
                new ParameterSpec
                {
                    Name = "group",
                    Description = "导入分组候选值。",
                    Required = true,
                    Position = 1,
                },
            ],
            ConfirmPrompt = context => $"确认导入账号并绑定分组 {context.GetString("group")} 与全局代理？",
            Handler = ImportAccountsAsync,
        });

        registry.Register(new CommandDescriptor
        {
            Name = "juno.sub2api.start",
            Domain = Domain,
            CommandClass = "sub2api",
            Summary = "启动 Sub2API 服务。",
            Level = CommandLevel.Run,
            Handler = StartSub2ApiAsync,
        });

        registry.Register(new CommandDescriptor
        {
            Name = "juno.proxy.connect",
            Domain = Domain,
            CommandClass = "proxy",
            Summary = "连接本机代理并刷新代理转发状态。",
            Level = CommandLevel.Run,
            Handler = async context => await RunScriptAsync("proxy-reconnect.ps1", null, context).ConfigureAwait(false),
        });

        registry.Register(new CommandDescriptor
        {
            Name = "juno.sub2api.stop",
            Domain = Domain,
            CommandClass = "sub2api",
            Summary = "关闭 Sub2API 服务。",
            Level = CommandLevel.Ask,
            ConfirmPrompt = _ => "确认关闭 Sub2API 服务？",
            Handler = async context => await RunScriptAsync("sub2api-tool.ps1", "stop", context).ConfigureAwait(false),
        });

        registry.Register(new CommandDescriptor
        {
            Name = "juno.sub2api.status",
            Domain = Domain,
            CommandClass = "sub2api",
            Summary = "检查 Sub2API 与本机代理端口状态。",
            Readonly = true,
            Handler = context => JunoPages.ReadDataAsync(
                JunoPages.StatusView,
                null,
                null,
                context.Cancellation),
        });
    }

    private static CommandDescriptor Internal(
        string name,
        string summary,
        Func<CommandContext, CommandResult> handler)
        => new()
        {
            Name = name,
            Domain = Domain,
            CommandClass = "ui",
            Summary = summary,
            Readonly = true,
            HiddenReason = "界面内部协议，对模型无意义",
            Handler = CommandDescriptor.Sync(handler),
        };

    private static async Task<CommandResult> RunScriptAsync(
        string script,
        string? argument,
        CommandContext context)
    {
        var args = argument == null ? Array.Empty<string>() : new[] { argument };
        var result = await Sub2ApiProcessRunner.RunAsync(script, args, context.Cancellation)
            .ConfigureAwait(false);
        var message = result.Success
            ? $"{script} 执行完成。"
            : $"{script} 执行失败（退出码 {result.ExitCode}）。";
        if (!string.IsNullOrWhiteSpace(result.Output))
            message += Environment.NewLine + result.Output;
        return result.Success ? CommandResult.Ok(message) : CommandResult.Fail(message);
    }

    private static async Task<CommandResult> StartSub2ApiAsync(CommandContext context)
    {
        if (Interlocked.CompareExchange(ref startInProgress, 1, 0) != 0)
            return CommandResult.Fail("Sub2API 正在启动，请勿重复提交；稍后刷新状态即可。");

        try
        {
            if (await WaitForPortsAsync([8080, 9090], TimeSpan.FromMilliseconds(600), context.Cancellation)
                    .ConfigureAwait(false))
            {
                return CommandResult.Ok("Sub2API 已在运行，管理端 8080 与导入页 9090 均已监听。");
            }

            var result = await Sub2ApiStartupRunner.StartAsync(context.Cancellation)
                .ConfigureAwait(false);
            if (!result.Success)
                return CommandResult.Fail(result.Message);

            var ready = await WaitForPortsAsync([8080, 9090], TimeSpan.FromSeconds(20), context.Cancellation)
                .ConfigureAwait(false);
            if (!ready)
            {
                return CommandResult.Fail(
                    "启动脚本已结束，但 Sub2API 端口 8080/9090 未就绪。请检查 WSL、Docker 与 compose 容器状态。");
            }

            return CommandResult.Ok("Sub2API 启动完成，管理端 8080 与导入页 9090 已监听。");
        }
        finally
        {
            Volatile.Write(ref startInProgress, 0);
        }
    }

    private static async Task<CommandResult> ImportAccountsAsync(CommandContext context)
    {
        if (Interlocked.CompareExchange(ref importInProgress, 1, 0) != 0)
            return CommandResult.Fail("账号正在导入，请勿重复提交；完成后刷新即可。");

        try
        {
            var source = context.GetString("source")?.Trim();
            if (string.IsNullOrWhiteSpace(source))
                return CommandResult.Fail("请选择 JSON 来源。");
            if (!JunoPages.TryParseGroupOption(context.GetString("group"), out var groupId))
                return CommandResult.Fail("请选择有效的导入分组。");

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(source);
            }
            catch
            {
                return CommandResult.Fail("JSON 来源路径无效。");
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                return CommandResult.Fail("JSON 来源必须是 .json 文件。");
            if (!File.Exists(fullPath))
                return CommandResult.Fail("找不到 JSON 来源文件。");
            if (new FileInfo(fullPath).Length > 16 * 1024 * 1024)
                return CommandResult.Fail("JSON 来源超过 16 MB，已拒绝导入。");

            JsonDocument document;
            try
            {
                await using var stream = File.OpenRead(fullPath);
                document = await JsonDocument.ParseAsync(stream, cancellationToken: context.Cancellation)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                return CommandResult.Fail("JSON 来源格式无效。");
            }
            catch (IOException)
            {
                return CommandResult.Fail("无法读取 JSON 来源文件。");
            }
            catch (UnauthorizedAccessException)
            {
                return CommandResult.Fail("没有权限读取 JSON 来源文件。");
            }

            using (document)
            using (var client = Sub2ApiAdminClient.CreateDefault())
            {
                var outcome = await client.ImportAccountsAsync(
                        document.RootElement,
                        groupId,
                        context.Cancellation)
                    .ConfigureAwait(false);
                var message = $"导入完成：新建 {outcome.Created} 个，自动配置 {outcome.Configured} 个，"
                    + $"分组 {outcome.GroupId}，全局代理 {outcome.ProxyId}。";
                if (outcome.Failed > 0)
                    return CommandResult.Fail(message + $"另有 {outcome.Failed} 个导入失败。");
                return CommandResult.Ok(message);
            }
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
            return CommandResult.Fail("账号导入失败，请检查 Sub2API 管理端、分组和代理状态。");
        }
        finally
        {
            Volatile.Write(ref importInProgress, 0);
        }
    }

    private static CommandResult ScriptFailure(string script, ScriptResult result)
    {
        var message = $"{script} 执行失败（退出码 {result.ExitCode}）。";
        if (!string.IsNullOrWhiteSpace(result.Output))
            message += Environment.NewLine + result.Output;
        return CommandResult.Fail(message);
    }

    internal static async Task<bool> WaitForPortsAsync(
        IReadOnlyList<int> ports,
        TimeSpan timeout,
        CancellationToken cancellation)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var states = await Task.WhenAll(ports.Select(port => CanConnectAsync(port, cancellation)))
                .ConfigureAwait(false);
            if (states.All(open => open))
                return true;
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation).ConfigureAwait(false);
        }

        return false;
    }

    private static async Task<bool> CanConnectAsync(int port, CancellationToken cancellation)
    {
        try
        {
            using var client = new TcpClient();
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            probe.CancelAfter(TimeSpan.FromMilliseconds(350));
            await client.ConnectAsync("127.0.0.1", port, probe.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
