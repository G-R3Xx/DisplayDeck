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

    private const int HOTKEY_ID_BASE = 9100;
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
    private List<ProfileDisplaySelection> _profileDisplaySelections = new();

    private HwndSource? _hwndSource;
    private Forms.NotifyIcon? _trayIcon;

    private readonly Dictionary<int, string> _hotkeyProfileMap = new();

    private bool _isSwitching;
    private bool _isExitRequested;
    private bool _hasShownTrayMessage;
    private bool _isLoadingProfile;

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
        LoadProfilesIntoUi();
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

        menu.Items.Add(openItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        foreach (DisplayModeProfile profile in _settings.Profiles)
        {
            var profileItem = new Forms.ToolStripMenuItem("Switch to " + profile.Name);
            string profileId = profile.Id;
            profileItem.Click += async (_, _) => await SwitchToProfileByIdAsync(profileId);
            menu.Items.Add(profileItem);
        }

        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = new Forms.ToolStripMenuItem("Exit DisplayDeck");
        exitItem.Click += (_, _) =>
        {
            _isExitRequested = true;
            Close();
        };

        menu.Items.Add(exitItem);

        return menu;
    }

    private void RefreshTrayMenu()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.ContextMenuStrip = BuildTrayMenu();
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
                "Use the tray icon or hotkeys to switch profiles. Right-click the tray icon to exit.",
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

    private void LoadSettingsIntoUi()
    {
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
    }

    private void LoadProfilesIntoUi()
    {
        ProfilesListBox.ItemsSource = null;
        ProfilesListBox.ItemsSource = _settings.Profiles;

        DisplayModeProfile? selectedProfile = GetSelectedProfileFromSettings();

        if (selectedProfile is not null)
        {
            ProfilesListBox.SelectedItem = selectedProfile;
        }
        else if (_settings.Profiles.Count > 0)
        {
            ProfilesListBox.SelectedIndex = 0;
        }

        LoadSelectedProfileIntoEditor();
        UpdateSelectedProfileHeader();
    }

    private DisplayModeProfile? GetSelectedProfileFromSettings()
    {
        return _settings.Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _settings.SelectedProfileId, StringComparison.OrdinalIgnoreCase)
        );
    }

    private DisplayModeProfile? GetSelectedProfile()
    {
        return ProfilesListBox.SelectedItem as DisplayModeProfile;
    }

    private void ProfilesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isLoadingProfile)
        {
            return;
        }

        DisplayModeProfile? selectedProfile = GetSelectedProfile();

        if (selectedProfile is null)
        {
            return;
        }

        _settings.SelectedProfileId = selectedProfile.Id;

        LoadSelectedProfileIntoEditor();
        UpdateSelectedProfileHeader();
    }

    private void LoadSelectedProfileIntoEditor()
    {
        DisplayModeProfile? profile = GetSelectedProfile();

        if (profile is null)
        {
            return;
        }

        _isLoadingProfile = true;

        SelectedProfileNameBox.Text = profile.Name;

        LauncherPathBox.Text = profile.LauncherPath;
        LauncherProcessNameBox.Text = profile.LauncherProcessName;

        HotkeyCtrlCheckBox.IsChecked = profile.HotkeyCtrl;
        HotkeyAltCheckBox.IsChecked = profile.HotkeyAlt;
        HotkeyShiftCheckBox.IsChecked = profile.HotkeyShift;
        HotkeyWinCheckBox.IsChecked = profile.HotkeyWin;
        HotkeyKeyBox.Text = profile.HotkeyKey;

        LaunchAppAfterSwitchCheckBox.IsChecked = profile.LaunchAppAfterSwitch;
        CloseLauncherAfterSwitchCheckBox.IsChecked = profile.CloseLauncherAfterSwitch;
        StartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;

        BuildProfileDisplaySelections(profile);

        _isLoadingProfile = false;
    }

   private void UpdateSelectedProfileHeader()
{
    DisplayModeProfile? profile = GetSelectedProfile();

    if (profile is null)
    {
        SwitchSelectedProfileButtonTitle.Text = "Switch Selected Profile";
        SwitchSelectedProfileButtonSubtitle.Text = "Applies the selected display layout";
        SelectedProfileHotkeyDisplayText.Text = "";
        ProfileDisplaySetupEditingText.Text = "Currently editing: No profile selected";
        return;
    }

    SwitchSelectedProfileButtonTitle.Text = "Switch to " + profile.Name;
    SwitchSelectedProfileButtonSubtitle.Text = "Applies this display profile";

    SelectedProfileHotkeyDisplayText.Text = string.IsNullOrWhiteSpace(profile.HotkeyKey)
        ? "No hotkey set"
        : FormatHotkey(profile.HotkeyCtrl, profile.HotkeyAlt, profile.HotkeyShift, profile.HotkeyWin, profile.HotkeyKey);

    ProfileDisplaySetupEditingText.Text = "Currently editing: " + profile.Name;
}

    private void BuildProfileDisplaySelections(DisplayModeProfile profile)
    {
        _profileDisplaySelections = _detectedDisplays
            .Select(display => new ProfileDisplaySelection
            {
                DisplayName = display.DisplayName,
                MonitorName = display.MonitorName,
                ResolutionText = display.ResolutionText,
                Enabled = profile.EnabledDisplays.Contains(display.DisplayName),
                Primary = string.Equals(profile.PrimaryDisplayName, display.DisplayName, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        ProfileDisplaysItemsControl.ItemsSource = null;
        ProfileDisplaysItemsControl.ItemsSource = _profileDisplaySelections;
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

            DisplayModeProfile? profile = GetSelectedProfile();

            if (profile is not null)
            {
                BuildProfileDisplaySelections(profile);
            }

            SetStatus($"Detected {_detectedDisplays.Count} active display(s).");
        }
        catch (Exception ex)
        {
            SetStatus("Display discovery error: " + ex.Message);
        }
    }

    private void AddProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = new DisplayModeProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "New Profile",
            LauncherPath = "",
            LauncherProcessName = "",
            LaunchAppAfterSwitch = false,
            CloseLauncherAfterSwitch = false,
            HotkeyCtrl = true,
            HotkeyAlt = true,
            HotkeyKey = ""
        };

        if (_detectedDisplays.Count > 0)
        {
            string firstDisplay = _detectedDisplays[0].DisplayName;

            profile.PrimaryDisplayName = firstDisplay;
            profile.EnabledDisplays = new List<string> { firstDisplay };
            profile.DisabledDisplays = _detectedDisplays
                .Skip(1)
                .Select(display => display.DisplayName)
                .ToList();
        }

        _settings.Profiles.Add(profile);
        _settings.SelectedProfileId = profile.Id;
        _settings.Save();

        LoadProfilesIntoUi();
        RegisterConfiguredHotkeys();
        RefreshTrayMenu();

        SetStatus("New profile added.");
    }

    private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
    {
        DisplayModeProfile? profile = GetSelectedProfile();

        if (profile is null)
        {
            return;
        }

        if (_settings.Profiles.Count <= 1)
        {
            System.Windows.MessageBox.Show(
                "DisplayDeck needs at least one profile.",
                "Cannot Delete Profile",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            return;
        }

        MessageBoxResult result = System.Windows.MessageBox.Show(
            $"Delete profile '{profile.Name}'?",
            "Delete Profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.Profiles.Remove(profile);
        _settings.SelectedProfileId = _settings.Profiles[0].Id;
        _settings.Save();

        LoadProfilesIntoUi();
        RegisterConfiguredHotkeys();
        RefreshTrayMenu();

        SetStatus("Profile deleted.");
    }

    private async void SwitchSelectedProfileButton_Click(object sender, RoutedEventArgs e)
    {
        DisplayModeProfile? profile = GetSelectedProfile();

        if (profile is null)
        {
            return;
        }

        SaveSelectedProfileFromUi(showSavedStatus: false);

        await SwitchToProfileAsync(profile);
    }

    private async Task SwitchToProfileByIdAsync(string profileId)
    {
        DisplayModeProfile? profile = _settings.Profiles.FirstOrDefault(item =>
            string.Equals(item.Id, profileId, StringComparison.OrdinalIgnoreCase)
        );

        if (profile is null)
        {
            return;
        }

        await SwitchToProfileAsync(profile);
    }

    private async Task SwitchToProfileAsync(DisplayModeProfile profile)
    {
        await RunWithUiLockAsync("Switching to " + profile.Name + "...", async () =>
        {
            await _displayModeService.SwitchToProfileAsync(profile);
            LoadDetectedDisplays();
            SetStatus(profile.Name + " activated.");
        });
    }

    private void RefreshDisplaysButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDetectedDisplays();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SaveSelectedProfileFromUi(showSavedStatus: true);
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

    private bool SaveSelectedProfileFromUi(bool showSavedStatus)
    {
        DisplayModeProfile? profile = GetSelectedProfile();

        if (profile is null)
        {
            return false;
        }

        if (!TryReadHotkeyEditorValues(out string errorMessage))
        {
            System.Windows.MessageBox.Show(
                errorMessage,
                "Invalid Hotkey",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            return false;
        }

        if (!TryApplyDisplaySelections(profile, out errorMessage))
        {
            System.Windows.MessageBox.Show(
                errorMessage,
                "Invalid Display Profile",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );

            return false;
        }

        string profileName = SelectedProfileNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(profileName))
        {
            profileName = "Display Profile";
        }

        profile.Name = profileName;
        profile.LauncherPath = LauncherPathBox.Text.Trim();
        profile.LauncherProcessName = LauncherProcessNameBox.Text.Trim();
        profile.LaunchAppAfterSwitch = LaunchAppAfterSwitchCheckBox.IsChecked == true;
        profile.CloseLauncherAfterSwitch = CloseLauncherAfterSwitchCheckBox.IsChecked == true;

        profile.HotkeyCtrl = HotkeyCtrlCheckBox.IsChecked == true;
        profile.HotkeyAlt = HotkeyAltCheckBox.IsChecked == true;
        profile.HotkeyShift = HotkeyShiftCheckBox.IsChecked == true;
        profile.HotkeyWin = HotkeyWinCheckBox.IsChecked == true;
        profile.HotkeyKey = HotkeyKeyBox.Text.Trim();

        profile.EnsureDefaults(profile.Name);

        _settings.SelectedProfileId = profile.Id;
        _settings.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;

        ApplyStartupSetting();

        _settings.Save();

        _displayModeService = new DisplayModeService(_settings);

        RegisterConfiguredHotkeys();
        RefreshTrayMenu();

        ProfilesListBox.Items.Refresh();
        UpdateSelectedProfileHeader();

        if (showSavedStatus)
        {
            SetStatus("Profile saved. Hotkeys and tray menu updated.");
        }

        return true;
    }

    private bool TryApplyDisplaySelections(DisplayModeProfile profile, out string errorMessage)
    {
        errorMessage = "";

        List<string> enabled = _profileDisplaySelections
            .Where(selection => selection.Enabled)
            .Select(selection => selection.DisplayName)
            .ToList();

        List<string> disabled = _profileDisplaySelections
            .Where(selection => !selection.Enabled)
            .Select(selection => selection.DisplayName)
            .ToList();

        string primary = _profileDisplaySelections
            .FirstOrDefault(selection => selection.Primary)
            ?.DisplayName ?? "";

        if (enabled.Count == 0)
        {
            errorMessage = "This profile must have at least one enabled display.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(primary))
        {
            primary = enabled[0];
        }

        if (!enabled.Contains(primary, StringComparer.OrdinalIgnoreCase))
        {
            errorMessage = "The primary display must also be enabled.";
            return false;
        }

        profile.EnabledDisplays = enabled;
        profile.DisabledDisplays = disabled;
        profile.PrimaryDisplayName = primary;

        return true;
    }

    private bool TryReadHotkeyEditorValues(out string errorMessage)
    {
        errorMessage = "";

        bool ctrl = HotkeyCtrlCheckBox.IsChecked == true;
        bool alt = HotkeyAltCheckBox.IsChecked == true;
        bool shift = HotkeyShiftCheckBox.IsChecked == true;
        bool win = HotkeyWinCheckBox.IsChecked == true;

        string keyRaw = HotkeyKeyBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(keyRaw))
        {
            return true;
        }

        if (!ctrl && !alt && !shift && !win)
        {
            errorMessage = "A hotkey must include at least one modifier such as Ctrl, Alt, Shift, or Win.";
            return false;
        }

        if (!TryGetVirtualKey(keyRaw, out _, out string normalizedKey))
        {
            errorMessage = "Hotkey key is invalid. Try a letter, number, F1-F12, Enter, Space, Escape, Home, End, PageUp, or PageDown.";
            return false;
        }

        uint currentModifiers = BuildModifierValue(ctrl, alt, shift, win);

        DisplayModeProfile? selectedProfile = GetSelectedProfile();

        foreach (DisplayModeProfile profile in _settings.Profiles)
        {
            if (selectedProfile is not null &&
                string.Equals(profile.Id, selectedProfile.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.HotkeyKey))
            {
                continue;
            }

            if (!TryGetVirtualKey(profile.HotkeyKey, out uint profileVirtualKey, out _))
            {
                continue;
            }

            if (!TryGetVirtualKey(normalizedKey, out uint currentVirtualKey, out _))
            {
                continue;
            }

            uint profileModifiers = BuildModifierValue(
                profile.HotkeyCtrl,
                profile.HotkeyAlt,
                profile.HotkeyShift,
                profile.HotkeyWin
            );

            if (profileModifiers == currentModifiers && profileVirtualKey == currentVirtualKey)
            {
                errorMessage = $"The hotkey {FormatHotkey(ctrl, alt, shift, win, normalizedKey)} is already used by '{profile.Name}'.";
                return false;
            }
        }

        HotkeyKeyBox.Text = normalizedKey;

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

    private void RegisterConfiguredHotkeys()
    {
        var helper = new WindowInteropHelper(this);

        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        UnregisterConfiguredHotkeys();

        int hotkeyId = HOTKEY_ID_BASE;
        int registeredCount = 0;

        foreach (DisplayModeProfile profile in _settings.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.HotkeyKey))
            {
                continue;
            }

            if (!TryGetVirtualKey(profile.HotkeyKey, out uint virtualKey, out _))
            {
                continue;
            }

            uint modifiers = BuildModifierValue(
                profile.HotkeyCtrl,
                profile.HotkeyAlt,
                profile.HotkeyShift,
                profile.HotkeyWin
            );

            if (modifiers == 0)
            {
                continue;
            }

            bool registered = RegisterHotKey(
                helper.Handle,
                hotkeyId,
                modifiers | MOD_NOREPEAT,
                virtualKey
            );

            if (registered)
            {
                _hotkeyProfileMap[hotkeyId] = profile.Id;
                registeredCount++;
            }

            hotkeyId++;
        }

        if (registeredCount > 0)
        {
            SetStatus($"Ready. {registeredCount} profile hotkey(s) active.");
        }
        else
        {
            SetStatus("Ready. No profile hotkeys are active.");
        }
    }

    private void UnregisterConfiguredHotkeys()
    {
        var helper = new WindowInteropHelper(this);

        if (helper.Handle == IntPtr.Zero)
        {
            return;
        }

        foreach (int hotkeyId in _hotkeyProfileMap.Keys.ToList())
        {
            UnregisterHotKey(helper.Handle, hotkeyId);
        }

        _hotkeyProfileMap.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int hotkeyId = wParam.ToInt32();

            if (_hotkeyProfileMap.TryGetValue(hotkeyId, out string? profileId))
            {
                handled = true;
                _ = SwitchToProfileByIdAsync(profileId);
            }
        }

        return IntPtr.Zero;
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
        SwitchSelectedProfileButton.IsEnabled = enabled;
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private static string FormatHotkey(bool ctrl, bool alt, bool shift, bool win, string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "No hotkey set";
        }

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