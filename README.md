# DisplayDeck

DisplayDeck is a Windows desktop utility for switching between a normal desk monitor setup and a TV gaming setup.

It is designed for people who keep multiple monitors connected to their PC and also want to use a TV in another room for couch gaming, streaming, or full-screen launcher use.

## Screenshot

![DisplayDeck main window](docs/images/displaydeck-main.png)

DisplayDeck can:

- Detect active displays connected to Windows
- Save separate display layouts for Desk Mode and TV Gaming Mode
- Switch between those modes using large buttons
- Switch using global hotkeys
- Launch an optional app when entering TV Gaming Mode
- Close the launcher app when returning to Desk Mode
- Minimize to the system tray
- Start with Windows
- Run without MultiMonitorTool or external display-switching tools

---

## Current hotkeys

| Mode | Hotkey |
|---|---|
| TV Gaming Mode | `Ctrl + Alt + G` |
| Desk Mode | `Ctrl + Alt + D` |

DisplayDeck must be running for hotkeys to work. It can run minimized in the system tray.

---

## Example use case

A common setup might be:

| Display | Use |
|---|---|
| Main monitor | Desk Mode primary display |
| Secondary monitor | Desk Mode extra display |
| TV | TV Gaming Mode primary display |

In Desk Mode, DisplayDeck can enable the desk monitors and disable the TV.

In TV Gaming Mode, DisplayDeck can enable the TV, make it the primary display, disable the desk monitors, and launch a full-screen launcher such as Winhanced, Playnite, Steam Big Picture, or another app.

---

## Important setup note

For initial setup, all displays you want to configure should be powered on, connected, and visible to Windows.

DisplayDeck only shows active displays during setup so inactive virtual or phantom displays stay hidden.

If the TV is physically disconnected, powered off, asleep, or not visible to Windows, Windows may reject the display switch. Turn the TV on, make sure it is on the correct HDMI input, refresh displays, then try again.

---

## Display switching delay

When switching modes, Windows may take a few seconds to apply the new monitor layout.

During switching, screens may:

- Flicker
- Go black briefly
- Move around
- Reappear in a different order
- Take a few seconds to settle

This is normal.

---

## System requirements

- Windows 10 or Windows 11
- x64 Windows PC
- .NET 8 runtime if running a framework-dependent build
- No separate runtime required if using the self-contained release build

---

## Download

Download the latest compiled version from the GitHub Releases page.

The recommended download is:

```text
<<<<<<< HEAD
DisplayDeck.exe
=======
DisplayDeck.exe
>>>>>>> 36dcaad (Initial public release of DisplayDeck)
