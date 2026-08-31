using System.Diagnostics;
using HistoryVulcan.Core.Commands;

namespace HistoryJuno;

internal static class JsonSourcePicker
{
    private const string PickerScript = """
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = [System.Windows.Forms.OpenFileDialog]::new()
        $dialog.Title = '选择账号导入 JSON'
        $dialog.Filter = 'JSON 文件 (*.json)|*.json'
        $dialog.CheckFileExists = $true
        $dialog.Multiselect = $false
        if (Test-Path -LiteralPath $env:JUNO_IMPORT_ROOT) {
          $dialog.InitialDirectory = $env:JUNO_IMPORT_ROOT
        }
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
          [Console]::Out.Write($dialog.FileName)
        }
        """;

    public static async Task<CommandResult> SelectAsync(CancellationToken cancellation)
    {
        var deploymentRoot = Environment.GetEnvironmentVariable("SUB2API_DEPLOY_ROOT");
        if (string.IsNullOrWhiteSpace(deploymentRoot))
        {
            deploymentRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Projects",
                "sub2api-deploy");
        }

        var start = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["JUNO_IMPORT_ROOT"] = deploymentRoot;
        start.ArgumentList.Add("-NoLogo");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-STA");
        start.ArgumentList.Add("-ExecutionPolicy");
        start.ArgumentList.Add("Bypass");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add(PickerScript);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                return CommandResult.Fail("无法打开 JSON 来源选择器。");

            var output = process.StandardOutput.ReadToEndAsync(cancellation);
            var error = process.StandardError.ReadToEndAsync(cancellation);
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
            var path = (await output.ConfigureAwait(false)).Trim();
            _ = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return CommandResult.Fail("JSON 来源选择器执行失败。");
            return path.Length == 0
                ? CommandResult.Fail("未选择 JSON 来源。")
                : CommandResult.Ok(path, path);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        catch
        {
            return CommandResult.Fail("无法打开 JSON 来源选择器。");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cancellation cleanup is best effort.
        }
    }
}
