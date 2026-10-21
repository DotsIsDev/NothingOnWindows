# Nothing on Windows v0.1.1

Automatic connection and Windows startup support for the unofficial Nothing audio companion.

## New

- Automatically opens the control connection for an already connected Nothing Bluetooth device, at app launch and after later Bluetooth connections or drops. Checks every five seconds; failed control connections retry after 30 seconds.
- Prefers your last successful device when several are connected. Renamed devices retain their selected model. Ambiguous devices still use the manual chooser.
- Choosing Disconnect pauses automatic connection until you reconnect manually or restart the app.
- **Settings → Application → Start with Windows**: optional, off by default, launches quietly in the tray at sign-in. Registration is per Windows account and needs no administrator access. Enable it again after moving the extracted app folder.
- Shutdown cancels pending Bluetooth connection work. Diagnostics and offline checks do not auto-connect unless explicitly requested.

## Download

- **NothingOnWindows-v0.1.1-win-x64.zip**: portable Windows x64 app including .NET 10.0.13. Extract the whole ZIP and open `NothingOnWindows/NothingOnWindows.exe`. Requires Windows 10 build 19041 or newer and Bluetooth.
- **NothingOnWindows-v0.1.1-source.zip**: corresponding source, also included as `Source.zip` inside the portable package.
- **SHA256SUMS.txt**: checksums for both archives.

Exit the older app from its tray menu before opening the update. The app remains unsigned.

## Validation and compatibility

341 offline core checks and 323 UI/window/tray checks. Startup tests use an isolated fake service and never enable real Windows startup. Packaging sends no headphone writes.

Headphone (a), firmware 1.0.1.51, is the only hardware-tested model. The other ten profiles remain experimental. See [device support](https://github.com/DotsIsDev/NothingOnWindows/blob/main/DEVICE_SUPPORT.md) for firmware requirements and limitations.

Code is AGPL-3.0-or-later. Runtime licenses and third-party attribution are bundled. Nothing product photos and names belong to their respective owners and are separate from the code license. This project is not affiliated with or endorsed by Nothing.

Live automatic discovery and control connection were verified with Headphone (a) connected to Windows. Device settings were read without any headphone writes.

