using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DisplayDeck;

public static class WindowsDisplaySwitchService
{
    public static Task ExtendDesktopAsync(int settleDelayMs)
    {
        return RunDisplaySwitchAsync("/extend", settleDelayMs);
    }

    public static Task ExternalOnlyAsync(int settleDelayMs)
    {
        return RunDisplaySwitchAsync("/external", settleDelayMs);
    }

    private static async Task RunDisplaySwitchAsync(string arguments, int settleDelayMs)
    {
        try
        {
            using Process process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = "displayswitch.exe",
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true
            };

            process.Start();

            try
            {
                await process.WaitForExitAsync();
            }
            catch
            {
                // Windows may hand this off to the shell.
            }
        }
        catch
        {
            // Continue with normal display switching logic if DisplaySwitch cannot be launched.
        }

        await Task.Delay(Math.Max(settleDelayMs, 3500));
    }
}
