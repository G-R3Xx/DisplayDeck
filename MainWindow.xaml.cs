using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

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
    private bool _hotkeysRegistered;

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
        UpdateHotkeyDisplayText();
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        _hwndSource = HwndSource.FromHwnd(helper.Handle);
        _hwndSource?.AddHook(WndProc);

        RegisterConfiguredHotkeys();
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
        UnregisterConfiguredHotkeys();

        _hwndSource?.RemoveHook(WndProc);

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void RegisterConfiguredHotkeys()
    {
        var helper = new WindowInteropHelper(this);

        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        UnregisterConfiguredHotkeys();

        bool tvHotkeyReady = TryGetVirtualKey(_settings.TvHotkeyKey, out uint tvVirtualKey, out _);
        bool deskHotkeyReady = TryGetVirtualKey(_settings.DeskHotkeyKey, out uint deskVirtualKey, out _);

        if (!tvHotkeyReady || !deskHotkeyReady)
        {
            SetStatus("Hotkeys could not be registered because one or more keys are invalid.");
            return;
        }

        uint tvModifiers = BuildModifierValue(
            _settings.TvHotkeyCtrl,
            _settings.TvHotkeyAlt,
            _settings.TvHotkeyShift,
            _settings.TvHotkeyWin
        );

        uint deskModifiers = BuildModifierValue(
            _settings.DeskHotkeyCtrl,
            _settings.DeskHotkeyAlt,
            _settings.DeskHotkeyShift,
            _settings.DeskHotkeyWin
        );

        bool tvRegistered = RegisterHotKey(
            helper.Handle,
            HOTKEY_ID_TV_MODE,
            tvModifiers | MOD_NOREPEAT,
            tvVirtualKey
        );

        bool deskRegistered = RegisterHotKey(
            helper.Handle,
            HOTKEY_ID_DESK_MODE,
            deskModifiers | MOD_NOREPEAT,
            deskVirtualKey
        );

        _hotkeysRegistered = tvRegistered || deskRegistered;

        UpdateHotkeyDisplayText();

        if (tvRegistered && deskRegistered)
        {
            SetStatus($"Ready. Hotkeys active: {FormatHotkey(_settings.TvHotkeyCtrl, _settings.TvHotkeyAlt, _settings.TvHotkeyShift, _settings.TvHotkeyWin, _settings.TvHotkeyKey)} = TV Gaming Mode, {FormatHotkey(_settings.DeskHotkeyCtrl, _settings.DeskHotkeyAlt, _settings.DeskHotkeyShift, _settings.DeskHotkeyWin, _settings.DeskHotkeyKey)} = Desk Mode.");
        }
        else
        {
            SetStatus("Ready. One or more hotkeys could not be registered. Another app may already be using the same shortcut.");
        }
    }

    private void UnregisterConfiguredHotkeys()
    {
        var helper = new WindowInteropHelper(this);

        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotKey(helper.Handle, HOTKEY_ID_TV_MODE);
        UnregisterHotKey(helper.Handle, HOTKEY_ID_DESK_MODE);

        _hotkeysRegistered = false;
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

            SetStatus($"Detected {_detectedDisplays.Count} active display(s).");
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

        TvHotkeyCtrlCheckBox.IsChecked = _settings.TvHotkeyCtrl;
        TvHotkeyAltCheckBox.IsChecked = _settings.TvHotkeyAlt;
        TvHotkeyShiftCheckBox.IsChecked = _settings.TvHotkeyShift;
        TvHotkeyWinCheckBox.IsChecked = _settings.TvHotkeyWin;
        TvHotkeyKeyBox.Text = _settings.TvHotkeyKey;

        DeskHotkeyCtrlCheckBox.IsChecked = _settings.DeskHotkeyCtrl;
        DeskHotkeyAltCheckBox.IsChecked = _settings.DeskHotkeyAlt;
        DeskHotkeyShiftCheckBox.IsChecked = _settings.DeskHotkeyShift;
        DeskHotkeyWinCheckBox.IsChecked = _settings.DeskHotkeyWin;
        DeskHotkeyKeyBox.Text = _settings.DeskHotkeyKey;
    }

    private void SaveSettingsFromUi()
    {
        if (!TryReadHotkeyEditorValues(out string errorMessage))
        {
            System.Windows.MessageBox.Show(
                errorMessage,
                "Invalid Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            return;
        }

        _settings.LauncherPath = LauncherPathBox.Text.Trim();
        _settings.LauncherProcessName = LauncherProcessNameBox.Text.Trim();

        _settings.LaunchAppOnTvMode = LaunchAppCheckBox.IsChecked == true;
        _settings.CloseAppOnDeskMode = CloseAppCheckBox.IsChecked == true;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;

        ApplyModeBuilderToSettings();
        ApplyStartupSetting();

        _settings.Save();

        _displayModeService = new DisplayModeService(_settings);

        RegisterConfiguredHotkeys();
        UpdateHotkeyDisplayText();

        SetStatus("Settings saved. Hotkeys updated.");
    }

    private bool TryReadHotkeyEditorValues(out string errorMessage)
    {
        errorMessage = "";

        bool tvCtrl = TvHotkeyCtrlCheckBox.IsChecked == true;
        bool tvAlt = TvHotkeyAltCheckBox.IsChecked == true;
        bool tvShift = TvHotkeyShiftCheckBox.IsChecked == true;
        bool tvWin = TvHotkeyWinCheckBox.IsChecked == true;

        bool deskCtrl = DeskHotkeyCtrlCheckBox.IsChecked == true;
        bool deskAlt = DeskHotkeyAltCheckBox.IsChecked == true;
        bool deskShift = DeskHotkeyShiftCheckBox.IsChecked == true;
        bool deskWin = DeskHotkeyWinCheckBox.IsChecked == true;

        string tvKeyRaw = TvHotkeyKeyBox.Text.Trim();
        string deskKeyRaw = DeskHotkeyKeyBox.Text.Trim();

        if (!tvCtrl && !tvAlt && !tvShift && !tvWin)
        {
            errorMessage = "TV Gaming Mode hotkey must include at least one modifier such as Ctrl, Alt, Shift, or Win.";
            return false;
        }

        if (!deskCtrl && !deskAlt && !deskShift && !deskWin)
        {
            errorMessage = "Desk Mode hotkey must include at least one modifier such as Ctrl, Alt, Shift, or Win.";
            return false;
        }

        if (!TryGetVirtualKey(tvKeyRaw, out uint tvVirtualKey, out string tvKeyNormalized))
        {
            errorMessage = "TV Gaming Mode hotkey key is invalid. Try a letter, number, F1-F12, Enter, Space, Escape, Home, End, PageUp, or PageDown.";
            return false;
        }

        if (!TryGetVirtualKey(deskKeyRaw, out uint deskVirtualKey, out string deskKeyNormalized))
        {
            errorMessage = "Desk Mode hotkey key is invalid. Try a letter, number, F1-F12, Enter, Space, Escape, Home, End, PageUp, or PageDown.";
            return false;
        }

        uint tvModifiers = BuildModifierValue(tvCtrl, tvAlt, tvShift, tvWin);
        uint deskModifiers = BuildModifierValue(deskCtrl, deskAlt, deskShift, deskWin);

        if (tvModifiers == deskModifiers && tvVirtualKey == deskVirtualKey)
        {
            errorMessage = "TV Gaming Mode and Desk Mode cannot use the same hotkey.";
            return false;
        }

        _settings.TvHotkeyCtrl = tvCtrl;
        _settings.TvHotkeyAlt = tvAlt;
        _settings.TvHotkeyShift = tvShift;
        _settings.TvHotkeyWin = tvWin;
        _settings.TvHotkeyKey = tvKeyNormalized;

        _settings.DeskHotkeyCtrl = deskCtrl;
        _settings.DeskHotkeyAlt = deskAlt;
        _settings.DeskHotkeyShift = deskShift;
        _settings.DeskHotkeyWin = deskWin;
        _settings.DeskHotkeyKey = deskKeyNormalized;

        return true;
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

    private void UpdateHotkeyDisplayText()
    {
        TvHotkeyDisplayText.Text = FormatHotkey(
            _settings.TvHotkeyCtrl,
            _settings.TvHotkeyAlt,
            _settings.TvHotkeyShift,
            _settings.TvHotkeyWin,
            _settings.TvHotkeyKey
        );

        DeskHotkeyDisplayText.Text = FormatHotkey(
            _settings.DeskHotkeyCtrl,
            _settings.DeskHotkeyAlt,
            _settings.DeskHotkeyShift,
            _settings.DeskHotkeyWin,
            _settings.DeskHotkeyKey
        );
    }

    private static string FormatHotkey(bool ctrl, bool alt, bool shift, bool win, string key)
    {
        var parts = new List<string>();

        if (ctrl)
        {
            parts.Add("Ctrl");
        }

        if (alt)
        {
            parts.Add("Alt");
        }

        if (shift)
        {
            parts.Add("Shift");
        }

        if (win)
        {
            parts.Add("Win");
        }

        parts.Add(NormalizeKeyLabel(key));

        return string.Join(" + ", parts);
    }

    private static uint BuildModifierValue(bool ctrl, bool alt, bool shift, bool win)
    {
        uint value = 0;

        if (ctrl)
        {
            value |= MOD_CONTROL;
        }

        if (alt)
        {
            value |= MOD_ALT;
        }

        if (shift)
        {
            value |= MOD_SHIFT;
        }

        if (win)
        {
            value |= MOD_WIN;
        }

        return value;
    }

    private static bool TryGetVirtualKey(string input, out uint virtualKey, out string normalizedKey)
    {
        virtualKey = 0;
        normalizedKey = "";

        string keyText = input.Trim();

        if (string.IsNullOrWhiteSpace(keyText))
        {
            return false;
        }

        keyText = keyText.Replace(" ", "", StringComparison.OrdinalIgnoreCase);

        if (keyText.Length == 1 && char.IsLetter(keyText[0]))
        {
            keyText = keyText.ToUpperInvariant();
        }
        else if (keyText.Length == 1 && char.IsDigit(keyText[0]))
        {
            keyText = "D" + keyText;
        }
        else
        {
            keyText = NormalizeKeyAlias(keyText);
        }

        if (!Enum.TryParse(keyText, ignoreCase: true, out Key key))
        {
            return false;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);

        if (vk <= 0)
        {
            return false;
        }

        virtualKey = (uint)vk;
        normalizedKey = NormalizeKeyLabel(keyText);

        return true;
    }

    private static string NormalizeKeyAlias(string keyText)
    {
        return keyText.ToUpperInvariant() switch
        {
            "ESC" => "Escape",
            "ENTER" => "Return",
            "RETURN" => "Return",
            "SPACEBAR" => "Space",
            "PGUP" => "PageUp",
            "PAGEUP" => "PageUp",
            "PGDN" => "PageDown",
            "PAGEDOWN" => "PageDown",
            "DEL" => "Delete",
            "INS" => "Insert",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "UP" => "Up",
            "DOWN" => "Down",
            _ => keyText
        };
    }

    private static string NormalizeKeyLabel(string keyText)
    {
        string value = keyText.Trim();

        if (value.StartsWith("D", StringComparison.OrdinalIgnoreCase) &&
            value.Length == 2 &&
            char.IsDigit(value[1]))
        {
            return value[1].ToString();
        }

        if (string.Equals(value, "Return", StringComparison.OrdinalIgnoreCase))
        {
            return "Enter";
        }

        return value.ToUpperInvariant() switch
        {
            "ESCAPE" => "Escape",
            "SPACE" => "Space",
            "PAGEUP" => "PageUp",
            "PAGEDOWN" => "PageDown",
            "DELETE" => "Delete",
            "INSERT" => "Insert",
            "LEFT" => "Left",
            "RIGHT" => "Right",
            "UP" => "Up",
            "DOWN" => "Down",
            _ => value.ToUpperInvariant()
        };
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}