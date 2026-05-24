# DisplayDeck

DisplayDeck is a Windows desktop utility for creating and switching between custom display profiles.

It is designed for people who use multiple monitors, TVs, projectors, racing displays, or other screens connected to one PC and want a quick way to switch between different display layouts.

Common examples include desk work, TV gaming, sim racing, presentation setups, streaming layouts, or single-monitor focus modes.

## Screenshot

![DisplayDeck main window](docs/images/displaydeck-main.png)

DisplayDeck can:

- Detect active displays connected to Windows
- Create multiple custom display profiles
- Save which displays are enabled or disabled for each profile
- Set one primary display per profile
- Show friendly display names on profile cards
- Switch profiles using a large button
- Switch profiles from the system tray
- Switch profiles using editable global hotkeys
- Launch an optional app after switching to a profile
- Close an optional launcher app after switching to a profile
- Minimize to the system tray
- Start with Windows
- Run without MultiMonitorTool or external display-switching tools

---

## Display profiles

DisplayDeck is profile-based.

Each profile can store:

- Profile name
- Enabled displays
- Disabled displays
- Primary display
- Custom hotkey
- Optional launcher app
- Optional launcher process name
- Whether to launch the app after switching
- Whether to close the launcher after switching

Example profiles might include:

| Profile | Use |
|---|---|
| Desk Mode | Main monitor and secondary monitor enabled |
| TV Gaming Mode | TV enabled as the primary display |
| All Displays | Main monitor, secondary monitor, and TV enabled |
| Sim Racing Mode | Racing display or cockpit screen enabled |
| Presentation Mode | Projector or TV enabled for presenting |
| Focus Mode | Only one main monitor enabled |

---

## Hotkeys

Each profile can have its own editable hotkey.

For example:

| Profile | Example hotkey |
|---|---|
| Desk Mode | `Ctrl + Alt + D` |
| TV Gaming Mode | `Ctrl + Alt + T` |
| All Displays | `Ctrl + Alt + A` |
| Sim Racing Mode | `Ctrl + Alt + S` |

Hotkeys can use:

- Ctrl
- Alt
- Shift
- Win
- Letters
- Numbers
- Function keys
- Common keys such as Enter, Space, Escape, Home, End, PageUp, and PageDown

DisplayDeck must be running for hotkeys to work. It can run minimized in the system tray.

---

## Example use case

A common setup might include several saved profiles:

| Profile | Displays enabled | Primary display |
|---|---|---|
| Desk Mode | Main monitor and secondary monitor | Main monitor |
| TV Gaming Mode | TV only | TV |
| All Displays | Main monitor, secondary monitor, and TV | Main monitor |
| Sim Racing Mode | Racing display or cockpit screen | Racing display |

Each profile can have its own hotkey and optional launcher actions.

For example, a TV Gaming Mode profile can enable the TV, make it the primary display, disable the desk monitors, and launch a full-screen launcher such as Winhanced, Playnite, Steam Big Picture, or another app.

A Desk Mode profile can return to your normal monitors and close the launcher app.

---

## Important setup note

For initial setup, all displays you want to configure should be powered on, connected, and visible to Windows.

DisplayDeck only shows active displays during setup so inactive virtual or phantom displays stay hidden.

If a TV is physically disconnected, powered off, asleep, or not visible to Windows, Windows may reject the display switch. Turn the TV on, make sure it is on the correct HDMI input, refresh displays, then try again.

---

## Display switching delay

When switching profiles, Windows may take a few seconds to apply the new monitor layout.

During switching, screens may:

- Flicker
- Go black briefly
- Move around
- Reappear in a different order
- Take a few seconds to settle

This is normal.

---

## System tray behavior

DisplayDeck is designed to keep running quietly in the background.

- Minimize button: hides DisplayDeck to the system tray
- Close button: hides DisplayDeck to the system tray
- Tray icon double-click: reopens DisplayDeck
- Tray icon right-click: shows options for opening, switching profiles, or exiting
- Exit DisplayDeck from the tray menu: fully closes the app

---

## Launcher app support

Each profile can optionally launch or close an app after switching.

Examples:

- Winhanced
- Playnite Fullscreen
- Steam Big Picture
- Moonlight
- Sunshine helper tools
- Any other `.exe`

This means you can have one profile that launches a gaming launcher, and another profile that closes it when returning to your normal desktop setup.

Some apps may resist normal close commands, especially if they are running as administrator. If the launcher does not close correctly, try running DisplayDeck with the same permission level as the launcher.

---

## Start with Windows

DisplayDeck includes a Start with Windows option.

When enabled, DisplayDeck adds itself to the current user's Windows startup registry location:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
