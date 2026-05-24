using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DisplayDeck;

public sealed class DisplayModeService
{
    private readonly AppSettings _settings;
    private readonly NativeDisplaySwitchingService _nativeDisplaySwitchingService = new();

    private const int WM_CLOSE = 0x0010;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

    public DisplayModeService(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task SwitchToProfileAsync(DisplayModeProfile profile)
    {
        await _nativeDisplaySwitchingService.ApplyProfileAsync(
            profile,
            _settings.CommandDelayMs
        );

        await Task.Delay(1000);

        if (profile.CloseLauncherAfterSwitch)
        {
            CloseConfiguredApp(profile);
        }

        if (profile.LaunchAppAfterSwitch)
        {
            LaunchConfiguredApp(profile);
        }
    }

    private static void LaunchConfiguredApp(DisplayModeProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.LauncherPath))
        {
            return;
        }

        if (!File.Exists(profile.LauncherPath))
        {
            throw new FileNotFoundException("Launcher app was not found.", profile.LauncherPath);
        }

        string processName = GetLauncherProcessName(profile);

        if (IsProcessRunning(processName))
        {
            return;
        }

        CommandRunner.StartProcess(profile.LauncherPath);
    }

    private static void CloseConfiguredApp(DisplayModeProfile profile)
    {
        var possibleNames = GetPossibleLauncherProcessNames(profile);

        CloseMatchingWindowsByMessage(possibleNames);

        Task.Delay(2000).Wait();

        if (!AnyMatchingProcessStillRunning(possibleNames))
        {
            return;
        }

        CloseMatchingProcessesGracefully(possibleNames);

        Task.Delay(2000).Wait();

        if (!AnyMatchingProcessStillRunning(possibleNames))
        {
            return;
        }

        ForceKillByProcessNames(possibleNames);

        Task.Delay(1000).Wait();

        KillMatchingProcessesDotNet(possibleNames);
    }

    private static string GetLauncherProcessName(DisplayModeProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.LauncherProcessName))
        {
            return CleanProcessName(profile.LauncherProcessName);
        }

        if (!string.IsNullOrWhiteSpace(profile.LauncherPath))
        {
            return Path.GetFileNameWithoutExtension(profile.LauncherPath);
        }

        return "";
    }

    private static List<string> GetPossibleLauncherProcessNames(DisplayModeProfile profile)
    {
        var names = new List<string>();

        string configuredName = GetLauncherProcessName(profile);

        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            names.Add(configuredName);
        }

        if (!string.IsNullOrWhiteSpace(profile.LauncherPath))
        {
            string pathName = Path.GetFileNameWithoutExtension(profile.LauncherPath);

            if (!string.IsNullOrWhiteSpace(pathName))
            {
                names.Add(pathName);
            }
        }

        names.Add("Winhanced");
        names.Add("Winhance");

        return names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(CleanProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanProcessName(string value)
    {
        return value
            .Replace(".exe", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool IsProcessRunning(string processNameWithoutExe)
    {
        if (string.IsNullOrWhiteSpace(processNameWithoutExe))
        {
            return false;
        }

        return Process.GetProcessesByName(processNameWithoutExe).Length > 0;
    }

    private static bool AnyMatchingProcessStillRunning(List<string> possibleNames)
    {
        return GetMatchingProcesses(possibleNames).Any();
    }

    private static void CloseMatchingWindowsByMessage(List<string> possibleNames)
    {
        var matchingWindows = new List<IntPtr>();

        EnumWindows((windowHandle, _) =>
        {
            if (windowHandle == IntPtr.Zero)
            {
                return true;
            }

            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out uint processId);

            if (processId == 0)
            {
                return true;
            }

            string windowTitle = GetWindowTitle(windowHandle);

            try
            {
                using Process process = Process.GetProcessById((int)processId);

                string processName = process.ProcessName ?? "";
                string processPath = "";

                try
                {
                    processPath = process.MainModule?.FileName ?? "";
                }
                catch
                {
                    processPath = "";
                }

                bool isMatch = possibleNames.Any(name =>
                    ContainsIgnoreCase(processName, name) ||
                    ContainsIgnoreCase(windowTitle, name) ||
                    ContainsIgnoreCase(processPath, name)
                );

                if (isMatch)
                {
                    matchingWindows.Add(windowHandle);
                }
            }
            catch
            {
                // Ignore inaccessible/system processes.
            }

            return true;
        }, IntPtr.Zero);

        foreach (IntPtr windowHandle in matchingWindows.Distinct())
        {
            try
            {
                PostMessage(windowHandle, WM_SYSCOMMAND, new IntPtr(SC_CLOSE), IntPtr.Zero);
                PostMessage(windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                // Ignore and allow other close methods to continue.
            }
        }
    }

    private static void CloseMatchingProcessesGracefully(List<string> possibleNames)
    {
        foreach (Process process in GetMatchingProcesses(possibleNames))
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
                // Ignore and continue to force-close fallback.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void ForceKillByProcessNames(List<string> possibleNames)
    {
        foreach (string processNameWithoutExe in possibleNames)
        {
            if (string.IsNullOrWhiteSpace(processNameWithoutExe))
            {
                continue;
            }

            string exeName = processNameWithoutExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processNameWithoutExe
                : processNameWithoutExe + ".exe";

            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/f /t /im \"{exeName}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(processStartInfo);
                process?.WaitForExit(3000);
            }
            catch
            {
                // Ignore and continue to .NET kill fallback.
            }
        }
    }

    private static void KillMatchingProcessesDotNet(List<string> possibleNames)
    {
        foreach (Process process in GetMatchingProcesses(possibleNames))
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                // Ignore. If it still will not close, it may be running elevated.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static List<Process> GetMatchingProcesses(List<string> possibleNames)
    {
        var matches = new List<Process>();

        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.HasExited)
                {
                    process.Dispose();
                    continue;
                }

                string processName = process.ProcessName ?? "";
                string mainWindowTitle = process.MainWindowTitle ?? "";
                string processPath = "";

                try
                {
                    processPath = process.MainModule?.FileName ?? "";
                }
                catch
                {
                    processPath = "";
                }

                bool isMatch = possibleNames.Any(name =>
                    ContainsIgnoreCase(processName, name) ||
                    ContainsIgnoreCase(mainWindowTitle, name) ||
                    ContainsIgnoreCase(processPath, name)
                );

                if (isMatch)
                {
                    matches.Add(process);
                }
                else
                {
                    process.Dispose();
                }
            }
            catch
            {
                process.Dispose();
            }
        }

        return matches;
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        int length = GetWindowTextLength(windowHandle);

        if (length <= 0)
        {
            return "";
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(windowHandle, builder, builder.Capacity);

        return builder.ToString();
    }

    private static bool ContainsIgnoreCase(string value, string search)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(search))
        {
            return false;
        }

        return value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}