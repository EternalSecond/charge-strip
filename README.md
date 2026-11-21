# Charge Strip

A portable Windows 11 taskbar overlay for headset, mouse and other battery readings. Choose the devices you want to see, then drag the strip beside your taskbar icons or media controls.

[**Download for Windows 11 x64**](https://github.com/EternalSecond/charge-strip/releases/latest/download/Charge-Strip-Windows-x64.zip) · [Release notes and checksum](https://github.com/EternalSecond/charge-strip/releases/latest) · [Changelog](CHANGELOG.md)

![Charge Strip settings with a live preview, device checklist, arrangement controls, and color options](docs/settings-preview.png)

## Install and run

1. Download **Charge-Strip-Windows-x64.zip** and extract it to a folder you plan to keep.
2. Open **ChargeStrip.exe**. The overlay appears near the taskbar.
3. Right-click the overlay or open **Open Settings.cmd** to configure it. The launcher reuses the running instance.

The portable executable is unsigned. Windows may display a publisher warning. To verify your download, run `Get-FileHash .\Charge-Strip-Windows-x64.zip -Algorithm SHA256` in PowerShell and compare it with **SHA256SUMS.txt** on the release page.

## Customize your strip

| Action | Result |
| --- | --- |
| Left-click or hover | See all detected devices in the list, or the name of the reading under the pointer |
| Drag the strip or its preview | Move the overlay, including between monitors |
| Set a monitor and X/Y | Place the strip at an exact offset from that monitor’s top-left corner |
| Check devices, use the arrows and set a limit | Choose the visible readings, their order and how many appear |
| Change display mode | Show icons and percentages, icons only, or percentages only |
| Change scale, spacing, colors or opacity | Preview appearance changes immediately |
| Enable **Run with Windows** | Start the strip when your Windows account signs in |
| Save / Cancel | Keep / revert changes |

Choose **УКР**, **РУС**, or **ENG** at the top of Settings to switch between Ukrainian, Russian, and English. The interface updates immediately. Select **Save** to keep the language, or **Cancel** to revert. Readings refresh in the background about every 12 seconds.

## Device compatibility

| Device or source | Status |
| --- | --- |
| SteelSeries Arctis Nova Pro Wireless, USB base `1038:12E0` | Verified on hardware. Separate readings for the headset and the spare battery in the base, compared with SteelSeries Engine. |
| HyperX Pulsefire Haste 2 Wireless, USB receiver `03F0:0D97` | Verified on hardware. Reports percentage and charging status. |
| HyperX Pulsefire Haste 2 Wireless, USB receiver `03F0:0F98` | Uses the documented battery query. This receiver revision was not tested for this release. |
| HyperX Cloud Alpha Wireless, USB receiver `03F0:098D` | Receiver detected on the test PC. It returned no battery reading during testing. |
| PC battery and Bluetooth accessories | Displays readings exposed by Windows. Availability depends on the model and connection mode. |

A dash means a detected device has no available reading. Check that the device is powered on, awake and connected through a supported receiver or Bluetooth mode.

## Settings, updates and removal

Preferences are stored per user in `%LOCALAPPDATA%\Charge Strip\settings.xml`. Charge Strip reads battery data locally and has no telemetry.

To update, quit Charge Strip using its notification-area menu and extract the new files into the same folder. To remove it, turn off **Run with Windows**, save, exit the app, then delete the extracted folder. Remove the separate preferences file if you also want to clear your layout and colors.

## Build from source

Use Windows 11 x64 and the included .NET Framework compiler. Run these commands from Command Prompt in the repository root.

| Command | Result |
| --- | --- |
| `"Start Charge Strip.cmd"` | Build if needed and launch the overlay |
| `"Start Charge Strip.cmd" /settings` | Build if needed and open Settings |
| `"Start Charge Strip.cmd" /build` | Rebuild and launch. Exit any running copy first. |
| `"Start Charge Strip.cmd" /package` | Build the portable ZIP and SHA-256 checksum in `build\` |

Charge Strip is written in C# with Windows Forms. `ChargeStrip.cs` handles the overlay and startup. `Settings.cs` handles the editor, rendering and persistence. `Batteries.cs` reads Windows properties and sends model-specific battery status queries over USB HID. `Localize.cs` contains the three interface translations.

### Protocol references

- [SteelSeries Arctis HID protocol](https://github.com/TimP4w/HaloDaemon-plugins/blob/main/steelseries_arctis/docs/protocol.md)
- [HyperX Pulsefire Haste 2 battery protocol](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor/blob/main/docs/hyperx_haste_2.md)
- [HyperX Cloud Alpha HID battery protocol](https://github.com/FedeltaMedia/hyperx-cloud-alpha-battery#technical-details)
