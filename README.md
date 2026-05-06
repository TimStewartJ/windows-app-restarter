# Windows App Restarter

A tiny Windows tray utility that restarts the Windows App / Windows 365 client processes and restarts Explorer.

It replaces this common manual script:

```powershell
Get-Process -Name Windows365, msrdcw, msrdc | Stop-Process -Force
Get-Process -Name explorer | Stop-Process -Force
Start-Process explorer.exe
```

## What it does

- Adds a tray icon named **Windows App Restarter**.
- Double-click the icon, or right-click and choose **Restart Windows App + Explorer**.
- Stops `Windows365`, `msrdcw`, and `msrdc` if they are running.
- Restarts `explorer.exe` and recreates the tray icon afterward.
- Shows a Windows notification with the result.
- Can start automatically when you sign in.
- Writes logs to `%LOCALAPPDATA%\WindowsAppRestarter\WindowsAppRestarter.log`.

## Install

Download the latest release from GitHub Releases.

Recommended:

1. Run `WindowsAppRestarterSetup.exe`.
2. Keep **Start Windows App Restarter when I sign in** checked if you want it to persist across reboots.
3. Use the tray icon whenever the Windows App needs a restart.

Portable:

1. Download `WindowsAppRestarter-win-x64-portable.zip`.
2. Extract it somewhere stable, such as `%LOCALAPPDATA%\Programs\WindowsAppRestarter`.
3. Run `WindowsAppRestarter.exe`.
4. Right-click the tray icon and enable **Start with Windows**.

## Build locally

Requirements:

- Windows
- .NET 10 SDK

```powershell
.\scripts\publish-local.ps1
.\scripts\build-installer.ps1
```

The generated executable is written to:

```text
artifacts\publish\win-x64\WindowsAppRestarter.exe
```

The generated installer is written to:

```text
installer\Output\WindowsAppRestarterSetup.exe
```

## Notes

This app is unsigned. Windows SmartScreen may warn the first time you run a downloaded build.
