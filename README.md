# BrightnessBridge

Makes the Windows brightness slider and keyboard brightness keys control external monitors over DDC/CI.

Windows only applies its own brightness setting to built-in laptop panels. Using the slider or brightness keys with a desktop monitor changes a stored value in Windows' power settings, but the monitor never receives it. BrightnessBridge is a tiny background helper that sends that value to every DDC/CI-capable monitor.

## How it works

- Subscribes to the Windows brightness power setting (`VIDEONORMALLEVEL`) with `RegisterPowerSettingNotification`. There is no polling: the process sleeps until Windows reports a change.
- Sends the new value to each monitor as VCP code `0x10` (brightness) through the Monitor Configuration API (`dxva2.dll`). Values are scaled to the monitor's reported range.
- Writes happen on a worker thread that only ever applies the latest value. Holding a brightness key down results in a few DDC writes, not a queue of dozens.
- Monitors are looked up again before every write, so it keeps working after sleep, hotplug and display mode changes.
- When it starts, it syncs each monitor to the current Windows brightness.
- No window, no tray icon, about 20 MB of memory and no CPU while idle. Only one instance runs at a time.

It runs per user at sign-in, not as a Windows service. Services run in session 0, which has no access to the interactive desktop's monitors, so DDC/CI calls would fail there.

## Requirements

- Windows 10 or 11. The helper uses .NET Framework 4.8, which comes with both.
- A monitor with DDC/CI enabled. Many monitors have a DDC/CI toggle in their on-screen menu, and it is sometimes off by default.

## Install

Download `BrightnessBridge-<version>.msi` from [Releases](https://github.com/nikicat/brightness-bridge/releases/latest) and run it. Installation is per user and doesn't need administrator rights. The installer:

- installs to `%LOCALAPPDATA%\Programs\BrightnessBridge`
- registers the helper to start at sign-in (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`)
- starts the helper right away, so the brightness keys work immediately

The MSI isn't code-signed, so Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**.

To uninstall, use **Settings → Apps → Installed apps → BrightnessBridge → Uninstall**. Installing a newer MSI upgrades in place: the running helper is closed, replaced and restarted.

Silent install and uninstall:

```powershell
msiexec /i BrightnessBridge-<version>.msi /qn
msiexec /x BrightnessBridge-<version>.msi /qn
```

## Building

### MSI

Requires the .NET SDK 6 or later. The WiX Toolset is downloaded from NuGet automatically.

```powershell
dotnet build installer -c Release -p:Version=1.2.3
```

The package is written to `installer\bin\Release\BrightnessBridge.msi`. Pushing a `v*` tag makes GitHub Actions build the MSI and publish it as a release.

### Without the SDK

`install.ps1` compiles the helper with the C# compiler that ships with .NET Framework and installs it to the same location and startup entry as the MSI. It's useful for quick local iteration:

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

Don't mix the two install methods. If the MSI is installed, uninstall it from Settings instead of running `uninstall.ps1`.

## Troubleshooting

Start the helper with `--log` to write `BrightnessBridge.log` next to the exe:

```powershell
Get-Process BrightnessBridge -ErrorAction SilentlyContinue | Stop-Process
& "$env:LOCALAPPDATA\Programs\BrightnessBridge\BrightnessBridge.exe" --log
```

- No `event:` lines when you move the slider: Windows isn't changing its brightness value.
- `DDC write failed`: the monitor isn't accepting DDC/CI. Check that DDC/CI is enabled in the monitor menu.
- The monitor accepts the value but the picture doesn't change: a picture mode (Eco, HDR, game modes, dynamic contrast) may be locking brightness. Switch the monitor to its standard mode.

## Limitations

- On the sign-in screen, before anyone has signed in, the keys don't change brightness.
- All DDC/CI monitors get the same brightness level.
- Changes made with the monitor's own buttons aren't reflected back to the Windows slider.

## License

[MIT](LICENSE)
