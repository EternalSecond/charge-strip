# Charge Strip

Charge Strip is a compact Windows 11 battery overlay for wireless peripherals. Drag it into a free space beside the taskbar icons or media controls. Open Settings to choose which devices appear, in what order, and how much space the strip uses.

## Download and run

1. Open [Releases](https://github.com/EternalSecond/charge-strip/releases) and download the latest **Charge-Strip-Windows-x64.zip**.
2. Extract the entire ZIP to a folder you plan to keep.
3. Run **ChargeStrip.exe**. To open the editor, right-click the strip or run **Open Settings.cmd** from the same folder. The command works while the strip is already running.

The repository is private. Release downloads require an account with repository access. The portable build is unsigned. Windows may show a publisher prompt.

## Everyday use

| Action | What it does |
| --- | --- |
| Left-click the strip | Show all detected devices and their battery status |
| Right-click the strip | Open Settings |
| Drag the strip or its preview | Move the overlay, including between monitors |
| Use the Devices list in Settings | Check what to show, reorder devices with the arrows, and set a limit of 1–12 |
| Change display mode | Show icons and percentages, icons only, or percentages only |
| Enable Run with Windows | Add a per-user Startup shortcut that launches the strip |
| Save or Cancel | Keep the changes or restore the previous layout and appearance |

You can also enter exact monitor-relative coordinates, change scale, gap and corner radius, and adjust colors and background opacity. The application stores preferences in `%LOCALAPPDATA%\Charge Strip\settings.xml`. It refreshes readings in the background about every 12 seconds.

## Battery support

| Device | Source and verification |
| --- | --- |
| SteelSeries Arctis Nova Pro Wireless | Hardware-tested with the `1038:12E0` 2.4 GHz USB base. Displays the headset and the spare battery separately. Both readings were compared with SteelSeries Engine. |
| HyperX Pulsefire Haste 2 Wireless | Hardware-tested with the `03F0:0D97` USB receiver. The app read the percentage and charging state during a live charge. The `03F0:0F98` variant uses the same documented query. |
| HyperX Cloud Alpha Wireless | The `03F0:098D` receiver was detected on the test machine. The battery query returned no reading there. |
| PC battery and Bluetooth accessories | Uses the Windows battery data if the device publishes it. Bluetooth compatibility depends on the model and connection mode. |

Brand names alone do not determine compatibility. A detected receiver can appear with an unavailable reading when the device is asleep, off, or does not answer.

## Build from source

Use 64-bit Windows 11 with the built-in .NET Framework compiler. Run `Start Charge Strip.cmd` from the repository root to build and launch. You can pass `/settings` to open the editor. To rebuild after changes, exit Charge Strip and run the command with `/build`.

Charge Strip is written in C# with Windows Forms. `ChargeStrip.cs` handles the taskbar overlay and single-instance startup. `Settings.cs` handles rendering, the editor and persistence. `Batteries.cs` reads Windows device properties and sends read-only battery queries to supported USB HID devices.

## Protocol references

- [SteelSeries Arctis HID protocol](https://github.com/TimP4w/HaloDaemon-plugins/blob/main/steelseries_arctis/docs/protocol.md)
- [HyperX Pulsefire Haste 2 battery protocol](https://github.com/krisgfx9/Wireless-Device-Battery-Monitor/blob/main/docs/hyperx_haste_2.md)
- [HyperX Cloud Alpha HID battery protocol](https://github.com/FedeltaMedia/hyperx-cloud-alpha-battery#technical-details)
