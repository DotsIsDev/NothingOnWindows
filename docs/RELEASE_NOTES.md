# Nothing on Windows v0.1.4

## Startup update checks

- Added **Settings → Updates → Check for updates at startup**, using the existing glass toggle style. It defaults to off and preserves existing settings.
- Enabling checks immediately; later launches check once in the background, including when Start with Windows opens the app in the tray.
- New releases show a clickable Windows tray notification and a persistent in-app update notice. Both open Settings to review and install the release. Windows notification settings may suppress the tray popup; the in-app notice stays visible.
- Each release is notified once per app run. Up-to-date checks and background errors stay quiet. Disabling the preference during a pending check suppresses its notification, and closing cancels the request.
- Automatic checks never download or install anything. Use **Download and restart** when ready. Manual checks remain available when the toggle is off.

## Install

In your existing app, use **Settings → Updates → Check for updates**, then **Download and restart**. Alternatively, extract the entire **NothingOnWindows-v0.1.4-win-x64.zip** and open `NothingOnWindows/NothingOnWindows.exe` after exiting the previous copy.

Includes .NET 10.0.13. Requires Windows 10 build 19041 or newer, x64, and Bluetooth. Corresponding source and SHA-256 checksums are attached.

## Validation

390 core checks and 353 UI/window/tray checks pass, including settings migration, automatic startup gating, duplicate-request prevention, notification deduplication and navigation, quiet errors, cancellation, and minimum-size layouts with an update notice. Notification tests use a fake callback and do not show fake release popups or change headphone settings.

The app remains unsigned. Headphone (a), firmware 1.0.1.51, is hardware-tested; the other ten device profiles are experimental. Code is AGPL-3.0-or-later. Nothing names and product photographs remain separate from the code license. Runtime licenses and third-party notices are bundled. This project is unofficial and not endorsed by Nothing.
