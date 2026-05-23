using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DisplayDeck;

public static class CommandRunner
{
    public static async Task<int> RunAsync(string fileName, string arguments)
    {
        var tcs = new TaskCompletionSource<int>();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            },
            EnableRaisingEvents = true
        };

        process.Exited += (_, _) =>
        {
            tcs.TrySetResult(process.ExitCode);
            process.Dispose();
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to run command:\n{fileName} {arguments}\n\n{ex.Message}",
                ex
            );
        }

        return await tcs.Task;
    }

    public static void StartProcess(string filePath, string arguments = "")
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = filePath,
            Arguments = arguments,
            UseShellExecute = true
        };

        Process.Start(processStartInfo);
    }

    public static bool IsProcessRunning(string processNameWithoutExe)
    {
        return Process.GetProcessesByName(processNameWithoutExe).Length > 0;
    }

    public static void CloseProcessGracefully(string processNameWithoutExe)
    {
        foreach (var process in Process.GetProcessesByName(processNameWithoutExe))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
                // Ignore and allow force-close fallback.
            }
        }
    }

    public static void KillProcess(string processNameWithoutExe)
    {
        foreach (var process in Process.GetProcessesByName(processNameWithoutExe))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore.
            }
        }
    }
}