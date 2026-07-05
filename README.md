# ThinkBook Fan Control

Experimental fan-curve controller for Lenovo ThinkBook 16p G6 IAX.

[中文说明](README.zh-CN.md)

## Disclaimer

This project is not affiliated with Lenovo.
This project is not an official Lenovo project.
This project is not endorsed, supported, or sponsored by Lenovo.
This project is an independent experimental tool.
This tool controls fan speeds through Lenovo WMI interfaces and may affect
system cooling, hardware stability, hardware lifespan, and data safety.
Use this project at your own risk; you are solely responsible for any
consequences arising from its use.
By using this tool, you acknowledge that you fully understand the risks above
and voluntarily accept all consequences.
If you do not agree, stop using and uninstall this tool immediately.

## Project Overview

The app is a C# WPF desktop program that reads temperatures through
LibreHardwareMonitor and controls the two fans through Lenovo WMI methods.

## Current Hardware Interface

- WMI namespace: `root\wmi`
- Method class: `LENOVO_OTHER_METHOD`
- Fan 1 RPM / target ID: `0x04030001`
- Fan 2 RPM / target ID: `0x04030002`
- Auto target value: `0`
- Fan RPM range source: `LENOVO_FAN_TEST_DATA`

## Features

- CPU/GPU/VRAM temperature display.
- Fan 1/Fan 2 RPM display.
- A Full speed switch that pauses the active controller, clears both manual
  targets to `0`, then sets WMI feature `0x04020000` to `1`. Turning it off
  restores the controller's previous running/stopped state.
- Separate CPU and GPU fan curves.
- Separate Fan 1 and Fan 2 curves inside each CPU/GPU chart.
- Optional synchronized point dragging for both fan curves.
- Five saved profiles.
- Light/dark theme and Chinese/English UI.
- Tray menu, close-to-tray, minimize-to-tray, and optional Windows startup.
- Restores firmware automatic fan control before exit.

## Safety Notes

This tool writes directly to Lenovo firmware/WMI fan-control methods. It has
only been developed against the hardware path above. Use it only with active
temperature monitoring and verify that `Stop` restores automatic fan control.

Full speed is a global `FNST` override for both fans. On exit, the app clears
feature `0x04020000` before writing both manual fan targets back to `0`.

Administrator permission is required at runtime.

## Build

Open PowerShell in the repository root:

```powershell
.\scripts\build_csharp.ps1 -Configuration Release -Publish
```

The script creates two release folders under `dist`:

- `ThinkBookFanControl-win-x64`: self-contained build, no .NET runtime install required.
- `ThinkBookFanControl-win-x64-net9-runtime`: smaller build, requires .NET 9 Desktop Runtime.

See [BUILDING.md](BUILDING.md) for details.
