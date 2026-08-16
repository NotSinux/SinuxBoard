# SinuxBoard

A lightweight Windows clipboard manager. SinuxBoard runs quietly in the system
tray, keeps a local history of everything you copy, and restores your last
clipboard item whenever Windows or the app starts.

## Features

- Runs entirely in the system tray — no window on startup, no console.
- Watches the clipboard using the native `AddClipboardFormatListener` /
  `WM_CLIPBOARDUPDATE` mechanism (no polling).
- Stores clipboard text history locally in a SQLite database.
- Restores your most recent clipboard item automatically on startup.
- Prevents duplicate consecutive entries (including its own restores).
- Browse history in a simple window; double-click any item to restore it.
- Export the full history to a JSON file, and import it back later.
- Optional automatic startup with Windows (per-user, no admin required).

## Screenshots

_Add screenshots of the tray menu and history window here._

## Requirements

- Windows 10 or 11, x64.
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
  — only needed for the runtime-required release. The self-contained
  release needs nothing preinstalled.

## Installation

1. Download the latest `SinuxBoardSetup-x64.exe` from
   [Releases](../../releases).
2. Run the installer and follow the prompts.
3. SinuxBoard starts automatically and appears in the system tray
   (you may need to expand the tray's hidden-icons area the first time).

## How It Works

SinuxBoard hosts a hidden, message-only native window purely to receive
clipboard change notifications from Windows. There is no visible window and
no polling timer. When the clipboard changes and contains text:

1. SinuxBoard reads the text.
2. If it's different from the most recently stored entry, it's saved to
   SQLite.
3. The tray icon and History window always reflect the latest state on
   demand.

When SinuxBoard starts (including after a Windows reboot), it reads the most
recent entry from the database and writes it back to the clipboard, so your
last copied text is always ready to paste.

## Database Location

```text
%AppData%\SinuxBoard\sinuxboard.db
```

The database is created automatically on first run. It is never stored next
to the executable and is left untouched by uninstalls/updates unless you
remove it yourself.

## Import / Export

- **Export History** writes the full history to a JSON file you choose, in
  the form:

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
  your existing database (invalid files are rejected without crashing the
  app, and identical consecutive entries are skipped).

## Startup Behavior

Use the tray menu's **Start with Windows** option to toggle automatic
startup. This writes/removes a single value under:

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
```

No administrator privileges are required, and no other registry locations
are touched.

## Build Instructions

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Restore dependencies
dotnet restore

# Debug build
dotnet build -c Debug

# Release build
dotnet build -c Release
```

## Publish Instructions

```bash
# Runtime-required (smaller, needs .NET 8 Desktop Runtime installed)
dotnet publish SinuxBoard/SinuxBoard.csproj -c Release -r win-x64 --self-contained false -o publish/win-x64-framework-dependent

# Self-contained (larger, runs with no .NET installation required)
dotnet publish SinuxBoard/SinuxBoard.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

## Packaging (Inno Setup)

An Inno Setup script is provided at `installer/SinuxBoard.iss`. It expects a
self-contained publish output at `publish/win-x64` (adjust `PublishDir` in
the script if you use a different path), and produces
`installer/Output/SinuxBoardSetup-x64.exe`.

```bash
iscc installer/SinuxBoard.iss
```

The installer installs the application under the user's local install
directory, creates an uninstaller and optional shortcuts, and can register
SinuxBoard to start with Windows. The SQLite database always remains under
`%AppData%\SinuxBoard`, independent of the install location.

## Repository Contents

Committed to source control:

```text
README.md
LICENSE
.gitignore
SinuxBoard.sln
SinuxBoard/            (source files, csproj, Assets/SinuxBoard.ico)
installer/SinuxBoard.iss
```

Never committed: `bin/`, `obj/`, `.vs/`, publish output, or any SQLite
database files.

## License

Released under the [MIT License](LICENSE).
