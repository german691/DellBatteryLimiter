# DellBatteryLimiter

A lightweight Windows system tray utility for Dell laptops that toggles battery charging thresholds (e.g., 50%–80% vs 100%) with a single click.

It uses Dell's official `cctk.exe` (*Dell Command | Configure*) to control battery charging thresholds directly in hardware.

## Features

- 🟢 **Limited Mode**: Stops charging at a set threshold (e.g., 80%) to preserve battery health.
- 🔴 **Unlimited Mode**: Charges up to 100% when full capacity is needed.
- ⚙️ **Custom Threshold**: Configure custom start and stop charging percentages directly from the tray.
- **Single Click**: Left-click the tray icon to toggle between modes instantly.
- **No GUI overhead**: Runs entirely in the Windows notification area.

## Requirements

- Dell laptop with `Dell Command | Configure` installed (`winget install -e --id Dell.CommandConfigure`).

## Build

Compile with Windows' built-in C# compiler:

```cmd
build.bat
```
