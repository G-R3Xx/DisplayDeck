using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace DisplayDeck;

public partial class MainWindow : Window
{
    private const string StartupRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupAppName = "DisplayDeck";

    private const int HOTKEY_ID_TV_MODE = 9001;
    private const int HOTKEY_ID_DESK_MODE = 9002;

    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;

    private const uint VK_G = 0x47;
    private const uint VK_D = 0x44;

    private AppSettings _settings;
    private DisplayModeService _displayModeService;
    private readonly NativeDisplayDiscoveryService _displayDiscoveryService = new();

    private List<DisplayInfo> _detectedDisplays = new();
    private List<ModeDisplaySelection> _modeSelections = new();

    private HwndSource? _hwndSource;
    private Forms.NotifyIcon? _trayIcon;

    private bool _isSwitching;
    private bool _isExitRequested;
    private bool _hasShownTrayMessage;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        _displayModeService = new DisplayModeService(_settings);

        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;

        CreateTrayIcon();

        LoadSettingsIntoUi();
        LoadDetectedDisplays();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        bool tvHotkeyRegistered = RegisterHotKey(helper.Handle, HOTKEY_ID_TV_MODE, MOD_CONTROL | MOD_ALT, VK_G);
        bool deskHotkeyRegistered = RegisterHotKey(helper.Handle, HOTKEY_ID_DESK_MODE, MOD_CONTROL | MOD_ALT, VK_D);

