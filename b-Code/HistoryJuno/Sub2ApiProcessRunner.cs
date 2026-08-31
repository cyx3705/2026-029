using System.Diagnostics;
using System.Text;

namespace HistoryJuno;

internal sealed record ScriptResult(bool Success, int ExitCode, string Output, string ScriptPath);

internal sealed record ScriptRunOptions(TimeSpan Timeout)
{
    public static ScriptRunOptions Default { get; } = new(TimeSpan.FromMinutes(2));
}

internal static class Sub2ApiProcessRunner
{
    public static async Task<ScriptResult> RunAsync(
        string scriptName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellation,
        ScriptRunOptions? options = null)
    {
        options ??= ScriptRunOptions.Default;
        var root = ToolRoots().FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && Directory.Exists(path));
        if (root == null)
        {
            return new ScriptResult(
                false,
                -1,
                "找不到 Sub2API 工具目录。请设置 SUB2API_TOOL_ROOT，或将工具放在桌面 easyTOOL/SU2API。",
                scriptName);
        }

        var script = Path.Combine(root, scriptName);
        if (!File.Exists(script))
            return new ScriptResult(false, -1, $"脚本不存在: {script}", script);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root,
        };
        psi.ArgumentList.Add("-NoLogo");
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-File");
        psi.ArgumentList.Add(script);
        foreach (var argument in arguments)
            psi.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                return new ScriptResult(false, -1, "无法启动 PowerShell。", script);

            // Existing helper scripts end with a console pause because they are also
            // double-click entry points. The desktop command must close that prompt.
            process.StandardInput.WriteLine();
            process.StandardInput.Close();

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(options.Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                KillProcessTree(process);
                await AwaitOutputAfterExitAsync(process, stdout, stderr).ConfigureAwait(false);
                return new ScriptResult(
                    false,
                    -2,
                    $"脚本执行超过 {FormatTimeout(options.Timeout)}，已终止本次任务。",
                    script);
            }

            var output = new StringBuilder();
            var standardOutput = await stdout.ConfigureAwait(false);
            var standardError = await stderr.ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(standardOutput))
                output.Append(standardOutput.Trim());
            if (!string.IsNullOrWhiteSpace(standardError))
            {
                if (output.Length > 0)
                    output.AppendLine();
                output.Append(standardError.Trim());
            }

            return new ScriptResult(
                process.ExitCode == 0,
                process.ExitCode,
                output.ToString(),
                script);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw;
        }
        catch (Exception ex)
        {
            return new ScriptResult(false, -1, $"{ex.GetType().Name}: {ex.Message}", script);
        }
    }

    private static IEnumerable<string> ToolRoots()
    {
        yield return Environment.GetEnvironmentVariable("SUB2API_TOOL_ROOT") ?? "";
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "easyTOOL",
            "SU2API");
    }

    private static string FormatTimeout(TimeSpan timeout)
        => timeout.TotalSeconds >= 60
            ? $"{timeout.TotalMinutes:0.#} 分钟"
            : $"{timeout.TotalSeconds:0.#} 秒";

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cancellation and timeout cleanup are best effort.
        }
    }

    private static async Task AwaitOutputAfterExitAsync(
        Process process,
        Task<string> stdout,
        Task<string> stderr)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch
        {
            // The timeout result must not be replaced by cleanup failures.
        }
    }
}
