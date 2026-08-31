using System.Diagnostics;
using System.Text;

namespace HistoryJuno;

internal sealed record StartupResult(bool Success, string Message);

internal static class Sub2ApiStartupRunner
{
    private static readonly TimeSpan DockerServiceTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ComposeTimeout = TimeSpan.FromSeconds(90);

    public static async Task<StartupResult> StartAsync(CancellationToken cancellation)
    {
        var deployRoot = ResolveDeployRoot();
        if (!Directory.Exists(deployRoot))
        {
            return new StartupResult(
                false,
                "找不到 Sub2API 部署目录。请设置 SUB2API_DEPLOY_ROOT，或准备默认部署目录。");
        }

        if (!File.Exists(Path.Combine(deployRoot, "docker-compose.yml")) &&
            !File.Exists(Path.Combine(deployRoot, "compose.yml")) &&
            !File.Exists(Path.Combine(deployRoot, "compose.yaml")))
        {
            return new StartupResult(false, "Sub2API 部署目录中找不到 compose 配置文件。");
        }

        if (!TryConvertToWslPath(deployRoot, out var wslRoot))
        {
            return new StartupResult(
                false,
                "Sub2API 部署目录必须是可映射到 WSL /mnt/<drive> 的本机盘符路径。");
        }

        var docker = await RunWslStepAsync(
                "启动 Ubuntu Docker 服务",
                "sudo -n service docker start",
                DockerServiceTimeout,
                cancellation)
            .ConfigureAwait(false);
        if (!docker.Success)
            return docker;

        var compose = await RunWslStepAsync(
                "启动 Sub2API compose",
                BuildComposeCommand(wslRoot),
                ComposeTimeout,
                cancellation)
            .ConfigureAwait(false);
        return compose.Success
            ? new StartupResult(true, "Sub2API Docker 服务与 compose 容器已启动。")
            : compose;
    }

    internal static string ResolveDeployRoot()
        => Path.GetFullPath(
            Environment.GetEnvironmentVariable("SUB2API_DEPLOY_ROOT") ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Projects",
                "sub2api-deploy"));

    internal static bool TryConvertToWslPath(string path, out string wslPath)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (root == null || root.Length < 2 || root[1] != ':')
        {
            wslPath = "";
            return false;
        }

        var relative = fullPath[root.Length..].Replace('\\', '/');
        wslPath = $"/mnt/{char.ToLowerInvariant(root[0])}/{relative}".TrimEnd('/');
        return true;
    }

    internal static string BuildComposeCommand(string wslRoot)
        => $"cd {QuoteBash(wslRoot)} && docker compose up -d";

    private static string QuoteBash(string value)
        => $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static async Task<StartupResult> RunWslStepAsync(
        string step,
        string linuxCommand,
        TimeSpan timeout,
        CancellationToken cancellation)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wsl.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-d", "Ubuntu", "--", "bash", "-lc", linuxCommand })
            psi.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
                return new StartupResult(false, $"{step}失败：无法启动 WSL。");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeoutCancellation.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                KillProcessTree(process);
                return new StartupResult(
                    false,
                    $"{step}超过 {timeout.TotalSeconds:0} 秒，已终止；请检查 WSL 与 Docker 服务状态。");
            }

            var output = JoinOutput(await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
            return process.ExitCode == 0
                ? new StartupResult(true, output)
                : new StartupResult(
                    false,
                    string.IsNullOrWhiteSpace(output)
                        ? $"{step}失败（退出码 {process.ExitCode}）。"
                        : $"{step}失败（退出码 {process.ExitCode}）：{output}");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }
        catch (Exception ex)
        {
            return new StartupResult(false, $"{step}失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string JoinOutput(string stdout, string stderr)
    {
        var output = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(stdout))
            output.Append(stdout.Trim());
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            if (output.Length > 0)
                output.AppendLine();
            output.Append(stderr.Trim());
        }

        const int maxLength = 2000;
        return output.Length <= maxLength
            ? output.ToString()
            : output.ToString(0, maxLength) + Environment.NewLine + "（输出已截断）";
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Timeout and cancellation cleanup are best effort.
        }
    }
}
