# Codex Usage Monitor

A Windows 11 tray app built with .NET 10 and WPF. It reads your signed-in Codex CLI account through one hidden `codex app-server` process.

The executable and window icon use a fixed 88% five-hour / 88% weekly / three-reset design on a dark tile. The live tray icon continues to show actual usage. Icon sources are in `src/CodexUsageMonitor/Assets`; regenerate them with `pwsh -NoProfile -File scripts/generate-icon.ps1`. The ICO includes nine resolutions from 16 to 256 pixels, with a separate 512px PNG.

## Tray icon

- **Outer arc:** weekly quota remaining, from 0 to 100%.
- **Center number:** five-hour quota remaining, rounded to a whole number, without a percent sign.
- **Bottom dots:** one dot per available reset. These occupy a reserved bottom gap, so the weekly arc never forms a closed ring when resets are available—even at 100% remaining.
- With no resets, 100% weekly remaining forms a complete circle.
- A hollow bottom marker means the reset count is unavailable. An amber corner badge and muted colors mean usage is unavailable or stale.

The arc fills clockwise from the left end of the bottom gap. A faint track shows its available length. Larger reset counts use closer, smaller dots; the popup and tooltip always give the exact count. At small tray sizes, many individual dots may be difficult to distinguish.

Click the icon for quota details and local reset times. Right-click for refresh, settings, startup-at-login, and exit. Close the details window to leave monitoring active. The app polls every 60 seconds, responds to account update notifications, and refreshes after resume. Failed connections retry with backoff while retaining the last known values.

## Run

1. Sign in with the Codex CLI using your ChatGPT account. The standard build also needs the **.NET 10 Desktop Runtime**; the single-file build bundles it.
2. Extract the release ZIP to a permanent folder; keep its files together.
3. Run `CodexUsageMonitor.exe`. Windows may initially put the icon in the hidden-icons overflow; drag it onto the taskbar if desired.
4. Optionally enable **Start with Windows** from the tray menu or Settings. This uses the current user's `Run` registry key. Disable it before moving or removing the app.

The app finds `codex.exe` on PATH, in the Codex desktop installation, or in the standard global npm install. Settings accepts an explicit executable path when needed. `--details` opens the popup on initial launch; launching again opens the existing instance's popup. Only one monitor instance runs per Windows session. `--exit` asks that instance to shut down cleanly.

No API key is needed. API-key-only accounts may not expose ChatGPT quota data. The app only reads usage; it never consumes a reset or runs model turns. Settings are stored at `%LOCALAPPDATA%\CodexUsageMonitor\settings.json`. It does not copy credentials or persist app-server messages.

## Build, test, and publish

From the repository in PowerShell 7, with .NET 10 SDK installed:

```powershell
pwsh -NoProfile -File scripts/build.ps1
pwsh -NoProfile -File scripts/test.ps1
pwsh -NoProfile -File scripts/test.ps1 -Live
pwsh -NoProfile -File scripts/publish.ps1
pwsh -NoProfile -File scripts/publish.ps1 -SingleFile
```

The scripts locate .NET under Program Files when it is not on PATH. No third-party NuGet packages are used. Build the solution in Visual Studio or with `dotnet build CodexUsageMonitor.slnx` if preferred.

The default release is framework-dependent Windows x64, at `artifacts/publish/win-x64/` and `artifacts/CodexUsageMonitor-win-x64.zip`. `-SelfContained` bundles the runtime in a separate `win-x64-self-contained` folder and ZIP.

`-SingleFile` produces a self-contained, compressed `CodexUsageMonitor.exe` in `artifacts/publish/win-x64-single-file/`, plus `artifacts/CodexUsageMonitor-win-x64-single-file.zip`. Only the EXE is needed to run the monitor; the ZIP also includes documentation and the license. .NET extracts bundled WPF/native components to its per-user temporary cache at launch. Codex CLI remains an external prerequisite. Runtime bundling may require downloading Microsoft runtime packs during publishing.

The tray icon uses permanent GUID `621dbea4-750f-4ea2-8f67-c9a6f8a1b65c` in every shell operation. Keep the executable at a stable path when updating: Windows associates unsigned GUID-based icons with their executable location. Windows controls taskbar-versus-overflow placement, and switching from the old registration may require choosing that placement once again.

The test runner checks quota parsing, window selection, ring geometry, out-of-order protocol replies, notification handling, disconnection, and automatic server recovery. Render checks produce `artifacts/checks/icon-preview.png`, `popup-preview.png`, and a 1,000-redraw native handle report. `-Live` additionally writes a sanitized usage snapshot to `artifacts/checks/live-usage.json`; it contains quota numbers and reset times, but no account identifiers or tokens. All artifacts are gitignored.

Standalone diagnostics:

```powershell
& .\CodexUsageMonitor.exe --probe "$PWD\usage.json" | Out-Null
& .\CodexUsageMonitor.exe --render-check "$PWD\previews" | Out-Null
```

These modes exit after running. `--probe` requires an existing parent output folder. Exit code 0 indicates success.

## Design

- `CodexUsageMonitor.Core`: account-response parsing and deterministic icon geometry.
- `CodexUsageMonitor`: asynchronous stdio client, reconnecting monitor, WPF popup and renderer, native GUID-based `Shell_NotifyIcon` integration, and settings. Windows Forms supplies the context menu and screen helpers.
- `CodexUsageMonitor.Tests`: dependency-free test executable with a fake app-server for failure and recovery tests.
- `CodexUsageMonitor.TrayTests`: native structure layout, GUID flags, version-4 callbacks, registration retry, Explorer recovery, and cleanup checks using a fake shell transport.

The main `codex` quota bucket is preferred over the legacy response. Five-hour and weekly windows are identified by their 300/10080-minute durations. Missing values stay unavailable rather than becoming zero. The backend's reset `availableCount` is authoritative even when detailed credit rows are missing. Implausible counts above 1,000 are treated as unavailable to bound rendering work.

Protocol reference: [official Codex app-server documentation](https://learn.chatgpt.com/docs/app-server). Verified against CLI 0.153.4. The client opts into experimental fields; future CLI protocol changes may require an update.

The hidden callback window re-registers the icon on `TaskbarCreated` after Explorer recreates the taskbar. The renderer checks taskbar theme and DPI every five seconds. Physical sleep/resume, Explorer restart, and mixed-DPI multi-monitor behavior should also be checked on the target desktop; automated tests exercise the recovery handler without restarting Explorer.
