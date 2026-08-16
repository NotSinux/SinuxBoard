# SinuxBoard

A lightweight Windows clipboard manager. SinuxBoard runs quietly in the system
tray, keeps a local history of everything you copy, and restores your last
clipboard item whenever Windows or the app starts.

## Features

- Runs entirely in the system tray — no window on startup and no console.
- Watches the clipboard using the native `AddClipboardFormatListener` /
  `WM_CLIPBOARDUPDATE` mechanism (no polling).
- Stores clipboard text history locally in a SQLite database.
- Restores your most recent clipboard item automatically on startup.
- Prevents duplicate consecutive entries, including its own restores.
- Browse history in a simple window; double-click any item to restore it.
- Export the full history to a JSON file, and import it back later.
- Optional automatic startup with Windows (per-user, no admin required).

## Requirements

- Windows 10 or 11, x64.
- .NET 8 Desktop Runtime — only needed for the runtime-required release.
  The self-contained release needs nothing preinstalled.

## Installation

Download the appropriate release from the GitHub Releases page:

- **Runtime Required** — smaller download; requires the .NET 8 Desktop Runtime.
- **Self-Contained** — larger download; includes the required .NET runtime.

Extract or install the downloaded release according to the release package
provided, then run `SinuxBoard.exe`.

SinuxBoard runs in the system tray. You may need to expand the tray's
hidden-icons area the first time.

## How It Works

SinuxBoard hosts a hidden, message-only native window purely to receive
clipboard change notifications from Windows. There is no visible window and
no polling timer. When the clipboard changes and contains text:

1. SinuxBoard reads the text.
2. If it's different from the most recently stored entry, it's saved to SQLite.
3. The tray icon and History window reflect the latest state on demand.

When SinuxBoard starts, including after a Windows reboot, it reads the most
recent entry from the database and writes it back to the clipboard, so your
last copied text is ready to paste.

## Database Location

```text
%AppData%\SinuxBoard\sinuxboard.db
```

The database is created automatically on first run. It is stored separately
from the application files.

## Import / Export

- **Export History** writes the full history to a JSON file you choose, in
  the following format:

  ```json
  [
    {
      "Id": 1,
      "Content": "Example",
      "Type": "Text",
      "CreatedAt": "2026-01-01T12:00:00Z"
    }
  ]
  ```

- **Import History** reads a JSON file in the same format and merges it into
  your existing database. Invalid files are rejected without crashing the
  app, and identical consecutive entries are skipped.

## Startup Behavior

Use the tray menu's **Start with Windows** option to toggle automatic startup.
This writes or removes a single value under:

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
```

No administrator privileges are required, and no other registry locations
are touched.

## Build Instructions

Requires the .NET 8 SDK.

```bash
# Restore dependencies
dotnet restore

# Debug build
dotnet build -c Debug

# Release build
dotnet build -c Release
```

## Publish Instructions

### Runtime Required

Smaller release. The target computer must have the .NET 8 Desktop Runtime
installed.

```bash
dotnet publish SinuxBoard/SinuxBoard.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64-runtime-required
```

### Self-Contained

Larger release. No .NET installation is required on the target computer.

```bash
dotnet publish SinuxBoard/SinuxBoard.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64-self-contained
```

## Repository Contents

The repository contains the source code, project configuration, application
icon, documentation, and license.

Never commit build output or local application data, including:

```text
bin/
obj/
.vs/
publish/
*.db
```

## License

Released under the [MIT License](LICENSE).
