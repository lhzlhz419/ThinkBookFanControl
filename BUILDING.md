# Building ThinkBook Fan Control

## Requirements

- Windows x64
- .NET 9 SDK
- Network access for first NuGet restore

## Build and Publish

Run from the repository root:

```powershell
.\scripts\build_csharp.ps1 -Configuration Release -Publish
```

Outputs:

- `dist\ThinkBookFanControl-win-x64`
  Self-contained build. Use this when the target computer may not have .NET installed.

- `dist\ThinkBookFanControl-win-x64-net9-runtime`
  Smaller build. Use this when the target computer already has .NET 9 Desktop Runtime.

When available, the build script copies the latest installed versions of these
Lenovo Vantage add-ins into `VantageAddins` in each build or publish directory:

- `SmartInteractAddin`
- `SmartColorAddin`
- `MultimediaAddin`
- `SmartNoiseCancelledAddin`
- `LenovoProductivitySystemAddin` (BIOS advanced-toolkit native interface)

Only the x64 files and feature-specific dependencies used by the app are
copied. ARM64/x86 binaries, localization resources, and unrelated Multimedia
components are omitted. Required native dependencies, config files, resources,
and license notices stay beside their assemblies. These third-party files
remain ignored by Git. At runtime the app searches the local copy first, then
the installed Vantage add-in directory.
Bundling add-in files does not replace the Lenovo services, drivers, or audio
components used by those add-ins.

The project also copies the checked-in x86 Lenovo PC Manager
`lib\LenovoPcManager\WrapPlugin.dll` to `LenovoPcManager` in each output.
The x64 app queries its `IsSupportColorTemperature` export once through the
32-bit Windows PowerShell host. The DLL has only Windows system dependencies;
the app implements the Gamma Ramp algorithm itself.

## Dependency Note

The repository intentionally includes:

```text
csharp\ThinkBookFanControl\lib\LibreHardwareMonitorLib.dll
```

The app currently uses this newer local LibreHardwareMonitor DLL while the
project file explicitly packages its runtime dependencies through NuGet.