        if (tvHotkeyRegistered && deskHotkeyRegistered)
        {
            SetStatus("Ready. Hotkeys active: Ctrl + Alt + G = TV Gaming Mode, Ctrl + Alt + D = Desk Mode.");
        }
        else
        {
            SetStatus("Ready. One or more hotkeys could not be registered. Another app may already be using them.");
        }
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "DisplayDeck",
            Icon = Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildTrayMenu()
        };

        _trayIcon.DoubleClick += (_, _) =>
        {
            ShowMainWindow();
        };
    }

    private Forms.ContextMenuStrip BuildTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();

        var openItem = new Forms.ToolStripMenuItem("Open DisplayDeck");
        openItem.Click += (_, _) => ShowMainWindow();

        var tvModeItem = new Forms.ToolStripMenuItem("Switch to TV Gaming Mode");
        tvModeItem.Click += async (_, _) => await SwitchToTvModeFromHotkeyAsync();

        var deskModeItem = new Forms.ToolStripMenuItem("Switch to Desk Mode");
        deskModeItem.Click += async (_, _) => await SwitchToDeskModeFromHotkeyAsync();

        var exitItem = new Forms.ToolStripMenuItem("Exit DisplayDeck");
        exitItem.Click += (_, _) =>
        {
            _isExitRequested = true;
            Close();
        };

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(tvModeItem);
        menu.Items.Add(deskModeItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        return menu;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExitRequested)
        {
            return;
        }

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        Hide();

        if (_trayIcon is not null && !_hasShownTrayMessage)
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "DisplayDeck is still running",
                "Use the tray icon or hotkeys to switch modes. Right-click the tray icon to exit.",
                Forms.ToolTipIcon.Info
            );

            _hasShownTrayMessage = true;
        }
    }

    private void ShowMainWindow()
    {
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);

        UnregisterHotKey(helper.Handle, HOTKEY_ID_TV_MODE);
        UnregisterHotKey(helper.Handle, HOTKEY_ID_DESK_MODE);

        _hwndSource?.RemoveHook(WndProc);

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();

            if (hotkeyId == HOTKEY_ID_TV_MODE)
            {
                handled = true;
                _ = SwitchToTvModeFromHotkeyAsync();
            }
            else if (hotkeyId == HOTKEY_ID_DESK_MODE)
            {
                handled = true;
                _ = SwitchToDeskModeFromHotkeyAsync();
            }
        }

        return IntPtr.Zero;
    }

    private async Task SwitchToTvModeFromHotkeyAsync()
    {
        if (_isSwitching)
        {
            return;
        }

        await RunWithUiLockAsync("Hotkey: Switching to TV Gaming Mode...", async () =>
        {
            await _displayModeService.SwitchToTvGamingModeAsync();
            LoadDetectedDisplays();
            SetStatus("TV Gaming Mode activated by hotkey.");
        });
    }

    private async Task SwitchToDeskModeFromHotkeyAsync()
    {
        if (_isSwitching)
        {
            return;
        }

        await RunWithUiLockAsync("Hotkey: Switching to Desk Mode...", async () =>
        {
            await _displayModeService.SwitchToDeskModeAsync();
            LoadDetectedDisplays();
            SetStatus("Desk Mode activated by hotkey.");
        });
    }

    private async void TvModeButton_Click(object sender, RoutedEventArgs e)
    {
        await RunWithUiLockAsync("Switching to TV Gaming Mode...", async () =>
        {
            await _displayModeService.SwitchToTvGamingModeAsync();
            LoadDetectedDisplays();
            SetStatus("TV Gaming Mode activated.");
        });
    }

    private async void DeskModeButton_Click(object sender, RoutedEventArgs e)
    {
        await RunWithUiLockAsync("Switching to Desk Mode...", async () =>
        {
            await _displayModeService.SwitchToDeskModeAsync();
            LoadDetectedDisplays();
            SetStatus("Desk Mode activated.");
        });
    }

    private void RefreshDisplaysButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDetectedDisplays();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSettingsFromUi();
        SetStatus("Settings saved. Hotkeys: Ctrl + Alt + G = TV Gaming Mode, Ctrl + Alt + D = Desk Mode.");
    }

    private void BrowseLauncher_Click(object sender, RoutedEventArgs e)
    {
        string? path = BrowseForFile("Executable files (*.exe)|*.exe|All files (*.*)|*.*");

        if (!string.IsNullOrWhiteSpace(path))
        {
            LauncherPathBox.Text = path;

            if (string.IsNullOrWhiteSpace(LauncherProcessNameBox.Text))
            {
                LauncherProcessNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(path);
            }
        }
    }

    private static string? BrowseForFile(string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = filter,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void LoadDetectedDisplays()
    {
        try
        {
            var allDisplays = _displayDiscoveryService.GetDisplays().ToList();

            _detectedDisplays = allDisplays
                .Where(display => display.IsActive)
                .ToList();

            DisplaysDataGrid.ItemsSource = null;
            DisplaysDataGrid.ItemsSource = _detectedDisplays;

            BuildModeSelections();

            SetStatus($"Detected {_detectedDisplays.Count} active display(s). Hotkeys: Ctrl + Alt + G / Ctrl + Alt + D.");
        }
        catch (Exception ex)
        {
            SetStatus("Display discovery error: " + ex.Message);
        }
    }

    private void BuildModeSelections()
    {
        _modeSelections = _detectedDisplays
            .Select(display => new ModeDisplaySelection
            {
                DisplayName = display.DisplayName,
                MonitorName = display.MonitorName,
                ResolutionText = display.ResolutionText,

                DeskEnabled = _settings.DeskMode.EnabledDisplays.Contains(display.DisplayName),
                DeskPrimary = string.Equals(_settings.DeskMode.PrimaryDisplayName, display.DisplayName, StringComparison.OrdinalIgnoreCase),

                TvEnabled = _settings.TvGamingMode.EnabledDisplays.Contains(display.DisplayName),
                TvPrimary = string.Equals(_settings.TvGamingMode.PrimaryDisplayName, display.DisplayName, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        ModeBuilderDataGrid.ItemsSource = null;
        ModeBuilderDataGrid.ItemsSource = _modeSelections;
    }

    private void LoadSettingsIntoUi()
    {
        LauncherPathBox.Text = _settings.LauncherPath;
        LauncherProcessNameBox.Text = _settings.LauncherProcessName;

        LaunchAppCheckBox.IsChecked = _settings.LaunchAppOnTvMode;
        CloseAppCheckBox.IsChecked = _settings.CloseAppOnDeskMode;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
    }

    private void SaveSettingsFromUi()
    {
        _settings.LauncherPath = LauncherPathBox.Text.Trim();
        _settings.LauncherProcessName = LauncherProcessNameBox.Text.Trim();

        _settings.LaunchAppOnTvMode = LaunchAppCheckBox.IsChecked == true;
        _settings.CloseAppOnDeskMode = CloseAppCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;

        ApplyModeBuilderToSettings();
        ApplyStartupSetting();

        _settings.Save();

        _displayModeService = new DisplayModeService(_settings);
    }

    private void ApplyStartupSetting()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                StartupRegistryKeyPath,
                writable: true
            );

            if (key is null)
            {
                throw new InvalidOperationException("Could not open the Windows startup registry key.");
            }

            if (_settings.StartWithWindows)
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

                if (string.IsNullOrWhiteSpace(exePath))
                {
                    throw new InvalidOperationException("Could not determine the DisplayDeck executable path.");
                }

                key.SetValue(StartupAppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(StartupAppName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "DisplayDeck could not update the Start with Windows setting.\n\n" + ex.Message,
                "DisplayDeck Startup Setting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            _settings.StartWithWindows = false;
            StartWithWindowsCheckBox.IsChecked = false;
        }
    }

    private void ApplyModeBuilderToSettings()
    {
        var deskEnabled = _modeSelections
            .Where(x => x.DeskEnabled)
            .Select(x => x.DisplayName)
            .ToList();

        var deskDisabled = _modeSelections
            .Where(x => !x.DeskEnabled)
            .Select(x => x.DisplayName)
            .ToList();

        var tvEnabled = _modeSelections
            .Where(x => x.TvEnabled)
            .Select(x => x.DisplayName)
            .ToList();

        var tvDisabled = _modeSelections
            .Where(x => !x.TvEnabled)
            .Select(x => x.DisplayName)
            .ToList();

        string deskPrimary = _modeSelections.FirstOrDefault(x => x.DeskPrimary)?.DisplayName ?? "";
        string tvPrimary = _modeSelections.FirstOrDefault(x => x.TvPrimary)?.DisplayName ?? "";

        if (string.IsNullOrWhiteSpace(deskPrimary) && deskEnabled.Count > 0)
        {
            deskPrimary = deskEnabled[0];
        }

        if (string.IsNullOrWhiteSpace(tvPrimary) && tvEnabled.Count > 0)
        {
            tvPrimary = tvEnabled[0];
        }

        _settings.DeskMode = new DisplayModeProfile
        {
            Name = "Desk Mode",
            PrimaryDisplayName = deskPrimary,
            EnabledDisplays = deskEnabled,
            DisabledDisplays = deskDisabled
        };

        _settings.TvGamingMode = new DisplayModeProfile
        {
            Name = "TV Gaming Mode",
            PrimaryDisplayName = tvPrimary,
            EnabledDisplays = tvEnabled,
            DisabledDisplays = tvDisabled
        };

        _settings.MainDisplayName = deskPrimary;
        _settings.TvDisplayName = tvPrimary;

        string? secondary = deskEnabled
            .FirstOrDefault(x => !string.Equals(x, deskPrimary, StringComparison.OrdinalIgnoreCase));

        _settings.SecondaryDisplayName = secondary ?? "";
    }

    private async Task RunWithUiLockAsync(string startingStatus, Func<Task> action)
    {
        if (_isSwitching)
        {
            return;
        }

        try
        {
            _isSwitching = true;

            SetStatus(startingStatus);
            SetButtonsEnabled(false);

            await action();
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
            System.Windows.MessageBox.Show(
                ex.Message,
                "DisplayDeck Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            SetButtonsEnabled(true);
            _isSwitching = false;
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        TvModeButton.IsEnabled = enabled;
        DeskModeButton.IsEnabled = enabled;
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}