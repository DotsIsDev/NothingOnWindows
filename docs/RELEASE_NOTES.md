# Nothing on Windows v0.1.2

## In-app updates

- Added **Settings → Updates → Check for updates**, with the installed version and clear up-to-date, available, progress, and failure states.
- **Download and restart** retrieves newer stable Windows x64 releases from this GitHub repository. No GitHub sign-in is needed.
- Downloads must match GitHub's SHA-256 digest; extracted files must match the release manifest. Unsafe paths, linked files, mismatched versions, unsupported platforms, and incomplete downloads are rejected.
- A separate updater waits for the app to exit, backs up and replaces managed files in the same folder, and restarts it. Failed replacement rolls back the changed files. Unknown user files, local settings/profiles, and startup registration are preserved.
- Unsaved EQ drafts prompt before restarting. Explicit Exit cancels a pending download. Downloads and backups remain in the local update cache for recovery.

## Download

Download **NothingOnWindows-v0.1.2-win-x64.zip**, extract the complete ZIP, and open `NothingOnWindows/NothingOnWindows.exe`. Exit the older app from its tray menu first. This is the one-time update needed to add the built-in updater to versions 0.1.0 and 0.1.1.

Includes .NET 10.0.13; requires Windows 10 build 19041 or newer, x64, and Bluetooth. Corresponding source and SHA-256 checksums are attached; Source.zip is also inside the portable package.

## Validation and limits

386 offline core checks and 330 UI/window/tray checks, including isolated update installation, partial failure/rollback, and a separate-process wait/restart test. Tests do not change headphone settings or enable real Windows startup.

Updates require a writable, nonlinked app folder. The app is unsigned; integrity is verified against GitHub release metadata and the packaged manifest, without claiming code-signing authentication.

Headphone (a), firmware 1.0.1.51, is hardware-tested; the other ten profiles remain experimental. See the repository's device-support matrix. Firmware updating and proprietary/phone-only features remain outside this release.

Code is AGPL-3.0-or-later. Nothing names and product photographs belong to their respective owners, separately from the code license. Runtime licenses and third-party notices are bundled. This project is not affiliated with or endorsed by Nothing.

